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

        public Guid Guid { get; }
        public string Name { get; }
        public bool IsOwnerServer { get; }
        public Dictionary<PacketType, ManualResetEvent> PacketResetEvents { get; }

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
            PacketResetEvents = new Dictionary<PacketType, ManualResetEvent>
            {
                { PacketType.EncryptionKeys, new ManualResetEvent(false) },
                { PacketType.BeginIdentity, new ManualResetEvent(false) },
                { PacketType.EndIdentity, new ManualResetEvent(false) },
            };
        }

        public async ValueTask Finalize(CancellationToken cancellationToken)
        {
            await SendEncryptionKeys(IsOwnerServer, cancellationToken);

            await Task.Run(() => BeginListenAsync(cancellationToken), cancellationToken);

            Log.Debug($"Waiting for encryption keys from {_Client.Client.RemoteEndPoint}.");
            PacketResetEvents[PacketType.EncryptionKeys].WaitOne();

            Log.Debug($"Connection to {_Client.Client.RemoteEndPoint} finalized.");
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

        private async Task BeginListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ReadLoopAsync(cancellationToken);
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                Log.Debug(ex.ToString());
                Log.Warning($"Connection at {_Client.Client.RemoteEndPoint} ({Guid}) forcibly closed connection.");

                if (Disconnected is { })
                {
                    await Disconnected.Invoke(this);
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception.ToString());
            }
        }

        private async ValueTask ReadLoopAsync(CancellationToken cancellationToken)
        {
            Log.Debug($"Beginning read loop for connection to {_Client.Client.RemoteEndPoint}.");

            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await _Output.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> sequence = result.Buffer;

                if (!TryReadPacket(sequence, out SequencePosition consumed, out Packet? packet))
                {
                    continue;
                }

                await OnPacketReceived(packet, cancellationToken);
                _Output.AdvanceTo(consumed, sequence.End);
            }
        }

        private static bool TryReadPacket(ReadOnlySequence<byte> sequence, [NotNull] out SequencePosition consumed,
            [NotNullWhen(true)] out Packet? packet)
        {
            int packetLength = BitConverter.ToInt32(sequence.Slice(0, sizeof(int)).FirstSpan);

            if (sequence.Length < packetLength)
            {
                consumed = sequence.Start;
                packet = null;

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

        public event ConnectionEventHandler? Disconnected;

        #endregion


        #region Packet Events

        public event PacketEventHandler? PacketReceived;
        public event PacketEventHandler? TextPacketReceived;
        public event PacketEventHandler? BeginIdentityReceived;
        public event PacketEventHandler? ChannelIdentityReceived;
        public event PacketEventHandler? EndIdentityReceived;

        private async ValueTask OnPacketReceived(Packet packet, CancellationToken cancellationToken)
        {
            if (PacketResetEvents.TryGetValue(packet.Type, out ManualResetEvent? resetEvent))
            {
                resetEvent.Set();
            }

            if (packet.Type == PacketType.EncryptionKeys)
            {
                // todo error check keys

                _EncryptionProvider.AssignRemoteKeys(packet.Content, packet.PublicKey);
                Log.Debug($"Received encryption keys from {_Client.Client.RemoteEndPoint}.");
            }
            else
            {
                packet.Content = await _EncryptionProvider.Decrypt(packet.PublicKey, packet.Content, cancellationToken);

                await InvokePacketTypeEvent(packet);
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
