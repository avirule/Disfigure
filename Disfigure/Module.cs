#region

using System;
using System.Collections.Concurrent;
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
    public delegate void ConsoleLineReadEventHandler(string line);

    public abstract class Module : IDisposable
    {
        protected readonly CancellationTokenSource CancellationTokenSource;
        protected readonly ConcurrentDictionary<Guid, Connection> Connections;
        protected readonly ConcurrentDictionary<Guid, Channel> Channels;

        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        public Module(LogEventLevel minimumLogLevel)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(minimumLogLevel).CreateLogger();

            CancellationTokenSource = new CancellationTokenSource();
            Connections = new ConcurrentDictionary<Guid, Connection>();
            Channels = new ConcurrentDictionary<Guid, Channel>();

#if DEBUG
            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

#endif
        }

        public abstract void Start();

        protected async ValueTask<Connection> EstablishConnectionAsync(TcpClient tcpClient, bool isServerModule)
        {
            Connection connection = new Connection(tcpClient, isServerModule);
            connection.Disconnected += OnDisconnected;
            await connection.Finalize(CancellationToken);
            Connections.TryAdd(connection.Identity, connection);

            return connection;
        }

        protected void ReadConsoleLoop()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                string? line = Console.ReadLine();

                if (!string.IsNullOrWhiteSpace(line))
                {
                    ConsoleLineRead?.Invoke(line);
                }
            }
        }

        public event ConsoleLineReadEventHandler? ConsoleLineRead;

        #region Connection Events

        private ValueTask OnDisconnected(Connection connection)
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
