#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;
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
        public Dictionary<PacketType, ManualResetEvent> PacketResetEvents { get; }

        public byte[] PublicKey => _EncryptionProvider.PublicKey;

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Input = PipeWriter.Create(_Stream);
            _Output = PipeReader.Create(_Stream);
            _EncryptionProvider = new EncryptionProvider();

            Guid = guid;
            Name = string.Empty;
            PacketResetEvents = new Dictionary<PacketType, ManualResetEvent>
            {
                { PacketType.EncryptionKeys, new ManualResetEvent(false) },
                { PacketType.BeginIdentity, new ManualResetEvent(false) },
                { PacketType.EndIdentity, new ManualResetEvent(false) },
            };
        }

        public async ValueTask SendEncryptionKeys(CancellationToken cancellationToken)
        {
            Debug.Assert(!_EncryptionProvider.EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            Log.Debug($"Exchanging encryption keys with remote endpoint {_Client.Client.RemoteEndPoint}.");

            Packet packet = new Packet(PacketType.EncryptionKeys, _EncryptionProvider.PublicKey, DateTime.UtcNow, _EncryptionProvider.IV);
            await _Stream.WriteAsync(packet.Serialize(), cancellationToken).ConfigureAwait(false);
            await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        #region Listening

        public void BeginListen(CancellationToken cancellationToken) =>
            Task.Run(() => ListenAsyncInternal(cancellationToken), cancellationToken);

        private async Task ListenAsyncInternal(CancellationToken cancellationToken)
        {
            try
            {
                Log.Debug($"Beginning read loop for connection {Guid}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    ReadResult result = await _Output.ReadAsync(cancellationToken);
                    ReadOnlySequence<byte> sequence = result.Buffer;

                    if (!TryReadPacket(sequence, out SequencePosition consumed, out Packet packet))
                    {
                        continue;
                    }

                    await InvokePacketTypeEvent(packet);
                    _Output.AdvanceTo(consumed, sequence.End);
                }
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

        private static bool TryReadPacket(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out Packet packet)
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
                ReadOnlySequence<byte> packetTypeSequence = sequence.Slice(Packet.OFFSET_PACKET_TYPE, sizeof(byte));
                ReadOnlySequence<byte> publicKeySequence = sequence.Slice(Packet.OFFSET_PUBLIC_KEY, EncryptionProvider.PUBLIC_KEY_SIZE);
                ReadOnlySequence<byte> timestampSequence = sequence.Slice(Packet.OFFSET_TIMESTAMP, sizeof(long));
                ReadOnlySequence<byte> contentSequence = sequence.Slice(Packet.HEADER_LENGTH, packetLength - Packet.HEADER_LENGTH);

                PacketType packetType = (PacketType)packetTypeSequence.FirstSpan[0];
                byte[] publicKey = publicKeySequence.ToArray();
                DateTime utcTimestamp = DateTime.FromBinary(BitConverter.ToInt64(timestampSequence.FirstSpan));
                byte[] content = contentSequence.ToArray();

                packet = new Packet(packetType, publicKey, utcTimestamp, content);
                consumed = sequence.GetPosition(packetLength);

                return true;
            }
        }

        #endregion


        #region Writing Data

        public async ValueTask WriteAsync(Packet packet, CancellationToken cancellationToken)
        {
            await WriteEncryptedAsync(packet, cancellationToken);
            await _Input.FlushAsync(cancellationToken);
        }

        public async ValueTask WriteAsync(IEnumerable<Packet> packets, CancellationToken cancellationToken)
        {
            foreach (Packet packet in packets)
            {
                await WriteEncryptedAsync(packet, cancellationToken);
            }

            await _Input.FlushAsync(cancellationToken);
        }

        private async ValueTask WriteEncryptedAsync(Packet packet, CancellationToken cancellationToken)
        {
            packet.Content = await _EncryptionProvider.Encrypt(packet.Content, cancellationToken);
            await _Input.WriteAsync(packet.Serialize(), cancellationToken);
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Disconnected;

        #endregion


        #region Packet Events

        public event PacketEventHandler? TextPacketReceived;
        public event PacketEventHandler? BeginIdentityReceived;
        public event PacketEventHandler? ChannelIdentityReceived;
        public event PacketEventHandler? EndIdentityReceived;

        private async ValueTask InvokePacketTypeEvent(Packet packet)
        {
            if (packet.Type == PacketType.EncryptionKeys)
            {
                // todo error check keys

                _EncryptionProvider.AssignRemoteKeys(packet.Content, packet.PublicKey);
                Log.Debug($"Received encryption keys from {_Client.Client.RemoteEndPoint}.");
                return;
            }
            else
            {
                packet.Content = await _EncryptionProvider.Decrypt(packet.PublicKey, packet.Content);
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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (PacketResetEvents.TryGetValue(packet.Type, out ManualResetEvent? resetEvent))
            {
                resetEvent.Set();
            }
        }

        #endregion


        #region Dispose

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
