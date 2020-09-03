#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DisfigureCore;
using Serilog;

#endregion

namespace DisfigureClient
{
    public class Client : IDisposable
    {
        private readonly Dictionary<Guid, Connection> _Connections;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;

        public IReadOnlyDictionary<Guid, Connection> Connections => _Connections;

        public Client()
        {
            // todo perhaps use a server descriptor or something
            _Connections = new Dictionary<Guid, Connection>();

            _CancellationTokenSource = new CancellationTokenSource();
            _CancellationToken = _CancellationTokenSource.Token;
        }

        public async ValueTask EstablishConnection(IPEndPoint ipEndPoint, TimeSpan retryDelay)
        {
            const int maximum_retries = 5;

            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Information($"Attempting to establish connection to {ipEndPoint}.");

            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to establish connection to {ipEndPoint}. Retrying...");
                    await Task.Delay(retryDelay, _CancellationToken);

                    if (tries == maximum_retries)
                    {
                        Log.Error($"Failed to establish connection to {ipEndPoint}.");
                        return;
                    }

                    tries += 1;
                }
            }

            Guid guid = Guid.NewGuid();

            Log.Information($"Established connection to {ipEndPoint} with auto-generated GUID {guid}");

            FinalizeConnection(guid, tcpClient);
        }

        private void FinalizeConnection(Guid guid, TcpClient tcpClient)
        {
            Connection connection = new Connection(guid, tcpClient);
            _Connections.Add(connection.Guid, connection);

            Log.Information($"Connection {connection.Guid} finalized.");

            connection.BeginListen(_CancellationToken, Connection.DefaultLoopDelay);
        }

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
                foreach (Connection connection in _Connections.Values)
                {
                    connection?.Dispose();
                }
            }

            _CancellationTokenSource.Cancel();
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
