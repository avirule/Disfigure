#region

using System;
using System.Threading.Tasks;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net
{
    public enum PacketType : byte
    {
        Text,
        Sound,
        Image,
        Video,
        Administration,
        Operation,
        EncryptionKeys,
        BeginIdentity,
        Identity,
        ChannelIdentity,
        EndIdentity
    }

    public delegate ValueTask PacketEventHandler(Connection origin, Packet packet);

    public class Packet
    {
        #region Static

        public const int HEADER_LENGTH = sizeof(int)
                                         + EncryptionProvider.PUBLIC_KEY_SIZE
                                         + sizeof(byte)
                                         + sizeof(long);

        public const int OFFSET_PACKET_LENGTH = 0;
        public const int OFFSET_PACKET_TYPE = OFFSET_PACKET_LENGTH + sizeof(int);
        public const int OFFSET_PUBLIC_KEY = OFFSET_PACKET_TYPE + sizeof(byte);
        public const int OFFSET_TIMESTAMP = OFFSET_PUBLIC_KEY + EncryptionProvider.PUBLIC_KEY_SIZE;

        #endregion

        public PacketType Type { get; }
        public byte[] PublicKey { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; set; }

        public Packet(PacketType type, byte[] publicKey, DateTime utcTimestamp, byte[] content)
        {
            Type = type;
            PublicKey = publicKey;
            UtcTimestamp = utcTimestamp;
            Content = content;
        }

        public byte[] Serialize()
        {
            int packetLength = HEADER_LENGTH + Content.Length;
            byte[] serialized = new byte[packetLength];

            BitConverter.GetBytes(packetLength).CopyTo(serialized, OFFSET_PACKET_LENGTH);
            serialized[OFFSET_PACKET_TYPE] = (byte)Type;
            BitConverter.GetBytes(UtcTimestamp.Ticks).CopyTo(serialized, OFFSET_TIMESTAMP);
            Content.CopyTo(serialized, HEADER_LENGTH);

            return serialized;
        }
    }
}
