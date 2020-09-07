using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Disfigure.Cryptography
{
    public class EncryptionProvider
    {
        public const int PRIVATE_KEY_SIZE = 72;
        public const int PUBLIC_KEY_SIZE = PRIVATE_KEY_SIZE * 2;

        private static readonly RNGCryptoServiceProvider _CryptoRandom = new RNGCryptoServiceProvider();
        private static readonly ArrayPool<byte> _SharedKeyPool = ArrayPool<byte>.Create(PUBLIC_KEY_SIZE, 8);

        private readonly AesCryptoServiceProvider _AES;
        private readonly byte[] _PrivateKey;
        private readonly byte[] _PublicKey;

        public EncryptionProvider()
        {
            _AES = new AesCryptoServiceProvider();
            _PrivateKey = new byte[PRIVATE_KEY_SIZE];
            _PublicKey = new byte[PUBLIC_KEY_SIZE];

            GeneratePrivateKey();
            GeneratePublicKey();
        }

        public bool EncryptionNegotiated { get; private set; }

        private void GeneratePrivateKey() => _CryptoRandom.GetBytes(_PrivateKey);

        private unsafe void GeneratePublicKey()
        {
            fixed (byte* privateKey = _PrivateKey)
            fixed (byte* publicKey = _PublicKey)
            {
                int result = TinyECDH.GenerateKeys((IntPtr)publicKey, (IntPtr)privateKey);
            }
        }

        private unsafe void GenerateSharedKey(byte[] remotePublicKey, ref byte[] sharedKey)
        {
            fixed (byte* privateKey = _PrivateKey)
            fixed (byte* remoteKey = remotePublicKey)
            fixed (byte* sharedKeyFixed = sharedKey)
            {
                int result = TinyECDH.GenerateSharedKey((IntPtr)privateKey, (IntPtr)remoteKey, (IntPtr)sharedKeyFixed);
            }
        }

        public void AssignRemoteIV(byte[] remoteIV)
        {
            Debug.Assert(!EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            _AES.IV = remoteIV;
            EncryptionNegotiated = true;
        }

        public async ValueTask<byte[]> Encrypt(byte[] remotePublicKey, byte[] unencryptedPacket)
        {
            if (!EncryptionNegotiated)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }

            byte[] sharedKey = _SharedKeyPool.Rent(PUBLIC_KEY_SIZE);
            GenerateSharedKey(remotePublicKey, ref sharedKey);
            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? encryptor = _AES.CreateEncryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, encryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(unencryptedPacket, 0, unencryptedPacket.Length);
            }

            _SharedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }

        public async ValueTask<byte[]> Decrypt(byte[] remotePublicKey, byte[] encryptedPacket)
        {
            if (!EncryptionNegotiated)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }

            byte[] sharedKey = _SharedKeyPool.Rent(PUBLIC_KEY_SIZE);
            GenerateSharedKey(remotePublicKey, ref sharedKey);

            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? decryptor = _AES.CreateDecryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, decryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(encryptedPacket, 0, encryptedPacket.Length);
            }

            _SharedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }
    }
}
