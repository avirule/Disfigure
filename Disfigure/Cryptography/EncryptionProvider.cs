#region

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Collections;

#endregion

namespace Disfigure.Cryptography
{
    public class EncryptionProvider
    {
        public const int KEY_SIZE = 32;
        public const int PUBLIC_KEY_SIZE = KEY_SIZE * 2;
        public const int INITIALIZATION_VECTOR_SIZE = 16;

        private static readonly RNGCryptoServiceProvider _CryptoRandom = new RNGCryptoServiceProvider();
        private static readonly ObjectPool<byte[]> _DerivedKeyPool = new ObjectPool<byte[]>(() => new byte[KEY_SIZE]);

        private readonly byte[] _PrivateKey;

        private byte[]? _RemotePublicKey;

        public byte[] PublicKey { get; }
        public bool EncryptionNegotiated { get; private set; }

        public EncryptionProvider()
        {
            _PrivateKey = new byte[KEY_SIZE];
            PublicKey = new byte[PUBLIC_KEY_SIZE];

            GeneratePrivateKey();
            DerivePublicKey();
        }


        private void GeneratePrivateKey() => _CryptoRandom.GetBytes(_PrivateKey);

        private unsafe void DerivePublicKey()
        {
            fixed (byte* privateKeyFixed = _PrivateKey)
            fixed (byte* publicKeyFixed = PublicKey)
            {
                TinyECDH.DerivePublicKey(publicKeyFixed, privateKeyFixed);
            }
        }

        private unsafe void DeriveSharedKey(byte[] remotePublicKey, byte[] derivedKey)
        {
            fixed (byte* privateKeyFixed = _PrivateKey)
            fixed (byte* remotePublicKeyFixed = remotePublicKey)
            fixed (byte* derivedKeyFixed = derivedKey)
            {
                TinyECDH.DeriveSharedKey(privateKeyFixed, remotePublicKeyFixed, derivedKeyFixed);
            }
        }

        public void AssignRemoteKeys(byte[] remotePublicKey)
        {
            Debug.Assert(!EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            _RemotePublicKey = remotePublicKey;
            EncryptionNegotiated = true;
        }

        public async ValueTask<(byte[] initializationVector, byte[] encrypted)> Encrypt(byte[] unencrypted, CancellationToken cancellationToken)
        {
            if (!EncryptionNegotiated || _RemotePublicKey is null)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }

            if (unencrypted.Length == 0)
            {
                return (Array.Empty<byte>(), unencrypted);
            }

            byte[] derivedKey = _DerivedKeyPool.Rent();
            Array.Clear(derivedKey, 0, derivedKey.Length);

            DeriveSharedKey(_RemotePublicKey, derivedKey);

            using AesCryptoServiceProvider aes = new AesCryptoServiceProvider
            {
                Key = derivedKey
            };
            await using MemoryStream cipherBytes = new MemoryStream();
            using ICryptoTransform encryptor = aes.CreateEncryptor();
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, encryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(unencrypted, cancellationToken);
            }

            _DerivedKeyPool.Return(derivedKey);

            return (aes.IV, cipherBytes.ToArray());
        }

        public async ValueTask<byte[]> Decrypt(byte[] remoteInitializationVector, byte[] remotePublicKey, byte[] encrypted,
            CancellationToken cancellationToken)
        {
            if (!EncryptionNegotiated || _RemotePublicKey is null)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }
            else if (encrypted.Length == 0)
            {
                return encrypted;
            }

            byte[] derivedKey = _DerivedKeyPool.Rent();
            Array.Clear(derivedKey, 0, derivedKey.Length);

            DeriveSharedKey(remotePublicKey, derivedKey);

            using AesCryptoServiceProvider aes = new AesCryptoServiceProvider
            {
                Key = derivedKey,
                IV = remoteInitializationVector
            };
            await using MemoryStream cipherBytes = new MemoryStream();
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, decryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(encrypted, 0, encrypted.Length, cancellationToken);
            }

            _DerivedKeyPool.Return(derivedKey);

            return cipherBytes.ToArray();
        }
    }
}
