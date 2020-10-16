#region

using Disfigure.Cryptography;
using Serilog;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.Net.Packets
{
    public readonly partial struct Packet
    {
        public const int ALIGNMENT_CONSTANT = 205582199;

        public const int OFFSET_DATA_LENGTH = 0;
        public const int OFFSET_ALIGNMENT_CONSTANT = OFFSET_DATA_LENGTH + sizeof(int);
        public const int OFFSET_INITIALIZATION_VECTOR = OFFSET_ALIGNMENT_CONSTANT + sizeof(int);
        public const int ENCRYPTION_HEADER_LENGTH = OFFSET_INITIALIZATION_VECTOR + IEncryptionProvider.INITIALIZATION_VECTOR_SIZE;

        public const int OFFSET_PACKET_TYPE = 0;
        public const int OFFSET_TIMESTAMP = OFFSET_PACKET_TYPE + sizeof(PacketType);
        public const int HEADER_LENGTH = OFFSET_TIMESTAMP + sizeof(long);

        public const int TOTAL_HEADER_LENGTH = ENCRYPTION_HEADER_LENGTH + HEADER_LENGTH;

        public static async ValueTask SendEncryptionKeys(Connection<Packet> connection) =>
            await connection.WriteDirectAsync(SerializePacket(ReadOnlyMemory<byte>.Empty,
                new Packet(PacketType.EncryptionKeys, DateTime.UtcNow, connection.EncryptionProviderAs<IEncryptionProvider>().PublicKey).Serialize()),
                CancellationToken.None);

        #region PacketSerializerAsync

        public static async ValueTask<ReadOnlyMemory<byte>> SerializerAsync(Packet packet, IEncryptionProvider? encryptionProvider,
            CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> initializationVector = ReadOnlyMemory<byte>.Empty;
            ReadOnlyMemory<byte> packetData = packet.Serialize();

            if (encryptionProvider is { })
            {
                (initializationVector, packetData) = await encryptionProvider!.EncryptAsync(packetData, cancellationToken);
            }
            else
            {
                throw new ArgumentException($"Write call has been received, but the {nameof(IEncryptionProvider)} is in an unusable state.", nameof(encryptionProvider));
            }

            return SerializePacket(initializationVector, packetData);
        }

        private static ReadOnlyMemory<byte> SerializePacket(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> packetData)
        {
            int alignmentConstant = ALIGNMENT_CONSTANT;
            int length = ENCRYPTION_HEADER_LENGTH + packetData.Length;
            Memory<byte> data = new byte[length];
            Span<byte> dataSpan = data.Span;

            MemoryMarshal.Write(dataSpan.Slice(OFFSET_DATA_LENGTH), ref length);
            MemoryMarshal.Write(dataSpan.Slice(OFFSET_ALIGNMENT_CONSTANT), ref alignmentConstant);
            initializationVector.CopyTo(data.Slice(OFFSET_INITIALIZATION_VECTOR));
            packetData.CopyTo(data.Slice(ENCRYPTION_HEADER_LENGTH));

            return data;
        }

        #endregion

        #region PacketFactoryAsync

        public static async ValueTask<(bool, SequencePosition, Packet)> FactoryAsync(ReadOnlySequence<byte> sequence,
            IEncryptionProvider? encryptionProvider, CancellationToken cancellationToken)
        {
            if (!TryGetData(sequence, out SequencePosition consumed, out ReadOnlyMemory<byte> data))
            {
                return (false, consumed, default);
            }
            else
            {
                ReadOnlyMemory<byte> initializationVector = data.Slice(0, IEncryptionProvider.INITIALIZATION_VECTOR_SIZE);
                ReadOnlyMemory<byte> packetData = data.Slice(IEncryptionProvider.INITIALIZATION_VECTOR_SIZE);

                if (encryptionProvider?.IsEncryptable ?? false)
                {
                    packetData = await encryptionProvider.DecryptAsync(initializationVector, packetData, cancellationToken);
                }
                else
                {
                    Log.Warning($"Packet data has been received, but the {nameof(IEncryptionProvider)} is in an unusable state.");
                }

                return (true, consumed, new Packet(packetData));
            }
        }

        private static bool TryGetData(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> span = sequence.FirstSpan;
            consumed = sequence.Start;
            data = ReadOnlyMemory<byte>.Empty;
            int length;

            // wait until 4 bytes and sequence is long enough to construct packet
            if ((sequence.Length < sizeof(int)) || (sequence.Length < (length = MemoryMarshal.Read<int>(span))))
            {
                return false;
            }
            // ensure length covers entire valid header
            else if (length < TOTAL_HEADER_LENGTH)
            {
                // if not, print warning and throw away data
                Log.Warning("Received packet with invalid header format (too short).");
                consumed = sequence.GetPosition(length);
                return false;
            }
            // ensure alignment constant is valid
            else if (MemoryMarshal.Read<int>(span.Slice(sizeof(int))) != ALIGNMENT_CONSTANT)
            {
                throw new PacketMisalignedException();
            }
            else
            {
                consumed = sequence.GetPosition(length);
                data = sequence.Slice(sizeof(int) + sizeof(int)).First;
                return true;
            }
        }

        #endregion

        #region PingPongLoop

        public static void PingPongLoop(Module<Packet> module, TimeSpan pingInterval, CancellationToken cancellationToken) =>
            Task.Run(async () => await PingPongLoopAsync(module, pingInterval, cancellationToken), cancellationToken);

        private static async ValueTask PingPongLoopAsync(Module<Packet> module, TimeSpan pingInterval, CancellationToken cancellationToken)
        {
            ConcurrentDictionary<Guid, Guid> pendingPings = new ConcurrentDictionary<Guid, Guid>();
            Stack<Guid> abandonedConnections = new Stack<Guid>();

            ValueTask PongPacketCallbackImpl(Connection<Packet> connection, Packet basicPacket)
            {
                if (basicPacket.Type != PacketType.Pong)
                {
                    return default;
                }

                if (!pendingPings.TryGetValue(connection.Identity, out Guid pingIdentity))
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping with that identity was pending.");
                    return default;
                }
                else if (basicPacket.ContentSpan.Length < 16)
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
                    return default;
                }

                Guid remotePingIdentity = new Guid(basicPacket.ContentSpan);
                if (remotePingIdentity != pingIdentity)
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but ping identity didn't match.");
                    return default;
                }

                pendingPings.TryRemove(connection.Identity, out _);
                return default;
            }

            module.PacketReceived += PongPacketCallbackImpl;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(pingInterval, cancellationToken);

                foreach ((Guid connectionIdentity, Connection<Packet> connection) in module.ReadOnlyConnections)
                {
                    Guid pingIdentity = Guid.NewGuid();

                    if (pendingPings.TryAdd(connectionIdentity, pingIdentity))
                    {
                        await connection.WriteAsync(new Packet(PacketType.Ping, DateTime.UtcNow, pingIdentity.ToByteArray()),
                            cancellationToken);
                    }
                    else
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Pending ping timed out. Queueing force disconnect."));
                        abandonedConnections.Push(connectionIdentity);
                    }
                }

                while (abandonedConnections.TryPop(out Guid connectionIdentity))
                {
                    module.ForceDisconnect(connectionIdentity);
                }
            }
        }

        #endregion
    }
}