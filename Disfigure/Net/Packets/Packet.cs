#region

using System;
using System.Runtime.InteropServices;
using System.Text;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net.Packets
{
    public readonly partial struct Packet : IPacket
    {
        private const int _OFFSET_DATA_LENGTH = 0;
        private const int _OFFSET_ALIGNMENT_CONSTANT = _OFFSET_DATA_LENGTH + sizeof(int);
        private const int _OFFSET_INITIALIZATION_VECTOR = _OFFSET_ALIGNMENT_CONSTANT + sizeof(int);
        private const int _ENCRYPTION_HEADER_LENGTH = _OFFSET_INITIALIZATION_VECTOR + ECDHEncryptionProvider.INITIALIZATION_VECTOR_SIZE;

        private const int _OFFSET_PACKET_TYPE = 0;
        private const int _OFFSET_TIMESTAMP = _OFFSET_PACKET_TYPE + sizeof(PacketType);
        private const int _HEADER_LENGTH = _OFFSET_TIMESTAMP + sizeof(long);

        private const int _TOTAL_HEADER_LENGTH = _ENCRYPTION_HEADER_LENGTH + _HEADER_LENGTH;

        public readonly ReadOnlyMemory<byte> Data;
        public readonly PacketType Type;
        public readonly DateTime UtcTimestamp;

        public ReadOnlySpan<byte> Content => Data.Slice(_HEADER_LENGTH).Span;

        public Packet(ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> destination = data.Span;

            Type = MemoryMarshal.Read<PacketType>(destination.Slice(_OFFSET_PACKET_TYPE));
            UtcTimestamp = MemoryMarshal.Read<DateTime>(destination.Slice(_OFFSET_TIMESTAMP));

            Data = data;
        }

        public Packet(PacketType packetType, DateTime utcTimestamp, ReadOnlySpan<byte> content)
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
    }
}
