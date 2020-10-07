#region

using System;
using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace Disfigure.Net.Packets
{
    public readonly partial struct Packet
    {
        public ReadOnlyMemory<byte> Data { get; }
        public PacketType Type { get; }
        public DateTime UtcTimestamp { get; }

        public ReadOnlySpan<byte> Content => Data.Slice(HEADER_LENGTH).Span;

        public Packet(ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> destination = data.Span;

            Type = MemoryMarshal.Read<PacketType>(destination.Slice(OFFSET_PACKET_TYPE));
            UtcTimestamp = MemoryMarshal.Read<DateTime>(destination.Slice(OFFSET_TIMESTAMP));

            Data = data;
        }

        public Packet(PacketType packetType, DateTime utcTimestamp, ReadOnlySpan<byte> content)
        {
            Type = packetType;
            UtcTimestamp = utcTimestamp;

            Memory<byte> data = new byte[HEADER_LENGTH + content.Length];
            Span<byte> destination = data.Span;

            MemoryMarshal.Write(destination.Slice(OFFSET_PACKET_TYPE), ref packetType);
            MemoryMarshal.Write(destination.Slice(OFFSET_TIMESTAMP), ref utcTimestamp);
            content.CopyTo(destination.Slice(HEADER_LENGTH));

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
