#region

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace DisfigureCore.Net
{
    public class PackerReader
    {
        private const int _BUFFER_SIZE = 16;

        private readonly NetworkStream _Stream;

        private readonly byte[] _Buffer;
        private readonly List<byte> _HeaderBuffer;
        private readonly List<byte> _ContentBuffer;

        private int _ReadIndex;
        private int _BufferedLength;
        private int _RemainingContentLength;

        public PackerReader(NetworkStream networkStream)
        {
            _Stream = networkStream;

            _Buffer = new byte[_BUFFER_SIZE];
            _HeaderBuffer = new List<byte>();
            _ContentBuffer = new List<byte>();
            _ReadIndex = 0;
        }

        public async ValueTask ReadPacketAsync(CancellationToken cancellationToken)
        {
            await ReadHeaderAsync(cancellationToken);
            await ReadContentAsync(cancellationToken);
            await BuildPacketAndInvokeAsync().ConfigureAwait(false);
        }

        private async ValueTask ReadHeaderAsync(CancellationToken cancellationToken)
        {
            while (_HeaderBuffer.Count < Packet.HEADER_LENGTH)
            {
                await AttemptRebuffer(cancellationToken);

                int endIndex = Math.Min(_ReadIndex + (Packet.HEADER_LENGTH - _HeaderBuffer.Count), _BufferedLength);

                _HeaderBuffer.AddRange(_Buffer[_ReadIndex..endIndex]);
                _ReadIndex = endIndex;
            }

            _RemainingContentLength = BitConverter.ToInt32(_HeaderBuffer.ToArray(), Packet.CONTENT_LENGTH_HEADER_OFFSET);
        }

        private async ValueTask ReadContentAsync(CancellationToken cancellationToken)
        {
            while (_RemainingContentLength > 0)
            {
                await AttemptRebuffer(cancellationToken);

                int endIndex = Math.Min(_ReadIndex + _RemainingContentLength, _BufferedLength);
                int readContentLength = endIndex - _ReadIndex;

                if ((_ReadIndex < 0) || (endIndex < 0)) { }

                _ContentBuffer.AddRange(_Buffer[_ReadIndex..endIndex]);
                _RemainingContentLength -= readContentLength;
                _ReadIndex += readContentLength;
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

        public event PacketEventHandler? PacketReceived;

        private async ValueTask BuildPacketAndInvokeAsync()
        {
            static (DateTime, PacketType, int) DeserializeHeaderInternal(byte[] headerBytes)
            {
                long timestamp = BitConverter.ToInt64(headerBytes, Packet.TIMESTAMP_HEADER_OFFSET);
                byte packetType = headerBytes[Packet.PACKET_TYPE_HEADER_OFFSET];
                int contentLength = BitConverter.ToInt32(headerBytes, Packet.CONTENT_LENGTH_HEADER_OFFSET);

                return (DateTime.FromBinary(timestamp), (PacketType)packetType, contentLength);
            }

            if (PacketReceived is { })
            {
                (DateTime timestamp, PacketType packetType, int _) = DeserializeHeaderInternal(_HeaderBuffer.ToArray());
                await PacketReceived.Invoke(null!, new Packet(timestamp, packetType, _ContentBuffer.ToArray()));
            }

            _HeaderBuffer.Clear();
            _ContentBuffer.Clear();
        }

        #endregion
    }
}
