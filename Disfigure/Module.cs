#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure
{
    public abstract class Module : IDisposable
    {
        protected readonly CancellationTokenSource CancellationTokenSource;
        protected readonly ConcurrentDictionary<Guid, Connection> Connections;
        protected readonly ConcurrentDictionary<Guid, Channel> Channels;

        public CancellationToken CancellationToken => CancellationTokenSource.Token;
        public IReadOnlyDictionary<Guid, Connection> ReadOnlyConnections => Connections;

        protected Module(LogEventLevel minimumLogLevel)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(minimumLogLevel).CreateLogger();

            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection>();
            Channels = new ConcurrentDictionary<Guid, Channel>();
        }

        protected async ValueTask<Connection> EstablishConnectionAsync(TcpClient tcpClient)
        {
            Connection connection = new Connection(tcpClient);
            connection.Disconnected += OnDisconnected;

            await connection.Finalize(CancellationToken).Contextless();
            Connections.TryAdd(connection.Identity, connection);

            return connection;
        }

        #region Connection Events

        private ValueTask OnDisconnected(Connection connection)
        {
            Connections.TryRemove(connection.Identity, out _);
            return default;
        }

        #endregion

        #region Packet Events

        public event PacketEventHandler? PacketReceived;

        protected virtual async ValueTask OnPacketReceived(Connection connection, Packet packet)
        {
            if (PacketReceived is { })
            {
                await PacketReceived(connection, packet).Contextless();
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
