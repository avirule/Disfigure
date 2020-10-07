using System;
using System.Threading;
using System.Threading.Tasks;

namespace Disfigure.Cryptography
{
    public interface IEncryptionProvider
    {
        byte[] PublicKey { get; }

        ValueTask<(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted)>
            EncryptAsync(ReadOnlyMemory<byte> unencrypted, CancellationToken cancellationToken);

        ValueTask<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted,
            CancellationToken cancellationToken);
    }
}
