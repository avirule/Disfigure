#region

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace DisfigureCore.Net
{
    public enum ConnectionState
    {
        Idle,
        ReadingHeader,
        ReadingContent
    }

    public delegate ValueTask PacketReceivedCallback(Connection origin, Packet packet);

    public class Connection : IDisposable
    {
        public const int BUFFER_SIZE = 1024;

        private static readonly ArrayPool<byte> _Buffers = ArrayPool<byte>.Create(BUFFER_SIZE, 8);

        public static TimeSpan DefaultLoopDelay = TimeSpan.FromSeconds(0.5d);

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;

        private byte[] _Buffer;
        private byte[] _Header;
        private int _CurrentHeaderLength;
        private List<byte> _PendingContent;

        private int _BufferedLength;
        private int _ReadPosition;
        private int _RemainingContentLength;

        public Guid Guid { get; }
        public ConnectionState State { get; private set; }
        public bool CompleteRemoteIdentity { get; private set; }

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            Guid = guid;

            _Buffer = _Buffers.Rent(BUFFER_SIZE);
            _Header = new byte[Packet.HEADER_LENGTH];
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
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
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

        public async ValueTask WriteAsync(CancellationToken cancellationToken, IEnumerable<Packet> packets)
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

        public event PacketReceivedCallback? PacketReceived;
        public event PacketReceivedCallback? BeginIdentityReceived;
        public event PacketReceivedCallback? EndIdentityReceived;

        private async ValueTask RebuildPacketAndCallbackAsync()
        {
            static (DateTime, PacketType, int) DeserializeHeaderInternal(byte[] headerBytes)
            {
                long timestamp = BitConverter.ToInt64(headerBytes, Packet.TIMESTAMP_HEADER_OFFSET);
                byte packetType = headerBytes[Packet.PACKET_TYPE_HEADER_OFFSET];
                int contentLength = BitConverter.ToInt32(headerBytes, Packet.CONTENT_LENGTH_HEADER_OFFSET);

                return (DateTime.FromBinary(timestamp), (PacketType)packetType, contentLength);
            }

            (DateTime timestamp, PacketType packetType, int _) = DeserializeHeaderInternal(_Header);
            await OnPacketReceived(new Packet(timestamp, packetType, _PendingContent.ToArray()));

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

        private async ValueTask OnPacketReceived(Packet packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived.Invoke(this, packet).ConfigureAwait(false);
            }

            await InvokePacketTypeEvent(packet).ConfigureAwait(false);
        }

        private async ValueTask InvokePacketTypeEvent(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.BeginIdentity:
                    if (BeginIdentityReceived is { })
                    {
                        await BeginIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    }

                    break;
                case PacketType.EndIdentity:
                    if (EndIdentityReceived is { })
                    {
                        await EndIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    }

                    break;
            }
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
            _Header = null!;
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
