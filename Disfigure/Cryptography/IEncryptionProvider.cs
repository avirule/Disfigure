#region

using System;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.Cryptography
{
    public interface IEncryptionProvider
    {
        byte[] PublicKey { get; }
        public bool IsEncryptable { get; }

        Task<(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted)>
            EncryptAsync(ReadOnlyMemory<byte> unencrypted, CancellationToken cancellationToken);

        Task<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted,
            CancellationToken cancellationToken);
    }
}
