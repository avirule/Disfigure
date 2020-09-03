#region

using System;
using System.Text;

#endregion

namespace DisfigureCore
{
    public readonly struct Packet
    {
        public const int HEADER_LENGTH = 35;

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
            byte[] header = Encoding.ASCII.GetBytes($"{UtcTimestamp:O} {(int)Type} {Content.Length:0000} ");
            byte[] serialized = new byte[header.Length + Content.Length];
            Array.Copy(header, 0, serialized, 0, header.Length);
            Array.Copy(Content, 0, serialized, header.Length, Content.Length);

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
