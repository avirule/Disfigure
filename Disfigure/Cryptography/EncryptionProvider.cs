#region

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.Cryptography
{
    public class EncryptionProvider
    {
        public const int PRIVATE_KEY_SIZE = 32;
        public const int PUBLIC_KEY_SIZE = PRIVATE_KEY_SIZE * 2;
        public const int INITIALIZATION_VECTOR_SIZE = 16;

        private static readonly RNGCryptoServiceProvider _CryptoRandom = new RNGCryptoServiceProvider();
        private static readonly TimeSpan _EncryptionKeysWaitTimeout = TimeSpan.FromSeconds(5d);

        private readonly ManualResetEventSlim _EncryptionKeysWait;
        private readonly byte[] _PrivateKey;

        private byte[]? _DerivedKey;

        public byte[] PublicKey { get; }

        public bool IsEncryptable => _DerivedKey is { };

        public EncryptionProvider()
        {
            _EncryptionKeysWait = new ManualResetEventSlim(false);
            _PrivateKey = new byte[PRIVATE_KEY_SIZE];

            PublicKey = new byte[PUBLIC_KEY_SIZE];

            GeneratePrivateKey();
            DerivePublicKey();
        }

        public void WaitForRemoteKeys(CancellationToken cancellationToken) => _EncryptionKeysWait.Wait(cancellationToken);
        public void WaitForRemoteKeys(TimeSpan timeout) => _EncryptionKeysWait.Wait(timeout);


        #region Key Operations

        private void GeneratePrivateKey() => _CryptoRandom.GetBytes(_PrivateKey);

        private unsafe void DerivePublicKey()
        {
            fixed (byte* privateKeyFixed = _PrivateKey)
            fixed (byte* publicKeyFixed = PublicKey)
            {
                TinyECDH.DerivePublicKey(publicKeyFixed, privateKeyFixed);
            }
        }

        private unsafe void DeriveSymmetricKey(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> derivedKey)
        {
            fixed (byte* privateKeyFixed = _PrivateKey)
            fixed (byte* publicKeyFixed = publicKey)
            fixed (byte* derivedKeyFixed = derivedKey)
            {
                TinyECDH.DeriveSharedKey(privateKeyFixed, publicKeyFixed, derivedKeyFixed);
            }
        }

        public void AssignRemoteKeys(ReadOnlySpan<byte> remotePublicKey)
        {
            if (IsEncryptable)
            {
                Log.Warning("Protocol requires that key exchanges happen ONLY ONCE.");
            }
            else if (remotePublicKey.Length != PUBLIC_KEY_SIZE)
            {
                Log.Warning($"Protocol requires that public keys be {PUBLIC_KEY_SIZE} bytes.");
            }
            else
            {
                byte[] derivedRemoteKey = new byte[PUBLIC_KEY_SIZE];
                DeriveSymmetricKey(remotePublicKey, derivedRemoteKey);

                using SHA256CryptoServiceProvider sha256 = new SHA256CryptoServiceProvider();
                _DerivedKey = sha256.ComputeHash(derivedRemoteKey);

                _EncryptionKeysWait.Set();
            }
        }

        #endregion


        #region Encrypt / Decrypt

        public async ValueTask<(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted)>
            EncryptAsync(ReadOnlyMemory<byte> unencrypted, CancellationToken cancellationToken)
        {
            WaitForRemoteKeys(_EncryptionKeysWaitTimeout);

            if (!IsEncryptable)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }
            else if (unencrypted.Length == 0)
            {
                return (ReadOnlyMemory<byte>.Empty, unencrypted);
            }

            // DO NOT ADD USING STATEMENT FOR AesCryptoServiceProvider
            // I'm unsure why, but the AesCryptoServiceProvider dispose method
            // throws an AccessViolationException. The dispose method only
            // clears arrays, so it is safe to NOT USE the using statement.
            AesCryptoServiceProvider aes = new AesCryptoServiceProvider();
            await using MemoryStream cipherBytes = new MemoryStream();
            using ICryptoTransform encryptor = aes.CreateEncryptor(_DerivedKey!, aes.IV);
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, encryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(unencrypted, cancellationToken);
            }

            return (aes.IV, cipherBytes.ToArray());
        }

        public async ValueTask<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted,
            CancellationToken cancellationToken)
        {
            WaitForRemoteKeys(_EncryptionKeysWaitTimeout);

            if (!IsEncryptable)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }
            else if (encrypted.Length == 0)
            {
                return default;
            }

            // DO NOT ADD USING STATEMENT FOR AesCryptoServiceProvider
            // I'm unsure why, but the AesCryptoServiceProvider dispose method
            // throws an AccessViolationException. The dispose method only
            // clears arrays, so it is safe to NOT USE the using statement.
            AesCryptoServiceProvider aes = new AesCryptoServiceProvider();
            await using MemoryStream cipherBytes = new MemoryStream();
            using ICryptoTransform decryptor = aes.CreateDecryptor(_DerivedKey!, initializationVector.ToArray());
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, decryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(encrypted, cancellationToken);
            }

            return cipherBytes.ToArray();
        }

        #endregion
    }
}
