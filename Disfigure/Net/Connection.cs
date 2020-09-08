#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        private readonly EncryptionProvider _EncryptionProvider;
        private readonly PackerReader _PackerReader;
        private readonly ManualResetEvent _KeysExchanged;

        private long _CompleteRemoteIdentity;

        public Guid Guid { get; }
        public string Name { get; }

        public bool CompleteRemoteIdentity
        {
            get => Interlocked.Read(ref _CompleteRemoteIdentity) == 1;
            private set => Interlocked.Exchange(ref _CompleteRemoteIdentity, Unsafe.As<bool, long>(ref value));
        }

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _EncryptionProvider = new EncryptionProvider();
            _PackerReader = new PackerReader(_Stream);
            _PackerReader.EncryptedPacketReceived += OnEncryptedPacketReceived;

            EndIdentityReceived += (origin, packet) =>
            {
                CompleteRemoteIdentity = true;
                return default;
            };

            Guid = guid;
            Name = string.Empty;
            _KeysExchanged = new ManualResetEvent(false);
        }

        public async ValueTask SendEncryptionKeys(CancellationToken cancellationToken)
        {
            Debug.Assert(!_EncryptionProvider.EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            Log.Debug($"Exchanging encryption keys with remote endpoint {_Client.Client.RemoteEndPoint}.");

            byte[] keyExchangePacket = _EncryptionProvider.GenerateKeyExchangePacket();
            await _Stream.WriteAsync(keyExchangePacket, cancellationToken).ConfigureAwait(false);
            await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void WaitForKeyExchange()
        {
            Log.Verbose($"Waiting for encryption keys from {_Client.Client.RemoteEndPoint}.");
            _KeysExchanged.WaitOne();
        }

        #region Listening

        public void BeginListen(CancellationToken cancellationToken) =>
            Task.Run(() => BeginListenAsyncInternal(cancellationToken), cancellationToken);

        private async Task BeginListenAsyncInternal(CancellationToken cancellationToken)
        {
            try
            {
                Log.Debug($"Beginning read loop for connection {Guid}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    await _PackerReader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
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

        #endregion


        #region Writing Data

        public async ValueTask WriteAsync(CancellationToken cancellationToken, Packet packet)
        {
            await EncryptWritePacketAsync(cancellationToken, packet).ConfigureAwait(false);
            await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(CancellationToken cancellationToken, IEnumerable<Packet> packets)
        {
            foreach (Packet packet in packets)
            {
                await EncryptWritePacketAsync(cancellationToken, packet).ConfigureAwait(false);
            }

            await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask EncryptWritePacketAsync(CancellationToken cancellationToken, Packet packet)
        {
            Debug.Assert(_EncryptionProvider.EncryptionNegotiated, "Encryption keys must have been exchanged for recipient to decrypt messages.");

            byte[] encryptedPacket = await EncryptPacketAsync(packet).ConfigureAwait(false);
            await _Stream.WriteAsync(encryptedPacket, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<byte[]> EncryptPacketAsync(Packet packet)
        {
            byte[] encryptedPacket = await _EncryptionProvider.Encrypt(packet.Serialize()).ConfigureAwait(false);
            byte[] finalPacket = new byte[EncryptedPacket.ENCRYPTION_HEADER_LENGTH + encryptedPacket.Length];
            Buffer.BlockCopy(_EncryptionProvider.GenerateHeader(encryptedPacket.Length), 0, finalPacket, 0, EncryptedPacket.ENCRYPTION_HEADER_LENGTH);
            Buffer.BlockCopy(encryptedPacket, 0, finalPacket, EncryptedPacket.ENCRYPTION_HEADER_LENGTH, encryptedPacket.Length);

            return encryptedPacket;
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Connected;
        public event ConnectionEventHandler? Disconnected;

        #endregion


        #region Packet Events

        public event PacketEventHandler? TextPacketReceived;
        public event PacketEventHandler? BeginIdentityReceived;
        public event PacketEventHandler? ChannelIdentityReceived;
        public event PacketEventHandler? EndIdentityReceived;

        private async ValueTask OnEncryptedPacketReceived(Connection connection, EncryptedPacket encryptedPacket)
        {
            switch (encryptedPacket.Type)
            {
                case EncryptedPacketType.Encrypted:
                    byte[] packetDataDecrypted = await _EncryptionProvider.Decrypt(encryptedPacket.PublicKey, encryptedPacket.PacketData);
                    Packet packet = Packet.Deserialize(packetDataDecrypted);
                    await InvokePacketTypeEvent(packet);
                    break;
                case EncryptedPacketType.KeyExchange:
                    Debug.Assert(!_EncryptionProvider.EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

                    byte[] publicKey = encryptedPacket.PublicKey;
                    byte[] iv = encryptedPacket.PacketData;

                    _EncryptionProvider.AssignRemoteKeys(iv, publicKey);
                    _KeysExchanged.Set();

                    Log.Verbose($"Received encryption keys from {_Client.Client.RemoteEndPoint}.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async ValueTask InvokePacketTypeEvent(Packet packet)
        {
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
