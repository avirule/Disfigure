#region

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Disfigure.Net.Packets;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public delegate ValueTask<(bool, SequencePosition, TPacket)> PacketFactoryAsync<TPacket>(ReadOnlySequence<byte> sequence,
        IEncryptionProvider encryptionProvider, CancellationToken cancellationToken);

    public delegate ValueTask<ReadOnlyMemory<byte>> PacketSerializerAsync<in TPacket>(TPacket packet, IEncryptionProvider encryptionProvider,
        CancellationToken cancellationToken);

    public delegate ValueTask ConnectionEventHandler<TEncryptionProvider, TPacket>(Connection<TEncryptionProvider, TPacket> connection)
        where TEncryptionProvider : class, IEncryptionProvider, new()
        where TPacket : struct, IPacket;

    public delegate ValueTask PacketEventHandler<TEncryptionProvider, TPacket>(Connection<TEncryptionProvider, TPacket> origin, TPacket packet)
        where TEncryptionProvider : class, IEncryptionProvider, new()
        where TPacket : struct, IPacket;

    public class Connection<TEncryptionProvider, TPacket> : IDisposable, IEquatable<Connection<TEncryptionProvider, TPacket>>
        where TEncryptionProvider : class, IEncryptionProvider, new()
        where TPacket : struct, IPacket
    {
        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Writer;
        private readonly PipeReader _Reader;
        private readonly PacketSerializerAsync<TPacket> _PacketSerializerAsync;
        private readonly PacketFactoryAsync<TPacket> _PacketFactoryAsync;

        /// <summary>
        ///     Unique identity of the <see cref="Connection{TPacket}" />.
        /// </summary>
        public Guid Identity { get; }

        public IEncryptionProvider EncryptionProvider { get; }


        /// <summary>
        ///     <see cref="EndPoint" /> the internal <see cref="TcpClient" /> is connected to.
        /// </summary>
        public EndPoint RemoteEndPoint => _Client.Client.RemoteEndPoint;

        public Connection(TcpClient client, PacketSerializerAsync<TPacket> packetSerializerAsync, PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Writer = PipeWriter.Create(_Stream);
            _Reader = PipeReader.Create(_Stream);
            _PacketSerializerAsync = packetSerializerAsync;
            _PacketFactoryAsync = packetFactoryAsync;

            Identity = Guid.NewGuid();
            EncryptionProvider = new TEncryptionProvider();
        }

        /// <summary>
        ///     Finalizes <see cref="Connection{TPacket}" />, completing encryption handshake and starting the socket listener.
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe.</param>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            await OnConnected();

            BeginListen(cancellationToken);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Waiting for encryption keys packet."));
            _ECDHEncryptionProvider.WaitForRemoteKeys(cancellationToken);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Encryption keys received; connection finalized."));
        }


        #region EncryptionProvider Exposition

        public byte[] PublicKey => EncryptionProvider.PublicKey;

        #endregion


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
                    ReadResult result = await _Reader.ReadAsync(cancellationToken);
                    ReadOnlySequence<byte> sequence = result.Buffer;

                    if (sequence.IsEmpty)
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                            "Received no data from reader. This is likely a connection error, so the loop will halt."));
                        break;
                    }

                    stopwatch.Restart();

                    (bool success, SequencePosition consumed, TPacket packet) = await _PacketFactoryAsync(sequence, EncryptionProvider,
                        cancellationToken);

                    if (success)
                    {
                        DiagnosticsProvider.CommitData<PacketDiagnosticGroup>(new ConstructionTime(stopwatch.Elapsed));

                        Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"INC {packet}"));

                        await PacketReceivedCallback(packet);
                    }

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
                await OnDisconnected();
            }
        }

        #endregion


        #region Writing Data

        public async ValueTask WriteAsync(TPacket packet, CancellationToken cancellationToken)
        {
            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"OUT {packet}"));

            ReadOnlyMemory<byte> encrypted = await _PacketSerializerAsync(packet, EncryptionProvider, cancellationToken);
            await _Writer.WriteAsync(encrypted, cancellationToken);
            await _Writer.FlushAsync(cancellationToken);
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler<TEncryptionProvider, TPacket>? Connected;
        public event ConnectionEventHandler<TEncryptionProvider, TPacket>? Disconnected;

        private async ValueTask OnConnected()
        {
            if (Connected is { })
            {
                await Connected(this);
            }
        }

        private async ValueTask OnDisconnected()
        {
            if (Disconnected is { })
            {
                await Disconnected(this);
            }
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler<TEncryptionProvider, TPacket>? PacketReceived;

        private async ValueTask PacketReceivedCallback(TPacket packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived(this, packet);
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

        public bool Equals(Connection<TEncryptionProvider, TPacket>? other)
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

            return Equals((Connection<TEncryptionProvider, TPacket>)obj);
        }

        public override int GetHashCode() => Identity.GetHashCode();

        public static bool operator ==(Connection<TEncryptionProvider, TPacket>? left, Connection<TEncryptionProvider, TPacket>? right) =>
            Equals(left, right);

        public static bool operator !=(Connection<TEncryptionProvider, TPacket>? left, Connection<TEncryptionProvider, TPacket>? right) =>
            !Equals(left, right);

        #endregion
    }
}
