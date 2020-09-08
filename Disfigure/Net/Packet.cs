#region

using System;
using System.Text;
using System.Threading.Tasks;

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

    public readonly struct Packet
    {
        #region Static

        public const int HEADER_LENGTH = sizeof(long) // timestamp
                                         + sizeof(byte) // type
                                         + sizeof(int); // content length

        public const int TIMESTAMP_HEADER_OFFSET = 0;
        public const int PACKET_TYPE_HEADER_OFFSET = TIMESTAMP_HEADER_OFFSET + sizeof(long);
        public const int CONTENT_LENGTH_HEADER_OFFSET = PACKET_TYPE_HEADER_OFFSET + sizeof(byte);

        public static unsafe Packet BuildMMSPacket(Channel channel, DateTime utcTimestamp, PacketType packetType, byte[] content)
        {
            byte[] adjustedContent = new byte[sizeof(Guid) + content.Length];
            Buffer.BlockCopy(channel.Guid.ToByteArray(), 0, adjustedContent, 0, sizeof(Guid));
            Buffer.BlockCopy(content, 0, adjustedContent, sizeof(Guid), content.Length);

            return new Packet(utcTimestamp, packetType, adjustedContent);
        }

        #endregion

        public readonly DateTime UtcTimestamp;
        public readonly PacketType Type;
        public readonly byte[] Content;

        public Packet(DateTime utcTimestamp, PacketType type, byte[] content)
        {
            UtcTimestamp = utcTimestamp;
            Type = type;
            Content = content;
        }

        public byte[] Serialize()
        {
            byte[] utcTimestamp = BitConverter.GetBytes(UtcTimestamp.Ticks);
            byte[] contentLength = BitConverter.GetBytes(Content.Length);
            byte[] serialized = new byte[HEADER_LENGTH + Content.Length];

            Buffer.BlockCopy(utcTimestamp, 0, serialized, TIMESTAMP_HEADER_OFFSET, utcTimestamp.Length);
            serialized[PACKET_TYPE_HEADER_OFFSET] = (byte)Type;
            Buffer.BlockCopy(contentLength, 0, serialized, CONTENT_LENGTH_HEADER_OFFSET, contentLength.Length);
            Buffer.BlockCopy(Content, 0, serialized, HEADER_LENGTH, Content.Length);

            return serialized;
        }

        public static Packet Deserialize(byte[] data)
        {
            long timestamp = BitConverter.ToInt64(data, TIMESTAMP_HEADER_OFFSET);
            byte packetType = data[PACKET_TYPE_HEADER_OFFSET];

            return new Packet(DateTime.FromBinary(timestamp), (PacketType)packetType, data[HEADER_LENGTH..]);
        }

        public override unsafe string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(UtcTimestamp.ToString("O"));
            builder.Append(' ');
            builder.Append(Enum.GetName(typeof(PacketType), Type));
            builder.Append(' ');

            switch (Type)
            {
                case PacketType.Text:
                    builder.Append(Encoding.Unicode.GetString(Content[sizeof(Guid)..]));
                    break;
                default:
                    builder.Append(Content);
                    break;
            }

            return builder.ToString();
        }
    }
}
