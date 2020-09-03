#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace DisfigureCore
{
    public delegate ValueTask MessageReceivedCallback(Connection origin, Packet packet);

    public class Connection : IDisposable
    {
        public const int BUFFER_SIZE = 1024;

        private static readonly ArrayPool<byte> _Buffers = ArrayPool<byte>.Create(BUFFER_SIZE, 8);

        public static TimeSpan DefaultLoopDelay = TimeSpan.FromSeconds(0.5d);

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;

        private byte[] _Buffer;
        private List<byte> _PendingHeader;
        private List<byte> _PendingContent;

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

        public void BeginListen(CancellationToken cancellationToken, TimeSpan loopDelay) =>
            Task.Run(() => BeginListenAsyncInternal(cancellationToken, loopDelay), cancellationToken);

        private async Task BeginListenAsyncInternal(CancellationToken cancellationToken, TimeSpan loopDelay)
        {
            try
            {
                Log.Information($"Beginning read loop for connection {Guid}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    await ReadAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(loopDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { }
        }

        #endregion

        #region Reading Data

        private async ValueTask ReadAsync(CancellationToken cancellationToken)
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
            int endIndex = _ReadPosition + (Packet.HEADER_LENGTH - _PendingHeader.Count);

            if (endIndex > _Buffer.Length)
            {
                endIndex = _Buffer.Length;
            }

            _PendingHeader.AddRange(_Buffer[startIndex..endIndex]);
            _ReadPosition = endIndex;

            if (_PendingHeader.Count == Packet.HEADER_LENGTH)
            {
                string contentLength = Encoding.ASCII.GetString(_PendingHeader.GetRange(Packet.HEADER_LENGTH - 4, 4).ToArray());
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
                await CompileMessageAndCallbackAsync().ConfigureAwait(false);
            }
        }


        private async ValueTask ReadIntoBufferAsync(CancellationToken cancellationToken)
        {
            _BufferedLength = await _Stream.ReadAsync(_Buffer, cancellationToken).ConfigureAwait(false);
            _ReadPosition = 0;
        }

        #endregion

        #region Writing Data

        // Ensure that write operations (which are usually called from outside the `Connection`
        //     object) do not use `.ConfigureAwait(false)`. This is so any external contexts are
        //    maintained.

        public async ValueTask WriteAsync(CancellationToken cancellationToken, Packet packet)
        {
            await _Stream.WriteAsync(packet.Serialize(), cancellationToken);
            await _Stream.FlushAsync(cancellationToken);
        }

        public async ValueTask WriteAsync(CancellationToken cancellationToken, params Packet[] packets)
        {
            foreach (Packet packet in packets)
            {
                await _Stream.WriteAsync(packet.Serialize(), cancellationToken);
            }

            await _Stream.FlushAsync(cancellationToken);
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

        public event MessageReceivedCallback? MessageReceived;

        private async ValueTask CompileMessageAndCallbackAsync()
        {
            static (DateTime, PacketType, int) DeserializeHeaderInternal(List<byte> headerBytes)
            {
                string[] headers = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(' ');

                if ((headers.Length != 3)
                    || !DateTime.TryParse(headers[0], out DateTime timestamp)
                    || !int.TryParse(headers[1], out int messageType)
                    || !Enum.IsDefined(typeof(PacketType), messageType)
                    || !int.TryParse(headers[2], out int contentLength))
                {
                    throw new FormatException("Header has invalid format.");
                }

                return (timestamp, (PacketType)messageType, contentLength);
            }

            (DateTime timestamp, PacketType messageType, int _) = DeserializeHeaderInternal(_PendingHeader);
            Packet packet = new Packet(timestamp, messageType, _PendingContent.ToArray());

            _PendingHeader.Clear();
            _PendingContent.Clear();

            if (!(MessageReceived is null))
            {
                await MessageReceived.Invoke(this, packet).ConfigureAwait(false);
            }

            if (_ReadPosition == _BufferedLength)
            {
                // reset read position in case are not moving on to new message in same buffer
                _ReadPosition = 0;
            }

            State = ConnectionState.Idle;
        }

        #endregion

        #region Dispose

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                _Client.Dispose();
                _Stream.Dispose();
            }

            _Buffer = null!;
            _PendingHeader = null!;
            _PendingContent = null!;
            _Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
