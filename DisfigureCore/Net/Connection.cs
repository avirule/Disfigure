#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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

    public delegate ValueTask ConnectionEventHandler(Connection connection);

    public delegate ValueTask PacketEventHandler(Connection origin, Packet packet);

    public class Connection : IDisposable
    {
        public static TimeSpan DefaultLoopDelay = TimeSpan.FromMilliseconds(5d);

        private readonly TcpClient _Client;
        private readonly NetworkStream _Stream;

        private PackerReader _PackerReader;
        private long _CompleteRemoteIdentity;

        public Guid Guid { get; }
        public string Name { get; private set; }

        public bool CompleteRemoteIdentity => Interlocked.Read(ref _CompleteRemoteIdentity) == 1;

        public Connection(Guid guid, TcpClient client)
        {
            _Client = client;
            _Stream = _Client.GetStream();
            _PackerReader = new PackerReader(_Stream);
            _PackerReader.PacketReceived += OnPacketReceived;

            EndIdentityReceived += (origin, packet) =>
            {
                Interlocked.Exchange(ref _CompleteRemoteIdentity, 1);
                return default;
            };

            Guid = guid;
            Name = string.Empty;
        }

        #region Listening

        public void BeginListen(CancellationToken cancellationToken) =>
            Task.Run(() => BeginListenAsyncInternal(cancellationToken), cancellationToken);

        private async Task BeginListenAsyncInternal(CancellationToken cancellationToken)
        {
            try
            {
                Log.Debug($"Beginning read loop for connection {Guid}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    await _PackerReader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException ex)
            {
                if (ex.InnerException is SocketException)
                {
                    Log.Warning($"Connection at {_Client.Client.RemoteEndPoint} ({Guid}) forcibly closed connection.");

                    if (Disconnected is { })
                    {
                        await Disconnected.Invoke(this);
                    }
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

        #if DEBUG

        public async ValueTask WriteAsyncDebug(CancellationToken cancellationToken, IEnumerable<Packet> packets, TimeSpan writeDelay)
        {
            foreach (Packet packet in packets)
            {
                await _Stream.WriteAsync(packet.Serialize(), cancellationToken);
                await _Stream.FlushAsync(cancellationToken);
                await Task.Delay(writeDelay, cancellationToken);
            }

        }


        #endif

        #endregion


        #region Connection Events

        public event ConnectionEventHandler? Connected;
        public event ConnectionEventHandler? Disconnected;

        #endregion


        #region Packet Events

        public event PacketEventHandler? TextPacketReceived;
        public event PacketEventHandler? BeginIdentityReceived;
        public event PacketEventHandler? ChannelIdentityReceived;
        public event PacketEventHandler? EndIdentityReceived;

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

            _PackerReader = null;
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
