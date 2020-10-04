#region

using System;

#endregion

namespace Disfigure.Net.Packets
{
    public interface IPacket<out TPacket> where TPacket : IPacket<TPacket>
    {
        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
