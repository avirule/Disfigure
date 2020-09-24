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
        public const int DERIVED_KEY_SIZE = 32;
        public const int PRIVATE_KEY_SIZE = 72;
        public const int PUBLIC_KEY_SIZE = PRIVATE_KEY_SIZE * 2;

        private static readonly RNGCryptoServiceProvider _CryptoRandom = new RNGCryptoServiceProvider();
        private static readonly ObjectPool<byte[]> _DerivedKeyPool = new ObjectPool<byte[]>(() => new byte[DERIVED_KEY_SIZE]);

        private readonly AesCryptoServiceProvider _AES;
        private readonly byte[] _PrivateKey;

        private byte[]? _RemotePublicKey;

        public byte[] PublicKey { get; }
        public bool EncryptionNegotiated { get; private set; }

        public byte[] IV => _AES.IV;

        public EncryptionProvider()
        {
            _AES = new AesCryptoServiceProvider();
            _PrivateKey = new byte[PRIVATE_KEY_SIZE];
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
                int result = TinyECDH.DerivePublicKey((IntPtr)publicKeyFixed, (IntPtr)privateKeyFixed);
            }
        }

        private unsafe void DeriveSharedKey(byte[] remotePublicKey, byte[] derivedKey)
        {
            fixed (byte* privateKeyFixed = _PrivateKey)
            fixed (byte* remotePublicKeyFixed = remotePublicKey)
            fixed (byte* derivedKeyFixed = derivedKey)
            {
                int result = TinyECDH.DeriveSharedKey((IntPtr)privateKeyFixed, (IntPtr)remotePublicKeyFixed, (IntPtr)derivedKeyFixed);
            }
        }

        public void AssignRemoteKeys(byte[] remoteIV, byte[] remotePublicKey)
        {
            Debug.Assert(!EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            if (remoteIV.Length > 0)
            {
                _AES.IV = remoteIV;
            }

            _RemotePublicKey = remotePublicKey;
            EncryptionNegotiated = true;
        }

        public async ValueTask<byte[]> Encrypt(byte[] unencrypted, CancellationToken cancellationToken)
        {
            if (!EncryptionNegotiated || _RemotePublicKey is null)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }
            else if (unencrypted.Length == 0)
            {
                return unencrypted;
            }

            byte[] sharedKey = _DerivedKeyPool.Rent();
            Array.Clear(sharedKey, 0, sharedKey.Length);

            DeriveSharedKey(_RemotePublicKey, sharedKey);
            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? encryptor = _AES.CreateEncryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, encryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(unencrypted, cancellationToken);
            }

            _DerivedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }

        public async ValueTask<byte[]> Decrypt(byte[] remotePublicKey, byte[] encrypted, CancellationToken cancellationToken)
        {
            if (!EncryptionNegotiated || _RemotePublicKey is null)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }
            else if (encrypted.Length == 0)
            {
                return encrypted;
            }

            byte[] sharedKey = _DerivedKeyPool.Rent();
            Array.Clear(sharedKey, 0, sharedKey.Length);

            DeriveSharedKey(remotePublicKey, sharedKey);
            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? decryptor = _AES.CreateDecryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, decryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(encrypted, 0, encrypted.Length, cancellationToken);
            }

            _DerivedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }
    }
}
