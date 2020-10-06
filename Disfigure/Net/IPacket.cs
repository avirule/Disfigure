#region

using System;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net
{
    public interface IPacket
    {
        public const int ENCRYPTION_HEADER_LENGTH = sizeof(int) + EncryptionProvider.INITIALIZATION_VECTOR_SIZE;

        public ReadOnlyMemory<byte> Serialize();

        public string ToString();
    }
}
