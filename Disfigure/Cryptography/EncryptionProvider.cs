#region

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Disfigure.Net;

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
        private readonly byte[] _PublicKey;

        private byte[]? _RemotePublicKey;

        public bool EncryptionNegotiated { get; private set; }

        public byte[] PublicKey => _PublicKey;
        public byte[] IV => _AES.IV;

        public EncryptionProvider()
        {
            _AES = new AesCryptoServiceProvider();
            _PrivateKey = new byte[PRIVATE_KEY_SIZE];
            _PublicKey = new byte[PUBLIC_KEY_SIZE];

            GeneratePrivateKey();
            GeneratePublicKey();
        }


        private void GeneratePrivateKey() => _CryptoRandom.GetBytes(_PrivateKey);

        private unsafe void GeneratePublicKey()
        {
            fixed (byte* privateKey = _PrivateKey)
            fixed (byte* publicKey = _PublicKey)
            {
                int result = TinyECDH.GenerateKeys((IntPtr)publicKey, (IntPtr)privateKey);
            }
        }

        private unsafe void DeriveKey(byte[] remotePublicKey, ref byte[] derivedKey)
        {
            fixed (byte* privateKey = _PrivateKey)
            fixed (byte* remoteKey = remotePublicKey)
            fixed (byte* derivedKeyFixed = derivedKey)
            {
                int result = TinyECDH.GenerateSharedKey((IntPtr)privateKey, (IntPtr)remoteKey, (IntPtr)derivedKeyFixed);
            }
        }

        public void AssignRemoteKeys(byte[] remoteIV, byte[] remotePublicKey)
        {
            Debug.Assert(!EncryptionNegotiated, "Protocol requires that key exchanges happen ONLY ONCE.");

            _AES.IV = remoteIV;
            _RemotePublicKey = remotePublicKey;
            EncryptionNegotiated = true;
        }

        public async ValueTask<byte[]> Encrypt(byte[] unencryptedPacket)
        {
            if (!EncryptionNegotiated || _RemotePublicKey is null)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }

            byte[] sharedKey = _DerivedKeyPool.Rent();
            DeriveKey(_RemotePublicKey, ref sharedKey);
            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? encryptor = _AES.CreateEncryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, encryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(unencryptedPacket, 0, unencryptedPacket.Length);
            }

            Array.Clear(sharedKey, 0, sharedKey.Length);
            _DerivedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }

        public async ValueTask<byte[]> Decrypt(byte[] remotePublicKey, byte[] encryptedPacket)
        {
            if (!EncryptionNegotiated)
            {
                throw new CryptographicException("Key exchange has not been completed.");
            }

            byte[] sharedKey = _DerivedKeyPool.Rent();
            DeriveKey(remotePublicKey, ref sharedKey);

            _AES.Key = sharedKey;

            await using MemoryStream cipherBytes = new MemoryStream();
            using (ICryptoTransform? decryptor = _AES.CreateDecryptor())
            await using (CryptoStream cryptoStream = new CryptoStream(cipherBytes, decryptor, CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(encryptedPacket, 0, encryptedPacket.Length);
            }

            Array.Clear(sharedKey, 0, sharedKey.Length);
            _DerivedKeyPool.Return(sharedKey);

            return cipherBytes.ToArray();
        }

        public byte[] GenerateHeader(int packetDataLength)
        {
            byte[] encryptionHeader = new byte[EncryptedPacket.ENCRYPTION_HEADER_LENGTH];

            encryptionHeader[EncryptedPacket.ENCRYPTION_PACKET_TYPE_OFFSET] = 0;
            Buffer.BlockCopy(PublicKey, 0, encryptionHeader, EncryptedPacket.PUBLIC_KEY_OFFSET, PUBLIC_KEY_SIZE);
            Buffer.BlockCopy(BitConverter.GetBytes(packetDataLength), 0, encryptionHeader, EncryptedPacket.PACKET_DATA_LENGTH_OFFSET, sizeof(int));

            return encryptionHeader;
        }

        public byte[] GenerateKeyExchangePacket()
        {
            byte[] keyPacket = new byte[EncryptedPacket.ENCRYPTION_HEADER_LENGTH + _AES.IV.Length];
            keyPacket[EncryptedPacket.ENCRYPTION_PACKET_TYPE_OFFSET] = (byte)EncryptedPacketType.KeyExchange;
            Buffer.BlockCopy(PublicKey, 0, keyPacket, EncryptedPacket.PUBLIC_KEY_OFFSET, PUBLIC_KEY_SIZE);
            Buffer.BlockCopy(BitConverter.GetBytes(_AES.IV.Length), 0, keyPacket, EncryptedPacket.PACKET_DATA_LENGTH_OFFSET, sizeof(int));
            Buffer.BlockCopy(_AES.IV, 0, keyPacket, EncryptedPacket.ENCRYPTION_HEADER_LENGTH, _AES.IV.Length);

            return keyPacket;
        }
    }
}
