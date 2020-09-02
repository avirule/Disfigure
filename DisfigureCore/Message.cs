#region

using System;
using System.Text;

#endregion

namespace DisfigureCore
{
    public readonly struct Message
    {
        public const int HEADER_LENGTH = 35;

        public readonly DateTime UtcTimestamp;
        public readonly MessageType Type;
        public readonly byte[] Content;

        public Message(DateTime utcTimestamp, MessageType type, byte[] content)
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
            builder.Append(Enum.GetName(typeof(MessageType), Type));
            builder.Append(' ');

            switch (Type)
            {
                case MessageType.Text:
                    builder.Append(Encoding.Unicode.GetString(Content));
                    break;
                case MessageType.Admin:
                case MessageType.Image:
                case MessageType.Video:
                case MessageType.Sound:
                    builder.Append(Content);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return builder.ToString();
        }
    }
}
