#region

using System;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Net.Packets;
using Xunit;

#endregion

namespace Disfigure.Tests
{
    public class PacketTests
    {
        private class DummyEncryptionProvider : IEncryptionProvider
        {
            public byte[] PublicKey { get; }
            public bool IsEncryptable { get; }

            public DummyEncryptionProvider()
            {
                PublicKey = Array.Empty<byte>();
                IsEncryptable = false;
            }

            Task<(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted)> IEncryptionProvider.EncryptAsync(
                ReadOnlyMemory<byte> unencrypted, CancellationToken cancellationToken) => throw new NotImplementedException();

            Task<ReadOnlyMemory<byte>> IEncryptionProvider.DecryptAsync(ReadOnlyMemory<byte> initializationVector, ReadOnlyMemory<byte> encrypted,
                CancellationToken cancellationToken) => throw new NotImplementedException();
        }

        private const string _GUID_VALUE = "50dfb8f5-78c9-4053-a573-8b0659648893";

        private static readonly Packet _Packet;
        private static readonly byte[] _PacketSerialized;
        private static readonly byte[] _InitializationVector;

        static PacketTests()
        {
            Guid guid = Guid.Parse(_GUID_VALUE);
            _Packet = new Packet(PacketType.Ping, DateTime.MinValue, new ReadOnlySpan<byte>(guid.ToByteArray()));
            _PacketSerialized = new byte[]
            {
                49,
                0,
                0,
                0,
                119,
                239,
                64,
                12,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                5,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                245,
                184,
                223,
                80,
                201,
                120,
                83,
                64,
                165,
                115,
                139,
                6,
                89,
                100,
                136,
                147
            };
            _InitializationVector = new byte[]
            {
                12,
                6,
                12,
                6,
                12,
                6,
                12,
                6,
                12,
                6,
                12,
                6,
                12,
                6,
                12,
                6
            };
        }

        [Fact]
        public async Task VerifyPacketSerializerAsyncOutput()
        {
            ReadOnlyMemory<byte> serialized = await Packet.SerializerAsync(_Packet, new DummyEncryptionProvider(), CancellationToken.None);
            byte[] serializedArray = serialized.ToArray();

            Assert.Equal(_PacketSerialized, serializedArray);
        }

        // [Fact]
        // public async Task VerifyPacketFactoryAsyncOutput() { }
    }
}
