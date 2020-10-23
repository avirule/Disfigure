#region

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic;

#endregion


namespace Disfigure.Net.Packets
{
    public readonly partial struct Packet
    {
        public ReadOnlyMemory<byte> Data { get; }

        public PacketType Type => MemoryMarshal.Read<PacketType>(Data.Span.Slice(OFFSET_PACKET_TYPE));
        public DateTime UtcTimestamp => MemoryMarshal.Read<DateTime>(Data.Span.Slice(OFFSET_TIMESTAMP));
        public ReadOnlyMemory<byte> ContentMemory => Data.Slice(HEADER_LENGTH);
        public ReadOnlySpan<byte> ContentSpan => Data.Span.Slice(HEADER_LENGTH);

        public Packet(ReadOnlyMemory<byte> data) => Data = data;

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
                    PacketType.Text => Encoding.Unicode.GetString(ContentSpan),
                    _ => MemoryMarshal.Cast<byte, char>(ContentSpan)
                })
                .ToString();
        }
    }
}
