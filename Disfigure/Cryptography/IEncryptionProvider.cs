#region

using System;
using System.Threading;
using System.Threading.Tasks;

#endregion


namespace Disfigure.Cryptography
{
    public interface IEncryptionProvider
    {
        public const int PRIVATE_KEY_SIZE = 32;
        public const int PUBLIC_KEY_SIZE = PRIVATE_KEY_SIZE * 2;
        public const int INITIALIZATION_VECTOR_SIZE = 16;

        byte[] PublicKey { get; }
        public bool IsEncryptable { get; }

        Task<(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted)>
            EncryptAsync(ReadOnlyMemory<byte> unencrypted, CancellationToken cancellationToken);

        Task<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted,
            CancellationToken cancellationToken);
    }
}
