#region

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Serilog;

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

        public static Packet Create(PacketType packetType, DateTime utcTimestamp, ReadOnlySpan<byte> content)
        {
            Memory<byte> data = new byte[HEADER_LENGTH + content.Length];
            Span<byte> destination = data.Span;

            MemoryMarshal.Write(destination.Slice(OFFSET_PACKET_TYPE), ref packetType);
            MemoryMarshal.Write(destination.Slice(OFFSET_TIMESTAMP), ref utcTimestamp);
            content.CopyTo(destination.Slice(HEADER_LENGTH));

            return new Packet(data);
        }



        #region PacketSerializerAsync



        #endregion


        #region PacketFactoryAsync



        #endregion


        #region PingPongLoop

        public static void PingPongLoop(Module module, TimeSpan pingInterval, CancellationToken cancellationToken) =>
            Task.Run(async () => await PingPongLoopAsync(module, pingInterval, cancellationToken), cancellationToken);

        private static async ValueTask PingPongLoopAsync(Module module, TimeSpan pingInterval, CancellationToken cancellationToken)
        {
            ConcurrentDictionary<Guid, Guid> pendingPings = new ConcurrentDictionary<Guid, Guid>();
            Stack<Guid> abandonedConnections = new Stack<Guid>();

            ValueTask PongPacketCallbackImpl(Connection connection, Packet basicPacket)
            {
                if (basicPacket.Type != PacketType.Pong) return default;

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

                foreach ((Guid connectionIdentity, Connection connection) in module.ReadOnlyConnections)
                {
                    Guid pingIdentity = Guid.NewGuid();

                    if (pendingPings.TryAdd(connectionIdentity, pingIdentity))
                    {
                        await connection.WriteAsync(Create(PacketType.Ping, DateTime.UtcNow, pingIdentity.ToByteArray()), cancellationToken);
                    }
                    else
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Pending ping timed out. Queueing force disconnect."));

                        abandonedConnections.Push(connectionIdentity);
                    }
                }

                while (abandonedConnections.TryPop(out Guid connectionIdentity)) module.ForceDisconnect(connectionIdentity);
            }
        }

        #endregion
    }
}
