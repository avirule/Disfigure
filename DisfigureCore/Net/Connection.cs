#region

using System;
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
        public static TimeSpan DefaultLoopDelay = TimeSpan.FromSeconds(0.5d);

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;

        private ConnectionReader _ConnectionReader;
        private long _CompleteRemoteIdentity;

        public Guid Guid { get; }
        public string Name { get; private set; }

        public ConnectionState State => _ConnectionReader.State;
        public bool CompleteRemoteIdentity => Interlocked.Read(ref _CompleteRemoteIdentity) == 1;

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _ConnectionReader = new ConnectionReader(_Stream);
            _ConnectionReader.PacketReceived += OnPacketReceived;

            EndIdentityReceived += (origin, packet) =>
            {
                Interlocked.Exchange(ref _CompleteRemoteIdentity, 1);
                return default;
            };

            Guid = guid;
            Name = string.Empty;
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
                    await _ConnectionReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(loopDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
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


        #region Events

        public event PacketReceivedCallback? TextPacketReceived;
        public event PacketReceivedCallback? BeginIdentityReceived;
        public event PacketReceivedCallback? ChannelIdentityReceived;
        public event PacketReceivedCallback? EndIdentityReceived;

        private async ValueTask OnPacketReceived(Connection connection, Packet packet) => await InvokePacketTypeEvent(packet).ConfigureAwait(false);

        private async ValueTask InvokePacketTypeEvent(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Text when TextPacketReceived is { }:
                    await TextPacketReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.BeginIdentity when BeginIdentityReceived is { }:
                    await BeginIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.EndIdentity when EndIdentityReceived is { }:
                    await EndIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
                    break;
                case PacketType.ChannelIdentity when ChannelIdentityReceived is { }:
                    await ChannelIdentityReceived.Invoke(this, packet).ConfigureAwait(false);
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

            _ConnectionReader = null;
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
