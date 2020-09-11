#region

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;

#endregion

namespace Disfigure
{
    public class Module : IDisposable
    {
        protected readonly List<Connection> Connections;
        protected readonly CancellationTokenSource CancellationTokenSource;
        protected readonly Dictionary<Guid, Channel> Channels;

        public CancellationToken CancellationToken => CancellationTokenSource.Token;

        public Module()
        {
            Connections = new List<Connection>();
            CancellationTokenSource = new CancellationTokenSource();
            Channels = new Dictionary<Guid, Channel>();
        }

        protected async ValueTask<Connection> EstablishConnectionAsync(TcpClient tcpClient, bool isServerModule)
        {
            Connection connection = new Connection(Guid.NewGuid(), tcpClient, isServerModule);
            connection.Disconnected += OnDisconnected;
            await connection.Finalize(CancellationToken);
            Connections.Add(connection);

            return connection;
        }

        #region Connection Events

        private ValueTask OnDisconnected(Connection connection)
        {
            Connections.Remove(connection);
            return default;
        }

        #endregion

        #region IDisposable

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (Connection connection in Connections)
                {
                    connection?.Dispose();
                }
            }

            CancellationTokenSource.Cancel();
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
