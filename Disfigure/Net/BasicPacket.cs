#region

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        BeginIdentity,
        Identity,
        ChannelIdentity,
        EndIdentity
    }

    public readonly struct BasicPacket : IPacket
    {
        #region Static

        public static async ValueTask<(bool, SequencePosition, BasicPacket)> Factory(ReadOnlySequence<byte> sequence,
            EncryptionProvider encryptionProvider, CancellationToken cancellationToken)
        {
            if (!TryGetPacketData(sequence, out SequencePosition consumed, out ReadOnlySequence<byte> data))
            {
                return (false, default, default);
            }

            const int offset_packet_type = 0;
            const int offset_timestamp = offset_packet_type + sizeof(byte);
            const int header_length = offset_timestamp + sizeof(long);

            ReadOnlyMemory<byte> initializationVector = data.Slice(0, EncryptionProvider.INITIALIZATION_VECTOR_SIZE).First;
            ReadOnlyMemory<byte> encrypted = data.Slice(EncryptionProvider.INITIALIZATION_VECTOR_SIZE, data.End).First;

            Memory<byte> decrypted = await encryptionProvider.DecryptAsync(initializationVector, encrypted, cancellationToken).ConfigureAwait(false);

            if (decrypted.IsEmpty)
            {
                throw new ArgumentException("Decrypted packet contained no data.", nameof(decrypted));
            }

            PacketType packetType = MemoryMarshal.Read<PacketType>(decrypted.Slice(offset_packet_type, sizeof(PacketType)).Span);
            DateTime utcTimestamp = MemoryMarshal.Read<DateTime>(decrypted.Slice(offset_timestamp, sizeof(long)).Span);
            Memory<byte> content = decrypted.Slice(header_length, decrypted.Length - header_length);

            return (default, consumed, new BasicPacket(packetType, utcTimestamp, content));
        }

        private static bool TryGetPacketData(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out ReadOnlySequence<byte> data)
        {
            consumed = default;
            data = default;

            if (sequence.Length < sizeof(int))
            {
                return false;
            }

            int length = MemoryMarshal.Read<int>(sequence.Slice(0, sizeof(int)).FirstSpan);

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


        public PacketType Type { get; }
        public DateTime UtcTimestamp { get; }
        public Memory<byte> Content { get; }

        public BasicPacket(PacketType type, DateTime utcTimestamp, Memory<byte> content)
        {
            Type = type;
            UtcTimestamp = utcTimestamp;
            Content = content;
        }

        public override string ToString()
        {
            return new StringBuilder()
                .Append(Type.ToString())
                .Append(' ')
                .Append(UtcTimestamp.ToString("O"))
                .Append(' ')
                .Append(Type switch
                {
                    PacketType.Text => Encoding.Unicode.GetString(Content.Span),
                    _ => MemoryMarshal.Cast<byte, char>(Content.Span)
                })
                .ToString();
        }
    }
}
