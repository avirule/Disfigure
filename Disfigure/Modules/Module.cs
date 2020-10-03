#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Modules
{
    public abstract class Module<TPacket> : IDisposable where TPacket : IPacket
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

        protected Module(LogEventLevel minimumLogLevel)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(minimumLogLevel).CreateLogger();

            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection<TPacket>>();
        }

        /// <summary>
        ///     Registers given <see cref="Connection" /> with <see cref="Module" />.
        /// </summary>
        /// <remarks>
        ///     This involves subscribing any relevant events or handlers, sharing <see cref="Module" /> identity information, and
        ///     adding the <see cref="Connection" /> to the <see cref="Connections" /> dictionary.
        /// </remarks>
        /// <param name="connection"><see cref="Connection" /> to be registered.</param>
        /// <returns>Fully registered <see cref="Connection" />.</returns>
        protected virtual async ValueTask<bool> RegisterConnection(Connection<TPacket> connection)
        {
            connection.Disconnected += DisconnectedCallback;
            connection.PacketReceived += OnClientPacketReceived;

            if (Connections.TryAdd(connection.Identity, connection))
            {
                await ShareIdentityAsync(connection).ConfigureAwait(false);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        ///     Shares identity information to given connection end point.
        /// </summary>
        /// <param name="connection"><see cref="Connection" /> to share identity information with.</param>
        protected abstract ValueTask ShareIdentityAsync(Connection<TPacket> connection);

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

        public event PacketEventHandler<TPacket>? ClientPacketReceived;

        private async ValueTask OnClientPacketReceived(Connection<TPacket> connection, TPacket packet)
        {
            if (ClientPacketReceived is { })
            {
                await ClientPacketReceived(connection, packet);
            }
        }

        #endregion

        #region Connection Events

        /// <summary>
        ///     Callback for the <see cref="Connection{T}.Disconnected" /> <see cref="ConnectionEventHandler{T}" />.
        /// </summary>
        /// <param name="connection"><see cref="Connection{T}" /> that has been disconnected.</param>
        protected virtual ValueTask DisconnectedCallback(Connection<TPacket> connection)
        {
            Connections.TryRemove(connection.Identity, out _);

            return default;
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
