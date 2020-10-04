using System;

namespace Disfigure.Net
{
    public interface IPacket<out TPacket> where TPacket : IPacket<TPacket>
    {
        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
