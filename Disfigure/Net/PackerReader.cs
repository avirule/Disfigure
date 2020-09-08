#region

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;

#endregion

namespace Disfigure.Net
{
    public class PackerReader
    {
        private const int _BUFFER_SIZE = 1024;

        private readonly NetworkStream _Stream;

        private readonly byte[] _Buffer;
        private readonly List<byte> _EncryptionHeaderBuffer;
        private readonly List<byte> _PacketDataBuffer;

        private int _ReadIndex;
        private int _BufferedLength;
        private int _RemainingPacketDataLength;

        public PackerReader(NetworkStream networkStream)
        {
            _Stream = networkStream;

            _Buffer = new byte[_BUFFER_SIZE];
            _EncryptionHeaderBuffer = new List<byte>();
            _PacketDataBuffer = new List<byte>();
            _ReadIndex = 0;
        }

        public async ValueTask ReadPacketAsync(CancellationToken cancellationToken)
        {
            EncryptedPacket BuildEncryptedPacketInternal()
            {
                EncryptedPacketType type = (EncryptedPacketType)_EncryptionHeaderBuffer[EncryptedPacket.ENCRYPTION_PACKET_TYPE_OFFSET];
                byte[] publicKey = new byte[EncryptionProvider.PUBLIC_KEY_SIZE];
                _EncryptionHeaderBuffer.CopyTo(EncryptedPacket.PUBLIC_KEY_OFFSET, publicKey, 0, publicKey.Length);

                return new EncryptedPacket(type, publicKey, _PacketDataBuffer.ToArray());
            }

            await ReadEncryptionHeaderAsync(cancellationToken).ConfigureAwait(false);
            await ReadPacketDataAsync(cancellationToken).ConfigureAwait(false);

            EncryptedPacket encryptedPacket = BuildEncryptedPacketInternal();
            await CallbackEncryptedPacket(encryptedPacket).ConfigureAwait(false);
        }

        private async ValueTask ReadEncryptionHeaderAsync(CancellationToken cancellationToken)
        {
            while (_EncryptionHeaderBuffer.Count < EncryptedPacket.ENCRYPTION_HEADER_LENGTH)
            {
                await AttemptRebuffer(cancellationToken).ConfigureAwait(false);

                int endIndex = Math.Min(_ReadIndex + (EncryptedPacket.ENCRYPTION_HEADER_LENGTH - _EncryptionHeaderBuffer.Count), _BufferedLength);

                _EncryptionHeaderBuffer.AddRange(_Buffer[_ReadIndex..endIndex]);
                _ReadIndex = endIndex;
            }

            _RemainingPacketDataLength = BitConverter.ToInt32(_EncryptionHeaderBuffer.ToArray(), EncryptedPacket.PACKET_DATA_LENGTH_OFFSET);
        }

        private async ValueTask ReadPacketDataAsync(CancellationToken cancellationToken)
        {
            while (_RemainingPacketDataLength > 0)
            {
                await AttemptRebuffer(cancellationToken).ConfigureAwait(false);

                int endIndex = Math.Min(_ReadIndex + _RemainingPacketDataLength, _BufferedLength);
                int readPacketDataLength = endIndex - _ReadIndex;

                _PacketDataBuffer.AddRange(_Buffer[_ReadIndex..endIndex]);
                _RemainingPacketDataLength -= readPacketDataLength;
                _ReadIndex += readPacketDataLength;
            }
        }


        private async ValueTask AttemptRebuffer(CancellationToken cancellationToken)
        {
            if (_ReadIndex >= _BufferedLength)
            {
                _BufferedLength = await _Stream.ReadAsync(_Buffer, cancellationToken).ConfigureAwait(false);
                _ReadIndex = 0;
            }
        }

        #region Events

        public event EncryptedPacketEventHandler? EncryptedPacketReceived;

        private async ValueTask CallbackEncryptedPacket(EncryptedPacket encryptedPacket)
        {
            if (EncryptedPacketReceived is { })
            {
                await EncryptedPacketReceived.Invoke(null!, encryptedPacket).ConfigureAwait(false);
            }

            _EncryptionHeaderBuffer.Clear();
            _PacketDataBuffer.Clear();
        }

        #endregion
    }
}
