#region

using Disfigure.Cryptography;
using Disfigure.Net.Packets;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

#endregion

namespace Disfigure.Tests
{
    public class PacketTests
    {
        private static readonly Packet _Packet;
        private static readonly byte[] _PacketSerialized;

        static PacketTests()
        {
            const string guid_value = "50dfb8f5-78c9-4053-a573-8b0659648893";

            Guid guid = Guid.Parse(guid_value);
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
        }

        [Fact]
        public async Task VerifyPacketSerializerAsyncOutput()
        {
            ReadOnlyMemory<byte> serialized = await Packet.SerializerAsync(_Packet, null, CancellationToken.None);
            byte[] packetSerialized = serialized.ToArray();

            // data length
            Assert.Equal
            (
                _PacketSerialized[..sizeof(int)],
                packetSerialized[..sizeof(int)]
            );

            // alignment constant
            Assert.Equal
            (
                _PacketSerialized[Packet.OFFSET_ALIGNMENT_CONSTANT..(Packet.OFFSET_ALIGNMENT_CONSTANT + sizeof(int))],
                packetSerialized[Packet.OFFSET_ALIGNMENT_CONSTANT..(Packet.OFFSET_ALIGNMENT_CONSTANT + sizeof(int))]
            );

            // initialization vector
            Assert.Equal
            (
                _PacketSerialized[Packet.OFFSET_INITIALIZATION_VECTOR..(Packet.OFFSET_INITIALIZATION_VECTOR
                                                                        + IEncryptionProvider.INITIALIZATION_VECTOR_SIZE)],
                packetSerialized[Packet.OFFSET_INITIALIZATION_VECTOR..(Packet.OFFSET_INITIALIZATION_VECTOR
                                                                       + IEncryptionProvider.INITIALIZATION_VECTOR_SIZE)]
            );

            const int offset = Packet.ENCRYPTION_HEADER_LENGTH;

            // packet type
            const int packet_type_offset = offset + Packet.OFFSET_PACKET_TYPE;
            Assert.Equal
            (
                _PacketSerialized[packet_type_offset..(packet_type_offset + sizeof(PacketType))],
                packetSerialized[packet_type_offset..(packet_type_offset + sizeof(PacketType))]
            );

            // utc timestamp
            const int utc_timestamp_offset = offset + Packet.OFFSET_TIMESTAMP;
            Assert.Equal
            (
                _PacketSerialized[utc_timestamp_offset..(utc_timestamp_offset + sizeof(long))],
                packetSerialized[utc_timestamp_offset..(utc_timestamp_offset + sizeof(long))]
            );

            // content
            const int content_offset = offset + Packet.HEADER_LENGTH;
            Assert.Equal
            (
                _PacketSerialized[content_offset..],
                packetSerialized[content_offset..]
            );
        }

        [Fact]
        public async Task VerifyPacketFactoryAsyncOutput()
        {
            ReadOnlySequence<byte> sequence = new ReadOnlySequence<byte>(_PacketSerialized);

            (bool success, SequencePosition consumed, Packet packet) = await Packet.FactoryAsync(sequence, null, CancellationToken.None);

            Assert.True(success);
            Assert.Equal(sequence.Length, consumed.GetInteger());
            Assert.Equal(_Packet.Type, packet.Type);
            Assert.Equal(_Packet.UtcTimestamp, packet.UtcTimestamp);
            Assert.Equal(_Packet.ContentSpan.ToArray(), packet.ContentSpan.ToArray());
            Assert.Equal(_Packet.Data.ToArray(), packet.Data.ToArray());
        }
    }
}