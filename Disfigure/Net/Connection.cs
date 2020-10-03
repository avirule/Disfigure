#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public delegate ValueTask<(bool, SequencePosition, TPacket)> PacketFactoryAsync<TPacket>(ReadOnlySequence<byte> sequence,
        EncryptionProvider encryptionProvider, CancellationToken cancellationToken) where TPacket : IPacket;

    public delegate ValueTask<(bool, byte[])> PacketEncryptor<in TPacket>(EncryptionProvider encryptionProvider, TPacket packet,
        CancellationToken cancellationToken);

    public delegate ValueTask ConnectionEventHandler<TPacket>(Connection<TPacket> connection) where TPacket : IPacket;

    public delegate ValueTask PacketEventHandler<TPacket>(Connection<TPacket> origin, TPacket packet) where TPacket : IPacket;

    public class Connection<TPacket> : IDisposable, IEquatable<Connection<TPacket>> where TPacket : IPacket
    {
        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Writer;
        private readonly PipeReader _Reader;
        private readonly EncryptionProvider _EncryptionProvider;
        private readonly PacketFactoryAsync<TPacket> _PacketFactoryAsync;

        /// <summary>
        ///     Unique identity of <see cref="Connection" />.
        /// </summary>
        public Guid Identity { get; }

        /// <summary>
        ///     <see cref="EndPoint" /> the internal <see cref="TcpClient" /> is connected to.
        /// </summary>
        public EndPoint RemoteEndPoint { get; }

        public Connection(TcpClient client, PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Writer = PipeWriter.Create(_Stream);
            _Reader = PipeReader.Create(_Stream);
            _EncryptionProvider = new EncryptionProvider();
            _PacketFactoryAsync = packetFactoryAsync;

            Identity = Guid.NewGuid();
            RemoteEndPoint = _Client.Client.RemoteEndPoint;
        }

        /// <summary>
        ///     Finalizes <see cref="Connection" />, completing encryption handshake and starting the socket listener.
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe.</param>
        public async ValueTask Finalize(CancellationToken cancellationToken)
        {
            await OnConnected().ConfigureAwait(false);

            await SendEncryptionKeysAsync(cancellationToken).ConfigureAwait(false);

            BeginListen(cancellationToken);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Waiting for encryption keys packet."));
            _EncryptionProvider.WaitForRemoteKeys(cancellationToken);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Connection finalized."));
        }


        #region Reading Data

        private void BeginListen(CancellationToken cancellationToken) => Task.Run(() => ReadLoopAsync(cancellationToken), cancellationToken);

        private async ValueTask ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Beginning read loop."));

                Stopwatch stopwatch = new Stopwatch();

                while (!cancellationToken.IsCancellationRequested)
                {
                    ReadResult result = await _Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    ReadOnlySequence<byte> sequence = result.Buffer;

                    if (sequence.IsEmpty)
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                            "Received no data from reader. This is likely a connection error, so the loop will halt."));
                        break;
                    }

                    stopwatch.Restart();

                    (bool success, SequencePosition consumed, TPacket packet) = await _PacketFactoryAsync(sequence, _EncryptionProvider,
                        cancellationToken);

                    if (!success)
                    {
                        continue;
                    }

                    DiagnosticsProvider.CommitData<PacketDiagnosticGroup>(new ConstructionTime(stopwatch.Elapsed));

                    await PacketReceivedCallback(packet).ConfigureAwait(false);

                    _Reader.AdvanceTo(consumed, consumed);
                }
            }
            catch (IOException exception) when (exception.InnerException is SocketException)
            {
                Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Connection forcibly closed."));
            }
            catch (Exception exception)
            {
                Log.Error(exception.ToString());
            }
            finally
            {
                await OnDisconnected().ConfigureAwait(false);
            }
        }

        #endregion


        #region Writing Data

        public async ValueTask WriteAsync(PacketType type, DateTime utcTimestamp, byte[] content, CancellationToken cancellationToken)
        {
            await WriteEncryptedAsync(type, utcTimestamp, content, cancellationToken).ConfigureAwait(false);
            await _Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(IEnumerable<(PacketType, DateTime, byte[])> packets, CancellationToken cancellationToken)
        {
            foreach ((PacketType type, DateTime timestamp, byte[] content) in packets)
            {
                await WriteEncryptedAsync(type, timestamp, content, cancellationToken).ConfigureAwait(false);
            }

            await _Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteEncryptedAsync(PacketType packetType, DateTime utcTimestamp, byte[] content, CancellationToken cancellationToken)
        {
            Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Write call: {packetType} | {utcTimestamp} | {content.Length}"));

            Stopwatch stopwatch = DiagnosticsProvider.Stopwatches.Rent();
            stopwatch.Restart();

            byte[] initializationVector, encryptedPacket;
            (initializationVector, encryptedPacket) = await EncryptTransmissionAsync(packetType, utcTimestamp, content, cancellationToken)
                .ConfigureAwait(false);

            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Encrypted packet in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms."));

            stopwatch.Restart();

            const int header_length = sizeof(int) + EncryptionProvider.INITIALIZATION_VECTOR_SIZE;

            byte[] data = new byte[header_length + encryptedPacket.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, data, 0, sizeof(int));
            Buffer.BlockCopy(initializationVector, 0, data, sizeof(int), initializationVector.Length);
            Buffer.BlockCopy(encryptedPacket, 0, data, header_length, encryptedPacket.Length);

            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Serialized packet in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms."));

            stopwatch.Reset();
            DiagnosticsProvider.Stopwatches.Return(stopwatch);

            await _Writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<(byte[], byte[])> EncryptTransmissionAsync(PacketType packetType, DateTime utcTimestamp, byte[] content,
            CancellationToken cancellationToken)
        {
            byte[] unencryptedPacket = new byte[sizeof(PacketType) + sizeof(long) + content.Length];

            unencryptedPacket[0] = (byte)packetType;
            Buffer.BlockCopy(BitConverter.GetBytes(utcTimestamp.Ticks), 0, unencryptedPacket, sizeof(PacketType), sizeof(long));
            Buffer.BlockCopy(content, 0, unencryptedPacket, sizeof(PacketType) + sizeof(long), content.Length);

            byte[] initializationVector, encryptedPacket;
            (initializationVector, encryptedPacket) =
                await _EncryptionProvider.EncryptAsync(unencryptedPacket, cancellationToken).ConfigureAwait(false);

            return (initializationVector, encryptedPacket);
        }

        private async ValueTask SendEncryptionKeysAsync(CancellationToken cancellationToken)
        {
            if (_EncryptionProvider.IsEncryptable())
            {
                Log.Warning("Protocol requires that key exchanges happen ONLY ONCE.");
            }
            else
            {
                Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Sending encryption keys."));

                byte[] serialized = new byte[sizeof(int) + EncryptionProvider.PUBLIC_KEY_SIZE];

                Buffer.BlockCopy(BitConverter.GetBytes(int.MinValue), 0, serialized, 0, sizeof(int));
                Buffer.BlockCopy(_EncryptionProvider.PublicKey, 0, serialized, sizeof(int), _EncryptionProvider.PublicKey.Length);

                await _Stream.WriteAsync(serialized, cancellationToken).ConfigureAwait(false);
                await _Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler<TPacket>? Connected;
        public event ConnectionEventHandler<TPacket>? Disconnected;

        private async ValueTask OnConnected()
        {
            if (Connected is { })
            {
                await Connected(this).ConfigureAwait(false);
            }
        }

        private async ValueTask OnDisconnected()
        {
            if (Disconnected is { })
            {
                await Disconnected(this).ConfigureAwait(false);
            }
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler<TPacket>? PacketReceived;

        private async ValueTask PacketReceivedCallback(TPacket packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived(this, packet).ConfigureAwait(false);
            }
        }

        #endregion


        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _Client.Dispose();
            _Stream.Dispose();

            _Disposed = true;
        }

        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion


        #region IEquatable<Connection>

        public bool Equals(Connection<TPacket>? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Identity.Equals(other.Identity);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Connection<TPacket>)obj);
        }

        public override int GetHashCode() => Identity.GetHashCode();

        public static bool operator ==(Connection<TPacket>? left, Connection<TPacket>? right) => Equals(left, right);

        public static bool operator !=(Connection<TPacket>? left, Connection<TPacket>? right) => !Equals(left, right);

        #endregion
    }
}
