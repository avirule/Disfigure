#region

using System;
using System.Text;

#endregion

namespace DisfigureCore.Net
{
    public enum PacketType : byte
    {
        Admin,
        Text,
        Image,
        Video,
        Sound,
        Operation,
    }

    public readonly struct Packet
    {
        public const int HEADER_LENGTH = sizeof(long) // timestamp
                                         + sizeof(byte) // type
                                         + 16 // guid
                                         + sizeof(int); // content length

        public const int TIMESTAMP_HEADER_OFFSET = 0;
        public const int PACKET_TYPE_HEADER_OFFSET = TIMESTAMP_HEADER_OFFSET + sizeof(long);
        public const int CHANNEL_GUID_HEADER_OFFSET = PACKET_TYPE_HEADER_OFFSET + sizeof(byte);
        public const int CONTENT_LENGTH_HEADER_OFFSET = CHANNEL_GUID_HEADER_OFFSET + 16; // guid is 16 bytes

        public readonly DateTime UtcTimestamp;
        public readonly PacketType Type;
        public readonly Guid Channel;
        public readonly byte[] Content;

        public Packet(DateTime utcTimestamp, PacketType type, Guid channel, byte[] content)
        {
            UtcTimestamp = utcTimestamp;
            Type = type;
            Channel = channel;
            Content = content;
        }

        public byte[] Serialize()
        {
            byte[] utcTimestamp = BitConverter.GetBytes(UtcTimestamp.Ticks);
            byte[] packetType = BitConverter.GetBytes((byte)Type);
            byte[] channel = Channel.ToByteArray();
            byte[] contentLength = BitConverter.GetBytes(Content.Length);
            byte[] serialized = new byte[HEADER_LENGTH + Content.Length];

            Buffer.BlockCopy(utcTimestamp, 0, serialized, TIMESTAMP_HEADER_OFFSET, utcTimestamp.Length);
            Buffer.BlockCopy(packetType, 0, serialized, PACKET_TYPE_HEADER_OFFSET, packetType.Length);
            Buffer.BlockCopy(channel, 0, serialized, CHANNEL_GUID_HEADER_OFFSET, channel.Length);
            Buffer.BlockCopy(contentLength, 0, serialized, CONTENT_LENGTH_HEADER_OFFSET, contentLength.Length);
            Buffer.BlockCopy(Content, 0, serialized, HEADER_LENGTH, Content.Length);

            return serialized;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(UtcTimestamp.ToString("O"));
            builder.Append(' ');
            builder.Append(Enum.GetName(typeof(PacketType), Type));
            builder.Append(' ');

            switch (Type)
            {
                case PacketType.Text:
                    builder.Append(Encoding.Unicode.GetString(Content));
                    break;
                case PacketType.Admin:
                case PacketType.Image:
                case PacketType.Video:
                case PacketType.Sound:
                    builder.Append(Content);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return builder.ToString();
        }
    }
}
