#region

using System;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net.Packets
{
    public interface IPacket
    {
        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
