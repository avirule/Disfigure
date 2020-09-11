#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public delegate ValueTask ConnectionEventHandler(Connection connection);

    public class Connection : IDisposable
    {
        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Input;
        private readonly PipeReader _Output;
        private readonly EncryptionProvider _EncryptionProvider;
        private readonly Dictionary<PacketType, ManualResetEvent> _PacketResetEvents;

        public Guid Guid { get; }
        public string Name { get; }
        public bool IsOwnerServer { get; }

        public byte[] PublicKey => _EncryptionProvider.PublicKey;

        public Connection(Guid guid, TcpClient client, bool isOwnerServer)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Input = PipeWriter.Create(_Stream);
            _Output = PipeReader.Create(_Stream);
            _EncryptionProvider = new EncryptionProvider();

            Guid = guid;
            Name = string.Empty;
            IsOwnerServer = isOwnerServer;
            _PacketResetEvents = new Dictionary<PacketType, ManualResetEvent>
            {
                { PacketType.EncryptionKeys, new ManualResetEvent(false) },
                { PacketType.BeginIdentity, new ManualResetEvent(false) },
                { PacketType.EndIdentity, new ManualResetEvent(false) },
            };
        }

        public async ValueTask Finalize(CancellationToken cancellationToken)
        {
            await OnConnected();

            await SendEncryptionKeys(IsOwnerServer, cancellationToken);

            BeginListen(cancellationToken);

            Log.Debug($"Waiting for {nameof(PacketType.EncryptionKeys)} packet from {_Client.Client.RemoteEndPoint}.");
            WaitForPacket(PacketType.EncryptionKeys);

            Log.Debug($"Connection to {_Client.Client.RemoteEndPoint} finalized.");
        }

        public void WaitForPacket(PacketType packetType)
        {
            _PacketResetEvents[packetType].WaitOne();
        }

        private async ValueTask SendEncryptionKeys(bool server, CancellationToken cancellationToken)
        {
            Debug.Assert(!_EncryptionProvider.EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            Log.Debug($"Sending encryption keys to {_Client.Client.RemoteEndPoint}.");

            Packet packet = new Packet(PacketType.EncryptionKeys, _EncryptionProvider.PublicKey, DateTime.UtcNow,
                server ? _EncryptionProvider.IV : Array.Empty<byte>());
            await _Stream.WriteAsync(packet.Serialize(), cancellationToken).ConfigureAwait(false);
            await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        #region Listening

        private void BeginListen(CancellationToken cancellationToken) =>
            Task.Run(() => BeginListenAsyncInternal(cancellationToken), cancellationToken);

        private async Task BeginListenAsyncInternal(CancellationToken cancellationToken)
        {
            try
            {
                await ReadLoopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                Log.Debug(ex.ToString());
                Log.Warning($"Connection to {_Client.Client.RemoteEndPoint} forcibly closed connection.");

                await OnDisconnected();
            }
            catch (Exception exception)
            {
                Log.Error(exception.ToString());
            }
        }

        private async ValueTask ReadLoopAsync(CancellationToken cancellationToken)
        {
            Log.Debug($"Beginning read loop for connection to {_Client.Client.RemoteEndPoint}.");

            Stopwatch stopwatch = new Stopwatch();

            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await _Output.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> sequence = result.Buffer;

                stopwatch.Restart();

                if (!TryReadPacket(sequence, out SequencePosition consumed, out Packet packet))
                {
                    continue;
                }

                stopwatch.Stop();
                DiagnosticsProvider.CommitData<PacketDiagnosticGroup, TimeSpan>(new ConstructionTime(stopwatch.Elapsed));

                await OnPacketReceived(packet, cancellationToken, stopwatch).ConfigureAwait(false);
                _Output.AdvanceTo(consumed, sequence.End);
            }
        }

        private static bool TryReadPacket(ReadOnlySequence<byte> sequence, [NotNull] out SequencePosition consumed,
            [NotNullWhen(true)] out Packet packet)
        {
            int packetLength = BitConverter.ToInt32(sequence.Slice(0, sizeof(int)).FirstSpan);

            if (sequence.Length < packetLength)
            {
                consumed = sequence.Start;
                packet = default;

                return false;
            }
            else
            {
                consumed = sequence.GetPosition(packetLength);
                packet = new Packet(sequence, packetLength);

                return true;
            }
        }

        #endregion


        #region Writing Data

        public async ValueTask WriteAsync(PacketType type, DateTime timestamp, byte[] content, CancellationToken cancellationToken)
        {
            await WriteEncryptedAsync(new Packet(type, PublicKey, timestamp, content), cancellationToken);
            await _Input.FlushAsync(cancellationToken);
        }

        public async ValueTask WriteAsync(IEnumerable<(PacketType, DateTime, byte[])> packets, CancellationToken cancellationToken)
        {
            foreach ((PacketType type, DateTime timestamp, byte[] content) in packets)
            {
                await WriteEncryptedAsync(new Packet(type, PublicKey, timestamp, content), cancellationToken);
            }

            await _Input.FlushAsync(cancellationToken);
        }

        private async ValueTask WriteEncryptedAsync(Packet packet, CancellationToken cancellationToken)
        {
            packet.Content = await _EncryptionProvider.Encrypt(packet.Content, cancellationToken);
            byte[] serialized = packet.Serialize();
            await _Input.WriteAsync(serialized, cancellationToken);

            Log.Verbose($"OUT {packet}");
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Connected;
        public event ConnectionEventHandler? Disconnected;

        private async ValueTask OnConnected()
        {
            if (Connected is { })
            {
                await Connected.Invoke(this);
            }
        }

        private async ValueTask OnDisconnected()
        {
            if (Disconnected is { })
            {
                await Disconnected.Invoke(this);
            }
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler? PacketReceived;
        public event PacketEventHandler? TextPacketReceived;
        public event PacketEventHandler? BeginIdentityReceived;
        public event PacketEventHandler? ChannelIdentityReceived;
        public event PacketEventHandler? EndIdentityReceived;

        private async ValueTask OnPacketReceived(Packet packet, CancellationToken cancellationToken, Stopwatch stopwatch)
        {
            if (_PacketResetEvents.TryGetValue(packet.Type, out ManualResetEvent? resetEvent))
            {
                resetEvent.Set();
            }

            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    _EncryptionProvider.AssignRemoteKeys(packet.Content, packet.PublicKey);
                    break;
                default:
                    stopwatch.Restart();

                    packet.Content = await _EncryptionProvider.Decrypt(packet.PublicKey, packet.Content, cancellationToken);

                    stopwatch.Stop();
                    DiagnosticsProvider.CommitData<PacketDiagnosticGroup, TimeSpan>(new DecryptionTime(stopwatch.Elapsed));

                    await InvokePacketTypeEvent(packet);
                    break;
            }

            Log.Verbose($"INC {packet}");
        }

        private async ValueTask InvokePacketTypeEvent(Packet packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived.Invoke(this, packet);
            }

            switch (packet.Type)
            {
                case PacketType.Text when TextPacketReceived is { }:
                    await TextPacketReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.BeginIdentity when BeginIdentityReceived is { }:
                    await BeginIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.EndIdentity when EndIdentityReceived is { }:
                    await EndIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.ChannelIdentity when ChannelIdentityReceived is { }:
                    await ChannelIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
            }
        }

        #endregion


        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                _Client.Dispose();
                _Stream.Dispose();
            }

            _Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
