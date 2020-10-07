#region

using System;

#endregion

namespace Disfigure.Net.Packets
{
    public interface IPacket
    {
        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
