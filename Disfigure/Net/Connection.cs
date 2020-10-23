#region

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticsProviderNS;
using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Disfigure.Net.Packets;
using Serilog;

#endregion


namespace Disfigure.Net
{
    public delegate ValueTask ConnectionEventHandler(Connection connection);

    public delegate ValueTask PacketEventHandler(Connection connection, Packet packet);

    public class Connection : IDisposable, IEquatable<Connection>
    {
        private readonly TcpClient _Client;
        private readonly IEncryptionProvider? _EncryptionProvider;
        private readonly PipeReader _Reader;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Writer;

        public Connection(TcpClient client, IEncryptionProvider? encryptionProvider)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Writer = PipeWriter.Create(_Stream);
            _Reader = PipeReader.Create(_Stream);
            _EncryptionProvider = encryptionProvider;

            Identity = Guid.NewGuid();
        }

        /// <summary>
        ///     Unique identity of the <see cref="Connection" />.
        /// </summary>
        public Guid Identity { get; }

        /// <summary>
        ///     <see cref="EndPoint" /> the internal <see cref="TcpClient" /> is connected to.
        /// </summary>
        public EndPoint RemoteEndPoint => _Client.Client.RemoteEndPoint;

        /// <summary>
        ///     Invokes <see cref="Connected" /> event and begins read loop.
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe.</param>
        public async ValueTask FinalizeAsync(CancellationToken cancellationToken)
        {
            await OnConnected();

            BeginListen(cancellationToken);
        }

        public TEncryptionProvider EncryptionProviderAs<TEncryptionProvider>() where TEncryptionProvider : class, IEncryptionProvider
            => _EncryptionProvider as TEncryptionProvider
               ?? throw new InvalidCastException($"Cannot cast {typeof(IEncryptionProvider)} to {typeof(TEncryptionProvider)}");


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
                        Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                            "Received no data from reader. This is likely a connection error, so the loop will halt."));

                        break;
                    }

                    stopwatch.Restart();

                    (bool success, SequencePosition consumed, Packet packet) = await ReadPacketAsync(sequence, cancellationToken);

                    if (success)
                    {
                        DiagnosticsProvider.CommitData<PacketDiagnosticGroup, TimeSpan>(new ConstructionTime(stopwatch.Elapsed));

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

        private async ValueTask<(bool, SequencePosition, Packet)> ReadPacketAsync(ReadOnlySequence<byte> sequence, CancellationToken cancellationToken)
        {
            static bool TryGetDataImpl(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out ReadOnlyMemory<byte> data)
            {
                ReadOnlySpan<byte> span = sequence.FirstSpan;
                consumed = sequence.Start;
                data = ReadOnlyMemory<byte>.Empty;
                int length;

                // wait until 4 bytes and sequence is long enough to construct packet
                if ((sequence.Length < sizeof(int)) || (sequence.Length < (length = MemoryMarshal.Read<int>(span)))) return false;

                // ensure length covers entire valid header
                else if (length < Packet.TOTAL_HEADER_LENGTH)
                {
                    // if not, print warning and throw away data
                    Log.Warning("Received packet with invalid header format (too short).");
                    consumed = sequence.GetPosition(length);
                    return false;
                }

                // ensure alignment constant is valid
                else if (MemoryMarshal.Read<int>(span.Slice(sizeof(int))) != Packet.ALIGNMENT_CONSTANT) throw new PacketMisalignedException();
                else
                {
                    consumed = sequence.GetPosition(length);
                    data = sequence.Slice(sizeof(int) + sizeof(int)).First;
                    return true;
                }
            }

            if (!TryGetDataImpl(sequence, out SequencePosition consumed, out ReadOnlyMemory<byte> data)) return (false, consumed, default);
            else
            {
                ReadOnlyMemory<byte> initializationVector = data.Slice(0, IEncryptionProvider.INITIALIZATION_VECTOR_SIZE);
                ReadOnlyMemory<byte> packetData = data.Slice(IEncryptionProvider.INITIALIZATION_VECTOR_SIZE);

                if (_EncryptionProvider?.IsEncryptable ?? false) packetData = await _EncryptionProvider.DecryptAsync(initializationVector, packetData, cancellationToken);
                else Log.Warning($"Packet data has been received, but the {nameof(IEncryptionProvider)} is in an unusable state.");

                return (true, consumed, new Packet(packetData));
            }
        }

        #endregion


        #region Writing Data

        private static ReadOnlyMemory<byte> SerializePacket(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> packetData)
        {
            int alignmentConstant = Packet.ALIGNMENT_CONSTANT;
            int length = Packet.ENCRYPTION_HEADER_LENGTH + packetData.Length;
            Memory<byte> data = new byte[length];
            Span<byte> dataSpan = data.Span;

            MemoryMarshal.Write(dataSpan.Slice(Packet.OFFSET_DATA_LENGTH), ref length);
            MemoryMarshal.Write(dataSpan.Slice(Packet.OFFSET_ALIGNMENT_CONSTANT), ref alignmentConstant);
            initializationVector.CopyTo(data.Slice(Packet.OFFSET_INITIALIZATION_VECTOR));
            packetData.CopyTo(data.Slice(Packet.ENCRYPTION_HEADER_LENGTH));

            return data;
        }

        private async ValueTask<ReadOnlyMemory<byte>> EncryptPacket(Packet packet, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> initializationVector, packetData = packet.Serialize();

            if (_EncryptionProvider is not null) (initializationVector, packetData) = await _EncryptionProvider!.EncryptAsync(packetData, cancellationToken);
            else
            {
                throw new ArgumentException($"Write call has been received, but the {nameof(IEncryptionProvider)} is in an unusable state.",
                    nameof(_EncryptionProvider));
            }

            return SerializePacket(initializationVector, packetData);
        }

        public async ValueTask WriteDirectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            await _Writer.WriteAsync(data, cancellationToken);
            await _Writer.FlushAsync(cancellationToken);

            Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"DIRECT OUT {data.Length} BYTES"));
        }

        public async ValueTask WriteAsync(Packet packet, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> encrypted = await EncryptPacket(packet, cancellationToken);
            await _Writer.WriteAsync(encrypted, cancellationToken);
            await _Writer.FlushAsync(cancellationToken);

            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"OUT {packet}"));

            await OnPacketWrittenAsync(packet);
        }

        public async ValueTask SendEncryptionKeys(CancellationToken cancellationToken)
        {
            if (_EncryptionProvider is not null)
            {
                await WriteDirectAsync(SerializePacket(ReadOnlyMemory<byte>.Empty,
                    Packet.Create(PacketType.EncryptionKeys, DateTime.UtcNow, _EncryptionProvider?.PublicKey).Serialize()), cancellationToken);
            }
            else
            {
                throw new ArgumentException($"Write call has been received, but the {nameof(IEncryptionProvider)} is in an unusable state.",
                    nameof(_EncryptionProvider));
            }
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Connected;

        public event ConnectionEventHandler? Disconnected;

        private async ValueTask OnConnected()
        {
            if (Connected is not null) await Connected(this);
        }

        private async ValueTask OnDisconnected()
        {
            if (Disconnected is not null) await Disconnected(this);
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler? PacketWritten;

        public event PacketEventHandler? PacketReceived;

        private async ValueTask OnPacketWrittenAsync(Packet packet)
        {
            if (PacketWritten is not null) await PacketWritten(this, packet);
        }

        private async ValueTask OnPacketReceivedAsync(Packet packet)
        {
            if (PacketReceived is not null) await PacketReceived(this, packet);
        }

        #endregion


        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            _Client.Dispose();
            _Stream.Dispose();

            _Disposed = true;
        }

        public void Dispose()
        {
            if (_Disposed) return;

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion


        #region IEquatable<Connection>

        public bool Equals(Connection? other)
        {
            if (ReferenceEquals(null, other)) return false;

            if (ReferenceEquals(this, other)) return true;

            return Identity.Equals(other.Identity);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;

            if (ReferenceEquals(this, obj)) return true;

            if (obj.GetType() != GetType()) return false;

            return Equals((Connection)obj);
        }

        public override int GetHashCode() => Identity.GetHashCode();

        public static bool operator ==(Connection? left, Connection? right) =>
            Equals(left, right);

        public static bool operator !=(Connection? left, Connection? right) =>
            !Equals(left, right);

        #endregion
    }
}
