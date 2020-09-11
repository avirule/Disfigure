#region

using System;
using System.Buffers;
using System.Text;
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

    public struct Packet
    {
        #region Static

        public const int HEADER_LENGTH = sizeof(int) // packet length
                                         + sizeof(byte) // packet type
                                         + EncryptionProvider.PUBLIC_KEY_SIZE // public key
                                         + sizeof(long); // timestamp

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

        public Packet(ReadOnlySequence<byte> sequence, int packetLength)
        {
            if (sequence.Length < packetLength)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), $"Sequence does not contain enough bytes to construct the {nameof(Packet)}.");
            }

            ReadOnlySequence<byte> packetTypeSequence = sequence.Slice(OFFSET_PACKET_TYPE, sizeof(byte));
            ReadOnlySequence<byte> publicKeySequence = sequence.Slice(OFFSET_PUBLIC_KEY, EncryptionProvider.PUBLIC_KEY_SIZE);
            ReadOnlySequence<byte> timestampSequence = sequence.Slice(OFFSET_TIMESTAMP, sizeof(long));
            ReadOnlySequence<byte> contentSequence = sequence.Slice(HEADER_LENGTH, packetLength - HEADER_LENGTH);

            Type = (PacketType)packetTypeSequence.FirstSpan[0];
            PublicKey = publicKeySequence.ToArray();
            UtcTimestamp = DateTime.FromBinary(BitConverter.ToInt64(timestampSequence.FirstSpan));
            Content = contentSequence.ToArray();
        }

        public byte[] Serialize()
        {
            int packetLength = HEADER_LENGTH + Content.Length;
            byte[] serialized = new byte[packetLength];

            BitConverter.GetBytes(packetLength).CopyTo(serialized, OFFSET_PACKET_LENGTH);
            serialized[OFFSET_PACKET_TYPE] = (byte)Type;
            PublicKey.CopyTo(serialized, OFFSET_PUBLIC_KEY);
            BitConverter.GetBytes(UtcTimestamp.Ticks).CopyTo(serialized, OFFSET_TIMESTAMP);
            Content.CopyTo(serialized, HEADER_LENGTH);

            return serialized;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Type.ToString());
            builder.Append(" =");
            builder.AppendJoin(' ', PublicKey[..5]);
            builder.Append("..= ");
            builder.Append(UtcTimestamp.ToString("O"));
            builder.Append(' ');

            switch (Type)
            {
                case PacketType.Text:
                    builder.Append(Encoding.Unicode.GetString(Content));
                    break;
                default:
                    builder.AppendJoin(' ', Content);
                    break;
            }

            return builder.ToString();
        }
    }
}
