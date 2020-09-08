#region

using System.Threading.Tasks;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net
{
    public delegate ValueTask EncryptedPacketEventHandler(Connection origin, EncryptedPacket encryptedPacket);

    public enum EncryptedPacketType : byte
    {
        Encrypted,
        KeyExchange,
    }

    public readonly struct EncryptedPacket
    {
        public const int ENCRYPTION_HEADER_LENGTH = sizeof(byte) // IsEncrypted
                                                    + EncryptionProvider.PUBLIC_KEY_SIZE // public key
                                                    + sizeof(int); // packet data length

        public const int ENCRYPTION_PACKET_TYPE_OFFSET = 0;
        public const int PUBLIC_KEY_OFFSET = ENCRYPTION_PACKET_TYPE_OFFSET + sizeof(byte);
        public const int PACKET_DATA_LENGTH_OFFSET = PUBLIC_KEY_OFFSET + EncryptionProvider.PUBLIC_KEY_SIZE;

        public readonly EncryptedPacketType Type;
        public readonly byte[] PublicKey;
        public readonly byte[] PacketData;

        public EncryptedPacket(EncryptedPacketType type, byte[] publicKey, byte[] packetData) =>
            (Type, PublicKey, PacketData) = (type, publicKey, packetData);
    }
}
