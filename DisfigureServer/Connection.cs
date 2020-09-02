#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DisfigureCore;

#endregion

namespace DisfigureServer
{
    public delegate ValueTask MessageReceivedCallback(Connection origin, Message message);

    public class Connection
    {
        public const int BUFFER_SIZE = 22;

        private static readonly ArrayPool<byte> _Buffers = ArrayPool<byte>.Create(BUFFER_SIZE, 8);

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;
        private readonly byte[] _Buffer;
        private readonly List<byte> _PendingHeader;
        private readonly List<byte> _PendingContent;

        private int _BufferedLength;
        private int _ReadPosition;
        private int _RemainingContentLength;

        public Guid Guid { get; }
        public ConnectionState State { get; private set; }

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            Guid = guid;

            _Buffer = _Buffers.Rent(BUFFER_SIZE);
            _PendingHeader = new List<byte>();
            _PendingContent = new List<byte>();
            _ReadPosition = 0;
        }

        #region Connection Operations

        public async ValueTask BeginListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await ReadAsync(cancellationToken);
                }
            }
            catch (Exception ex) { }
        }

        public void Close()
        {
            _Buffers.Return(_Buffer);
            _PendingHeader.Clear();
            _PendingContent.Clear();
            _Client.Close();
        }

        #endregion

        #region Reading Data

        private async ValueTask ReadAsync(CancellationToken cancellationToken)
        {
            if (_ReadPosition >= _Buffer.Length)
            {
                await ReadIntoBufferAsync(cancellationToken);
            }

            switch (State)
            {
                case ConnectionState.Idle:
                    await ProcessIdleAsync(cancellationToken);
                    break;
                case ConnectionState.ReadingHeader:
                    ReadHeader();
                    break;
                case ConnectionState.ReadingContent:
                    await ReadContentAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ReadHeader()
        {
            int startIndex = _ReadPosition;
            int endIndex = _ReadPosition + (Message.HEADER_LENGTH - _PendingHeader.Count);

            if (endIndex > _Buffer.Length)
            {
                endIndex = _Buffer.Length;
            }

            _PendingHeader.AddRange(_Buffer[startIndex..endIndex]);
            _ReadPosition = endIndex;

            if (_PendingHeader.Count == Message.HEADER_LENGTH)
            {
                string contentLength = Encoding.ASCII.GetString(_PendingHeader.GetRange(Message.HEADER_LENGTH - 4, 4).ToArray());
                _RemainingContentLength = int.Parse(contentLength);
                State = ConnectionState.ReadingContent;
                _ReadPosition += 1; // advance past the space delimiter between header and content
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
                await CompileMessageAndCallbackAsync();
            }
        }


        private async ValueTask ReadIntoBufferAsync(CancellationToken cancellationToken)
        {
            _BufferedLength = await _Stream.ReadAsync(_Buffer, cancellationToken);
            _ReadPosition = 0;
        }

        #endregion


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

        public event MessageReceivedCallback MessageReceived;

        private async ValueTask CompileMessageAndCallbackAsync()
        {
            static (DateTime, MessageType, int) DeserializeHeaderInternal(List<byte> headerBytes)
            {
                string[] headers = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(' ');

                if ((headers.Length != 3)
                    || !DateTime.TryParse(headers[0], out DateTime timestamp)
                    || !int.TryParse(headers[1], out int messageType)
                    || !Enum.IsDefined(typeof(MessageType), messageType)
                    || !int.TryParse(headers[2], out int contentLength))
                {
                    throw new FormatException("Header has invalid format.");
                }

                return (timestamp, (MessageType)messageType, contentLength);
            }

            (DateTime timestamp, MessageType messageType, int _) = DeserializeHeaderInternal(_PendingHeader);
            Message message = new Message(timestamp, messageType, _PendingContent.ToArray());

            _PendingHeader.Clear();
            _PendingContent.Clear();

            if (!(MessageReceived is null))
            {
                await MessageReceived.Invoke(this, message);
            }

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
