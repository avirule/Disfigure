#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure
{
    public abstract class Module<TPacket> : IDisposable where TPacket : struct
    {
        /// <summary>
        ///     <see cref="CancellationTokenSource" /> used to provide <see cref="CancellationToken" /> for async operations.
        /// </summary>
        protected readonly CancellationTokenSource CancellationTokenSource;

        /// <summary>
        ///     Thread-safe dictionary of current connections.
        /// </summary>
        protected readonly ConcurrentDictionary<Guid, Connection<TPacket>> Connections;

        /// <summary>
        ///     <see cref="CancellationToken" /> used for async operations.
        /// </summary>
        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        /// <summary>
        ///     Read-only representation of internal connections dictionary.
        /// </summary>
        public IReadOnlyDictionary<Guid, Connection<TPacket>> ReadOnlyConnections => Connections;

        protected Module()
        {
            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection<TPacket>>();

            Connected += connection =>
            {
                Connections.TryAdd(connection.Identity, connection);
                return default;
            };
            Disconnected += connection =>
            {
                Connections.TryRemove(connection.Identity, out _);
                return default;
            };
        }

        protected void RegisterConnection(Connection<TPacket> connection)
        {
            connection.Connected += OnConnected;
            connection.Disconnected += OnDisconnected;
            connection.PacketReceived += OnClientPacketReceived;
        }

        /// <summary>
        ///     Forcibly (and as safely as possible) disconnects the given <see cref="Connection{T}" />.
        /// </summary>
        public void ForceDisconnect(Guid connectionIdentity)
        {
            if (!Connections.TryRemove(connectionIdentity, out Connection<TPacket>? connection))
            {
                return;
            }

            connection.Dispose();
            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Connection forcibly disconnected."));
        }


        #region Packet Events

        public event PacketEventHandler<TPacket>? PacketReceived;

        private async Task OnClientPacketReceived(Connection<TPacket> connection, TPacket packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived(connection, packet);
            }
        }

        #endregion


        #region Connection Events

        public event ConnectionEventHandler<TPacket>? Connected;
        public event ConnectionEventHandler<TPacket>? Disconnected;


        protected virtual async Task OnConnected(Connection<TPacket> connection)
        {
            if (Connected is { })
            {
                await Connected(connection);
            }
        }

        protected virtual async Task OnDisconnected(Connection<TPacket> connection)
        {
            if (Disconnected is { })
            {
                await Disconnected(connection);
            }
        }

        #endregion


        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            CancellationTokenSource.Cancel();

            foreach ((Guid _, Connection<TPacket> connection) in Connections)
            {
                connection?.Dispose();
            }

            CancellationTokenSource.Cancel();
            _Disposed = true;
        }

        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
