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

        public const int OFFSET_PACKET_LENGTH = 0;
        public const int OFFSET_PACKET_TYPE = OFFSET_PACKET_LENGTH + sizeof(int);
        public const int OFFSET_PUBLIC_KEY = OFFSET_PACKET_TYPE + sizeof(byte);
        public const int OFFSET_INITIALIZATION_VECTOR = OFFSET_PUBLIC_KEY + EncryptionProvider.PUBLIC_KEY_SIZE;
        public const int OFFSET_TIMESTAMP = OFFSET_INITIALIZATION_VECTOR + EncryptionProvider.INITIALIZATION_VECTOR_SIZE;

        public const int HEADER_LENGTH = OFFSET_TIMESTAMP + sizeof(long);

        #endregion

        public PacketType Type { get; }
        public byte[] PublicKey { get; }
        public byte[] InitializationVector { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; set; }

        public Packet(PacketType type, byte[] publicKey, byte[] initializationVector, DateTime utcTimestamp, byte[] content)
        {
            Type = type;
            PublicKey = publicKey;
            InitializationVector = initializationVector;
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
            ReadOnlySequence<byte> initializationVectorSequence =
                sequence.Slice(OFFSET_INITIALIZATION_VECTOR, EncryptionProvider.INITIALIZATION_VECTOR_SIZE);
            ReadOnlySequence<byte> timestampSequence = sequence.Slice(OFFSET_TIMESTAMP, sizeof(long));
            ReadOnlySequence<byte> contentSequence = sequence.Slice(HEADER_LENGTH, packetLength - HEADER_LENGTH);

            Type = (PacketType)packetTypeSequence.FirstSpan[0];
            PublicKey = publicKeySequence.ToArray();
            InitializationVector = initializationVectorSequence.ToArray();
            UtcTimestamp = DateTime.FromBinary(BitConverter.ToInt64(timestampSequence.FirstSpan));
            Content = contentSequence.ToArray();
        }

        public byte[] Serialize()
        {
            int packetLength = HEADER_LENGTH + Content.Length;
            byte[] serialized = new byte[packetLength];

            Buffer.BlockCopy(BitConverter.GetBytes(packetLength), 0, serialized, OFFSET_PACKET_LENGTH, sizeof(int));
            serialized[OFFSET_PACKET_TYPE] = (byte)Type;
            Buffer.BlockCopy(PublicKey, 0, serialized, OFFSET_PUBLIC_KEY, PublicKey.Length);
            Buffer.BlockCopy(InitializationVector, 0, serialized, OFFSET_INITIALIZATION_VECTOR, InitializationVector.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(UtcTimestamp.Ticks), 0, serialized, OFFSET_TIMESTAMP, sizeof(long));
            Buffer.BlockCopy(Content, 0, serialized, HEADER_LENGTH, Content.Length);

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
