#region

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Modules;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public enum PacketType : byte
    {
        EncryptionKeys,
        Connect,
        Disconnect,
        Connected,
        Disconnected,
        Ping,
        Pong,
        Text,
        Sound,
        Media,
        Video,
        Administration,
        Operation,
        BeginIdentity,
        Identity,
        ChannelIdentity,
        EndIdentity
    }

    public readonly struct BasicPacket : IPacket<BasicPacket>
    {
        private const int _OFFSET_PACKET_TYPE = 0;
        private const int _OFFSET_TIMESTAMP = _OFFSET_PACKET_TYPE + sizeof(PacketType);
        private const int _HEADER_LENGTH = _OFFSET_TIMESTAMP + sizeof(long);

        public readonly ReadOnlyMemory<byte> Data;
        public readonly PacketType Type;
        public readonly DateTime UtcTimestamp;

        public ReadOnlySpan<byte> Content => Data.Slice(_HEADER_LENGTH).Span;

        public BasicPacket(ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> destination = data.Span;

            Type = MemoryMarshal.Read<PacketType>(destination.Slice(_OFFSET_PACKET_TYPE));
            UtcTimestamp = MemoryMarshal.Read<DateTime>(destination.Slice(_OFFSET_TIMESTAMP));

            Data = data;
        }

        public BasicPacket(PacketType packetType, DateTime utcTimestamp, ReadOnlySpan<byte> content)
        {
            Type = packetType;
            UtcTimestamp = utcTimestamp;

            Memory<byte> data = new byte[_HEADER_LENGTH + content.Length];
            Span<byte> destination = data.Span;

            MemoryMarshal.Write(destination.Slice(_OFFSET_PACKET_TYPE), ref packetType);
            MemoryMarshal.Write(destination.Slice(_OFFSET_TIMESTAMP), ref utcTimestamp);
            content.CopyTo(destination.Slice(_HEADER_LENGTH));

            Data = data;
        }

        public ReadOnlyMemory<byte> Serialize() => Data;

        public override string ToString()
        {
            return new StringBuilder()
                .Append(Type.ToString())
                .Append(' ')
                .Append(UtcTimestamp.ToString("O"))
                .Append(' ')
                .Append(Type switch
                {
                    PacketType.Text => Encoding.Unicode.GetString(Content),
                    _ => MemoryMarshal.Cast<byte, char>(Content)
                })
                .ToString();
        }


        public static async ValueTask SendEncryptionKeys(Connection<BasicPacket> connection) =>
            await connection.WriteAsync(new BasicPacket(PacketType.EncryptionKeys, DateTime.UtcNow, connection.PublicKey), CancellationToken.None);


        #region PacketEncryptorAsync

        public static async ValueTask<ReadOnlyMemory<byte>> EncryptorAsync(BasicPacket packet, EncryptionProvider encryptionProvider,
            CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> initializationVector = ReadOnlyMemory<byte>.Empty;
            ReadOnlyMemory<byte> packetData = packet.Serialize();

            if (encryptionProvider.IsEncryptable)
            {
                (initializationVector, packetData) = await encryptionProvider.EncryptAsync(packetData, cancellationToken);
            }
            else
            {
                Log.Warning($"Write call has been received, but the {nameof(EncryptionProvider)} has no keys.");
            }

            const int header_length = sizeof(int) + EncryptionProvider.INITIALIZATION_VECTOR_SIZE;

            int length = header_length + packetData.Length;
            Memory<byte> data = new byte[length];
            MemoryMarshal.Write(data.Span, ref length);
            initializationVector.CopyTo(data.Slice(sizeof(int)));
            packetData.CopyTo(data.Slice(header_length));

            return data;
        }

        #endregion


        #region PacketFactoryAsync

        public static async ValueTask<(bool, SequencePosition, BasicPacket)> FactoryAsync(ReadOnlySequence<byte> sequence,
            EncryptionProvider encryptionProvider, CancellationToken cancellationToken)
        {
            if (!TryGetPacketData(sequence, out SequencePosition consumed, out ReadOnlySequence<byte> data))
            {
                return (false, default, default);
            }

            ReadOnlyMemory<byte> initializationVector = data.Slice(0, EncryptionProvider.INITIALIZATION_VECTOR_SIZE).First;
            ReadOnlyMemory<byte> packetData = data.Slice(EncryptionProvider.INITIALIZATION_VECTOR_SIZE, data.End).First;

            if (packetData.IsEmpty)
            {
                throw new ArgumentException("Decrypted packet contained no data.", nameof(packetData));
            }

            if (encryptionProvider.IsEncryptable)
            {
                packetData = await encryptionProvider.DecryptAsync(initializationVector, packetData, cancellationToken);
            }
            else
            {
                Log.Warning($"Packet data has been received, but the {nameof(EncryptionProvider)} has no keys.");
            }

            return (true, consumed, new BasicPacket(packetData));
        }

        private static bool TryGetPacketData(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out ReadOnlySequence<byte> data)
        {
            consumed = default;
            data = default;

            if (sequence.Length < sizeof(int))
            {
                return false;
            }

            int length = MemoryMarshal.Read<int>(sequence.FirstSpan);

            // otherwise, check if entire packet has been received
            if ((length > 0) && (sequence.Length >= length))
            {
                consumed = sequence.GetPosition(length);
            }
            // if none, then wait for more data
            else
            {
                return false;
            }

            // begin at sizeof(int) to skip originalLength
            data = sequence.Slice(sizeof(int), consumed);
            return true;
        }

        #endregion


        #region PingPongLoop

        public static void PingPongLoop(Module<BasicPacket> module, TimeSpan pingInterval, CancellationToken cancellationToken) =>
            Task.Run(async () => await PingPongLoopAsync(module, pingInterval, cancellationToken), cancellationToken);

        private static async Task PingPongLoopAsync(Module<BasicPacket> module, TimeSpan pingInterval, CancellationToken cancellationToken)
        {
            ConcurrentDictionary<Guid, PendingPing> pendingPings = new ConcurrentDictionary<Guid, PendingPing>();
            Stack<Guid> abandonedConnections = new Stack<Guid>();

            ValueTask PongPacketCallbackImpl(Connection<BasicPacket> connection, BasicPacket basicPacket)
            {
                if (basicPacket.Type != PacketType.Pong)
                {
                    return default;
                }

                if (!pendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
                    return default;
                }
                else if (basicPacket.Content.Length != 16)
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
                    return default;
                }

                Guid pingIdentity = new Guid(basicPacket.Content);
                if (pendingPing.Identity != pingIdentity)
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

                foreach ((Guid connectionIdentity, Connection<BasicPacket> connection) in module.ReadOnlyConnections)
                {
                    PendingPing pendingPing = new PendingPing();

                    if (pendingPings.TryAdd(connectionIdentity, pendingPing))
                    {
                        await connection.WriteAsync(new BasicPacket(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray()),
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
