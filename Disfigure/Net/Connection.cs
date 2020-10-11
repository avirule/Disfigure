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
    public delegate Task<ReadOnlyMemory<byte>> PacketSerializerAsync<in TPacket>(TPacket packet, IEncryptionProvider? encryptionProvider,
        CancellationToken cancellationToken);

    public delegate Task<(bool, SequencePosition, TPacket)> PacketFactoryAsync<TPacket>(ReadOnlySequence<byte> sequence,
        IEncryptionProvider? encryptionProvider, CancellationToken cancellationToken);

    public delegate Task ConnectionEventHandler<TPacket>(Connection<TPacket> connection) where TPacket : struct;

    public delegate Task PacketEventHandler<TPacket>(Connection<TPacket> connection, TPacket packet) where TPacket : struct;

    public class Connection<TPacket> : IDisposable, IEquatable<Connection<TPacket>> where TPacket : struct
    {
        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Writer;
        private readonly PipeReader _Reader;
        private readonly IEncryptionProvider? _EncryptionProvider;
        private readonly PacketSerializerAsync<TPacket> _PacketSerializerAsync;
        private readonly PacketFactoryAsync<TPacket> _PacketFactoryAsync;

        /// <summary>
        ///     Unique identity of the <see cref="Connection{TPacket}" />.
        /// </summary>
        public Guid Identity { get; }


        /// <summary>
        ///     <see cref="EndPoint" /> the internal <see cref="TcpClient" /> is connected to.
        /// </summary>
        public EndPoint RemoteEndPoint => _Client.Client.RemoteEndPoint;

        public Connection(TcpClient client, IEncryptionProvider? encryptionProvider, PacketSerializerAsync<TPacket> packetSerializerAsync,
            PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Writer = PipeWriter.Create(_Stream);
            _Reader = PipeReader.Create(_Stream);
            _EncryptionProvider = encryptionProvider;
            _PacketSerializerAsync = packetSerializerAsync;
            _PacketFactoryAsync = packetFactoryAsync;

            Identity = Guid.NewGuid();
        }

        /// <summary>
        ///     Invokes <see cref="Connected" /> event and begins read loop.
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe.</param>
        public async Task FinalizeAsync(CancellationToken cancellationToken)
        {
            await OnConnected();

            BeginListen(cancellationToken);
        }


        public TEncryptionProvider EncryptionProviderAs<TEncryptionProvider>() where TEncryptionProvider : class, IEncryptionProvider
            => _EncryptionProvider as TEncryptionProvider
               ?? throw new InvalidCastException($"Cannot cast {typeof(IEncryptionProvider)} to {typeof(TEncryptionProvider)}");


        #region Reading Data

        private void BeginListen(CancellationToken cancellationToken) => Task.Run(() => ReadLoopAsync(cancellationToken), cancellationToken);

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
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
                        Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                            "Received no data from reader. This is likely a connection error, so the loop will halt."));
                        break;
                    }

                    stopwatch.Restart();

                    (bool success, SequencePosition consumed, TPacket packet) = await _PacketFactoryAsync(sequence, _EncryptionProvider,
                        cancellationToken);

                    if (success)
                    {
                        DiagnosticsProvider.CommitData<PacketDiagnosticGroup>(new ConstructionTime(stopwatch.Elapsed));

                        Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"INC {packet}"));

                        await OnPacketReceivedAsync(packet);
                    }

                    _Reader.AdvanceTo(consumed, consumed);
                }
            }
            catch (IOException exception) when (exception.InnerException is SocketException)
            {
                Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Connection disconnected."));
                Log.Debug(exception.Message);
            }
            catch (PacketMisalignedException)
            {
                Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Packet reader has received a misaligned packet."));
            }
            finally
            {
                await OnDisconnected();
            }
        }

        #endregion


        #region Writing Data

        public async Task WriteAsync(TPacket packet, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> encrypted = await _PacketSerializerAsync(packet, _EncryptionProvider, cancellationToken);
            await _Writer.WriteAsync(encrypted, cancellationToken);
            await _Writer.FlushAsync(cancellationToken);

            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"OUT {packet}"));

            await OnPacketWrittenAsync(packet);
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler<TPacket>? Connected;
        public event ConnectionEventHandler<TPacket>? Disconnected;

        private async Task OnConnected()
        {
            if (Connected is { })
            {
                await Connected(this);
            }
        }

        private async Task OnDisconnected()
        {
            if (Disconnected is { })
            {
                await Disconnected(this);
            }
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler<TPacket>? PacketWritten;
        public event PacketEventHandler<TPacket>? PacketReceived;

        private async Task OnPacketWrittenAsync(TPacket packet)
        {
            if (PacketWritten is { })
            {
                await PacketWritten(this, packet);
            }
        }

        private async Task OnPacketReceivedAsync(TPacket packet)
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

        public static bool operator ==(Connection<TPacket>? left, Connection<TPacket>? right) =>
            Equals(left, right);

        public static bool operator !=(Connection<TPacket>? left, Connection<TPacket>? right) =>
            !Equals(left, right);

        #endregion
    }
}
