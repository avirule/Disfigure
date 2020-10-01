#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Microsoft.VisualBasic;
using Serilog;
using Serilog.Events;

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

        protected virtual async ValueTask<bool> RegisterConnection(Connection connection)
        {
            connection.Disconnected += DisconnectedCallback;
            connection.PacketReceived += PacketReceivedCallback;

            if (Connections.TryAdd(connection.Identity, connection))
            {
                await ShareIdentityAsync(connection).Contextless();
                return true;
            }
            else
            {
                return false;
            }
        }

        protected abstract ValueTask ShareIdentityAsync(Connection connection);

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

        protected virtual ValueTask DisconnectedCallback(Connection connection)
        {
            Connections.TryRemove(connection.Identity, out _);

            return default;
        }

        protected virtual ValueTask PacketReceivedCallback(Connection connection, Packet packet) => default;

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

#if DEBUG
            PacketDiagnosticGroup? packetDiagnosticGroup = DiagnosticsProvider.GetGroup<PacketDiagnosticGroup>();

            if (packetDiagnosticGroup is { })
            {
                (double avgConstruction, double avgDecryption) = packetDiagnosticGroup.GetAveragePacketTimes();

                Log.Information($"Construction: {avgConstruction:0.00}ms");
                Log.Information($"Decryption: {avgDecryption:0.00}ms");
            }
#endif

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
