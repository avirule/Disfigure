#region

using System;
using System.Buffers;
using System.Runtime.InteropServices;
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
        BeginIdentity,
        Identity,
        ChannelIdentity,
        EndIdentity
    }

    public delegate ValueTask PacketEventHandler(Connection origin, Packet packet);

    public readonly struct Packet
    {
        #region Static

        public const int OFFSET_PACKET_TYPE = 0;
        public const int OFFSET_TIMESTAMP = OFFSET_PACKET_TYPE + sizeof(byte);
        public const int HEADER_LENGTH = OFFSET_TIMESTAMP + sizeof(long);

        #endregion

        public PacketType Type { get; }
        public DateTime UtcTimestamp { get; }
        public Memory<byte> Content { get; }

        public Packet(PacketType type, DateTime utcTimestamp, Memory<byte> content)
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
