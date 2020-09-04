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
        public const int BUFFER_SIZE = 16;

        private static readonly ArrayPool<byte> _Buffers = ArrayPool<byte>.Create(BUFFER_SIZE, 8);

        private readonly NetworkStream _Stream;

        private readonly byte[] _Buffer;
        private readonly byte[] _HeaderBuffer;
        private readonly List<byte> _ContentBuffer;

        private int _BufferedLength;
        private int _ReadPosition;
        private int _CurrentHeaderLength;
        private int _RemainingContentLength;

        public ConnectionState State { get; private set; }

        public ConnectionReader(NetworkStream networkStream)
        {
            _Stream = networkStream;

            _Buffer = _Buffers.Rent(BUFFER_SIZE);
            _HeaderBuffer = new byte[Packet.HEADER_LENGTH];
            _ContentBuffer = new List<byte>();
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

            int copyLength = endIndex - startIndex;
            Buffer.BlockCopy(_Buffer, startIndex, _HeaderBuffer, _CurrentHeaderLength, copyLength);
            _CurrentHeaderLength += copyLength;
            _ReadPosition = endIndex;

            if (_CurrentHeaderLength == _HeaderBuffer.Length)
            {
                _RemainingContentLength = BitConverter.ToInt32(_HeaderBuffer, Packet.CONTENT_LENGTH_HEADER_OFFSET);
                State = ConnectionState.ReadingContent;
            }
        }

        private async ValueTask ReadContentAsync()
        {
            // if no content, continue
            if (_RemainingContentLength > 0)
            {
                int startIndex = _ReadPosition;
                int endIndex = _ReadPosition + _RemainingContentLength;

                if (endIndex > _BufferedLength)
                {
                    endIndex = _BufferedLength;
                }

                _ContentBuffer.AddRange(_Buffer[_ReadPosition..endIndex]);
                _RemainingContentLength -= endIndex - _ReadPosition;
                _ReadPosition += endIndex - startIndex;
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
            await ReadIntoBufferAsync(cancellationToken);

            State = ConnectionState.ReadingHeader;
        }

        #region Events

        public event PacketEventHandler? PacketReceived;

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
                (DateTime timestamp, PacketType packetType, int _) = DeserializeHeaderInternal(_HeaderBuffer);
                await PacketReceived.Invoke(null!, new Packet(timestamp, packetType, _ContentBuffer.ToArray()));
            }

            Array.Clear(_HeaderBuffer, 0, _HeaderBuffer.Length);
            _CurrentHeaderLength = 0;
            _ContentBuffer.Clear();

            if (_ReadPosition == _BufferedLength)
            {
                // reset read position in case are not moving on to new message in same buffer
                _ReadPosition = 0;
            }

            State = (_ReadPosition > 0) && (_ReadPosition < _Buffer.Length) ? ConnectionState.ReadingHeader : ConnectionState.Idle;
        }

        #endregion
    }
}
