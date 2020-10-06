#region

using System;

#endregion

namespace Disfigure.Net
{
    public interface IPacket
    {
        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
