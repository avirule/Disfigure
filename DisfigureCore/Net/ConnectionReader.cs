#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace DisfigureCore.Net
{
    public class ConnectionReader
    {
        public const int BUFFER_SIZE = 1024;

        private static readonly ArrayPool<byte> _Buffers = ArrayPool<byte>.Create(BUFFER_SIZE, 8);

        private readonly NetworkStream _Stream;

        private readonly byte[] _Buffer;
        private readonly byte[] _Header;
        private readonly List<byte> _PendingContent;

        private int _BufferedLength;
        private int _ReadPosition;
        private int _CurrentHeaderLength;
        private int _RemainingContentLength;

        public ConnectionState State { get; private set; }

        public ConnectionReader(NetworkStream networkStream)
        {
            _Stream = networkStream;

            _Buffer = _Buffers.Rent(BUFFER_SIZE);
            _Header = new byte[Packet.HEADER_LENGTH];
            _PendingContent = new List<byte>();
            _ReadPosition = 0;
        }

        public async ValueTask ReadAsync(CancellationToken cancellationToken)
        {
            if (_ReadPosition >= _Buffer.Length)
            {
                await ReadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            }

            switch (State)
            {
                case ConnectionState.Idle:
                    await ProcessIdleAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectionState.ReadingHeader:
                    ReadHeader();
                    break;
                case ConnectionState.ReadingContent:
                    await ReadContentAsync().ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ReadHeader()
        {
            int startIndex = _ReadPosition;
            int endIndex = _ReadPosition + (Packet.HEADER_LENGTH - _CurrentHeaderLength);

            if (endIndex > _Buffer.Length)
            {
                endIndex = _Buffer.Length;
            }

            int indexCount = endIndex - startIndex;
            Buffer.BlockCopy(_Buffer, startIndex, _Header, _CurrentHeaderLength, indexCount);
            _CurrentHeaderLength += indexCount;
            _ReadPosition = endIndex;

            if (_CurrentHeaderLength == _Header.Length)
            {
                _RemainingContentLength = BitConverter.ToInt32(_Header, Packet.CONTENT_LENGTH_HEADER_OFFSET);
                State = ConnectionState.ReadingContent;
            }
        }

        private async ValueTask ReadContentAsync()
        {
            int startIndex = _ReadPosition;

            for (int index = startIndex;
                (index < _Buffer.Length) && (_RemainingContentLength > 0);
                index++, _ReadPosition++, _RemainingContentLength--)
            {
                _PendingContent.Add(_Buffer[index]);
            }

            if (_RemainingContentLength == 0)
            {
                await RebuildPacketAndCallbackAsync().ConfigureAwait(false);
            }
        }


        private async ValueTask ReadIntoBufferAsync(CancellationToken cancellationToken)
        {
            _BufferedLength = await _Stream.ReadAsync(_Buffer, cancellationToken).ConfigureAwait(false);
            _ReadPosition = 0;
        }

        private async ValueTask ProcessIdleAsync(CancellationToken cancellationToken)
        {
            if ((_ReadPosition > 0) && (_ReadPosition < _Buffer.Length))
            {
                State = ConnectionState.ReadingHeader;
                return;
            }
            else if (!_Stream.DataAvailable)
            {
                return;
            }

            await ReadIntoBufferAsync(cancellationToken);

            State = ConnectionState.ReadingHeader;
        }

        #region Events

        public event PacketReceivedCallback? PacketReceived;

        private async ValueTask RebuildPacketAndCallbackAsync()
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
                (DateTime timestamp, PacketType packetType, int _) = DeserializeHeaderInternal(_Header);
                await PacketReceived.Invoke(null!, new Packet(timestamp, packetType, _PendingContent.ToArray()));
            }

            Array.Clear(_Header, 0, _Header.Length);
            _CurrentHeaderLength = 0;
            _PendingContent.Clear();

            if (_ReadPosition == _BufferedLength)
            {
                // reset read position in case are not moving on to new message in same buffer
                _ReadPosition = 0;
            }

            State = ConnectionState.Idle;
        }

        #endregion
    }
}
