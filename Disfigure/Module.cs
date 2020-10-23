#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;

#endregion


namespace Disfigure
{
    public abstract class Module : IDisposable
    {
        /// <summary>
        ///     <see cref="CancellationTokenSource" /> used to provide <see cref="CancellationToken" /> for async operations.
        /// </summary>
        protected readonly CancellationTokenSource CancellationTokenSource;

        /// <summary>
        ///     Thread-safe dictionary of current connections.
        /// </summary>
        protected readonly ConcurrentDictionary<Guid, Connection> Connections;

        protected Module()
        {
            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection>();

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

        /// <summary>
        ///     <see cref="CancellationToken" /> used for async operations.
        /// </summary>
        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        /// <summary>
        ///     Read-only representation of internal connections dictionary.
        /// </summary>
        public IReadOnlyDictionary<Guid, Connection> ReadOnlyConnections => Connections;

        protected virtual void RegisterConnection(Connection connection)
        {
            connection.Connected += OnConnected;
            connection.Disconnected += OnDisconnected;
            connection.PacketWritten += OnPacketWrittenAsync;
            connection.PacketReceived += OnPacketReceivedAsync;
        }

        /// <summary>
        ///     Forcibly (and as safely as possible) disconnects the given <see cref="Connection{T}" />.
        /// </summary>
        public void ForceDisconnect(Guid connectionIdentity)
        {
            if (!Connections.TryRemove(connectionIdentity, out Connection? connection)) return;

            connection.Dispose();
            Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Connection forcibly disconnected."));
        }


        #region Connection Events

        public event ConnectionEventHandler? Connected;

        public event ConnectionEventHandler? Disconnected;

        protected async ValueTask OnConnected(Connection connection)
        {
            if (Connected is not null) await Connected(connection);
        }

        protected async ValueTask OnDisconnected(Connection connection)
        {
            if (Disconnected is not null) await Disconnected(connection);
        }

        #endregion


        #region Packet Events

        public event PacketEventHandler? PacketWritten;

        public event PacketEventHandler? PacketReceived;

        private async ValueTask OnPacketWrittenAsync(Connection connection, Packet packet)
        {
            if (PacketWritten is not null) await PacketWritten(connection, packet);
        }

        private async ValueTask OnPacketReceivedAsync(Connection connection, Packet packet)
        {
            if (PacketReceived is not null) await PacketReceived(connection, packet);
        }

        #endregion


        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            CancellationTokenSource.Cancel();

            foreach ((Guid _, Connection connection) in Connections) connection?.Dispose();

            CancellationTokenSource.Cancel();
            _Disposed = true;
        }

        public void Dispose()
        {
            if (_Disposed) return;

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
