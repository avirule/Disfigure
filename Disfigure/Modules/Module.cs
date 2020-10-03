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

        /// <summary>
        ///     <see cref="CancellationToken" /> used for async operations.
        /// </summary>
        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        /// <summary>
        ///     Read-only representation of internal connections dictionary.
        /// </summary>
        public IReadOnlyDictionary<Guid, Connection> ReadOnlyConnections => Connections;

        protected Module(LogEventLevel minimumLogLevel)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(minimumLogLevel).CreateLogger();

            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection>();
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
        protected virtual async ValueTask<bool> RegisterConnection(Connection connection)
        {
            connection.Disconnected += DisconnectedCallback;
            connection.PacketReceived += PacketReceivedCallback;

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
        protected abstract ValueTask ShareIdentityAsync(Connection connection);

        /// <summary>
        ///     Forcibly (and as safely as possible) disconnects the given <see cref="Connection" />.
        /// </summary>
        /// <param name="connection"><see cref="Connection" /> to disconnect.</param>
        protected void ForceDisconnect(Connection connection)
        {
            if (!Connections.TryRemove(connection.Identity, out _))
            {
                return;
            }

            connection.Dispose();
            Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Connection forcibly disconnected."));
        }


        #region Connection Events

        /// <summary>
        ///     Callback for the <see cref="Connection.Disconnected" /> <see cref="ConnectionEventHandler" />.
        /// </summary>
        /// <param name="connection"><see cref="Connection" /> that has been disconnected.</param>
        protected virtual ValueTask DisconnectedCallback(Connection connection)
        {
            Connections.TryRemove(connection.Identity, out _);

            return default;
        }

        /// <summary>
        ///     Callback for the <see cref="Connection.PacketReceived" /> <see cref="PacketEventHandler" />.
        /// </summary>
        /// <param name="connection"><see cref="Connection" /> from which the <see cref="BasicPacket" /> was received from.</param>
        /// <param name="basicPacket"><see cref="BasicPacket" /> that was received.</param>
        protected virtual ValueTask PacketReceivedCallback(Connection connection, BasicPacket basicPacket) => default;

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

            foreach ((Guid _, Connection connection) in Connections)
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
