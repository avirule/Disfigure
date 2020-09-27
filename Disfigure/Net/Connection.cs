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
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Collections;
using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public delegate ValueTask ConnectionEventHandler(Connection connection);

    public class Connection : IDisposable, IEquatable<Connection>
    {
        private static readonly ObjectPool<Stopwatch> _DiagnosticStopwatches = new ObjectPool<Stopwatch>(() => new Stopwatch());

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly PipeWriter _Writer;
        private readonly PipeReader _Reader;
        private readonly EncryptionProvider _EncryptionProvider;
        private readonly Dictionary<PacketType, ManualResetEvent> _PacketResetEvents;

        public Guid Identity { get; }
        public EndPoint RemoteEndPoint { get; }

        public byte[] PublicKey => _EncryptionProvider.PublicKey;

        public Connection(TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _Writer = PipeWriter.Create(_Stream);
            _Reader = PipeReader.Create(_Stream);
            _EncryptionProvider = new EncryptionProvider();

            Identity = Guid.NewGuid();
            RemoteEndPoint = _Client.Client.RemoteEndPoint;

            _PacketResetEvents = new Dictionary<PacketType, ManualResetEvent>
            {
                { PacketType.EncryptionKeys, new ManualResetEvent(false) },
                { PacketType.BeginIdentity, new ManualResetEvent(false) },
                { PacketType.EndIdentity, new ManualResetEvent(false) },
            };
        }

        public async ValueTask Finalize(CancellationToken cancellationToken)
        {
            await OnConnected().Contextless();

            await SendEncryptionKeys(cancellationToken).Contextless();

            BeginListen(cancellationToken);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Waiting for {nameof(PacketType.EncryptionKeys)} packet."));
            WaitForPacket(PacketType.EncryptionKeys);

            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Connection finalized."));
        }

        public void WaitForPacket(PacketType packetType)
        {
            _PacketResetEvents[packetType].WaitOne();
        }


        #region Listening

        private void BeginListen(CancellationToken cancellationToken) => Task.Run(() => ReadLoopAsync(cancellationToken), cancellationToken);

        private async ValueTask ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Beginning read loop."));

                Stopwatch stopwatch = new Stopwatch();

                while (!cancellationToken.IsCancellationRequested)
                {
                    ReadResult result = await _Reader.ReadAsync(cancellationToken).Contextless();
                    ReadOnlySequence<byte> sequence = result.Buffer;

                    if (sequence.IsEmpty)
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                            "Received no data from reader. This is likely a connection closure, so the loop will halt."));
                        break;
                    }

                    stopwatch.Restart();

                    if (!TryReadPacket(sequence, out SequencePosition consumed, out Packet packet))
                    {
                        continue;
                    }

                    DiagnosticsProvider.CommitData<PacketDiagnosticGroup>(new ConstructionTime(stopwatch.Elapsed));

                    await OnPacketReceived(packet, cancellationToken).Contextless();
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
                await OnDisconnected().Contextless();
            }
        }

        private static bool TryReadPacket(ReadOnlySequence<byte> sequence, [NotNull] out SequencePosition consumed,
            [NotNullWhen(true)] out Packet packet)
        {
            consumed = sequence.Start;
            packet = default;

            if (sequence.Length < sizeof(int))
            {
                return false;
            }

            int packetLength = BitConverter.ToInt32(sequence.Slice(0, sizeof(int)).FirstSpan);

            if (sequence.Length < packetLength)
            {
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

        public async ValueTask WriteAsync(PacketType type, DateTime utcTimestamp, byte[] content, CancellationToken cancellationToken)
        {
            await WriteEncryptedAsync(type, utcTimestamp, content, cancellationToken).Contextless();
            await _Writer.FlushAsync(cancellationToken).Contextless();
        }

        public async ValueTask WriteAsync(IEnumerable<(PacketType, DateTime, byte[])> packets, CancellationToken cancellationToken)
        {
            foreach ((PacketType type, DateTime timestamp, byte[] content) in packets)
            {
                await WriteEncryptedAsync(type, timestamp, content, cancellationToken).Contextless();
            }

            await _Writer.FlushAsync(cancellationToken).Contextless();
        }

        private async ValueTask WriteEncryptedAsync(PacketType type, DateTime utcTimestamp, byte[] content, CancellationToken cancellationToken)
        {
            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Write call: {type} | {utcTimestamp} | {content.Length}"));

            Stopwatch stopwatch = _DiagnosticStopwatches.Rent();

            stopwatch.Restart();

            byte[] initializationVector = Array.Empty<byte>();

            if (content.Length > 0)
            {
                (initializationVector, content) = await _EncryptionProvider.Encrypt(content, cancellationToken).Contextless();
                Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                    $"Encrypted packet in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms."));

                stopwatch.Restart();
            }

            Packet packet = new Packet(type, PublicKey, initializationVector, utcTimestamp, content);
            byte[] serialized = packet.Serialize();
            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                $"Serialized packet in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms."));

            stopwatch.Reset();
            _DiagnosticStopwatches.Return(stopwatch);

            await _Writer.WriteAsync(serialized, cancellationToken).Contextless();

            Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"OUT: {packet}"));
        }

        private async ValueTask SendEncryptionKeys(CancellationToken cancellationToken)
        {
            if (_EncryptionProvider.EncryptionNegotiated)
            {
                Log.Warning("Protocol requires that key exchanges happen ONLY ONCE.");
            }
            else
            {
                Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Sending encryption keys."));

                Packet packet = new Packet(PacketType.EncryptionKeys, _EncryptionProvider.PublicKey, Array.Empty<byte>(), DateTime.UtcNow,
                    Array.Empty<byte>());
                await _Stream.WriteAsync(packet.Serialize(), cancellationToken).Contextless();
                await _Stream.FlushAsync(cancellationToken).Contextless();
            }
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Connected;
        public event ConnectionEventHandler? Disconnected;

        private async ValueTask OnConnected()
        {
            if (Connected is { })
            {
                await Connected(this).Contextless();
            }
        }

        private async ValueTask OnDisconnected()
        {
            if (Disconnected is { })
            {
                await Disconnected(this).Contextless();
            }
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler? PacketReceived;

        private async ValueTask OnPacketReceived(Packet packet, CancellationToken cancellationToken)
        {
            Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, $"INC: {packet}"));

            if (_PacketResetEvents.TryGetValue(packet.Type, out ManualResetEvent? resetEvent))
            {
                resetEvent.Set();
            }

            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    _EncryptionProvider.AssignRemoteKeys(packet.PublicKey);
                    Log.Debug(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint, "Assigned given encryption keys."));
                    break;
                default:
                    if (packet.Content.Length == 0)
                    {
                        // we don't need to decrypt the content
                        break;
                    }

                    Stopwatch stopwatch = _DiagnosticStopwatches.Rent();
                    stopwatch.Restart();

                    packet.Content = await _EncryptionProvider.Decrypt(packet.InitializationVector, packet.PublicKey, packet.Content,
                        cancellationToken).Contextless();

                    DiagnosticsProvider.CommitData<PacketDiagnosticGroup>(new DecryptionTime(stopwatch.Elapsed));

                    Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                        $"Decrypted packet in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms."));

                    _DiagnosticStopwatches.Return(stopwatch);
                    break;
            }

            if (PacketReceived is { })
            {
                Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, RemoteEndPoint,
                    $"Invoking packet event ({packet.Type}: {packet.Content.Length} bytes)."));
                await PacketReceived(this, packet).Contextless();
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

        public bool Equals(Connection? other)
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

            return Equals((Connection)obj);
        }

        public override int GetHashCode() => Identity.GetHashCode();

        public static bool operator ==(Connection? left, Connection? right) => Equals(left, right);

        public static bool operator !=(Connection? left, Connection? right) => !Equals(left, right);

        #endregion
    }
}
