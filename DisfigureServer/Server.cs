#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DisfigureCore;
using DisfigureCore.Net;
using Serilog;

#endregion

namespace DisfigureServer
{
    public class Server : IDisposable
    {
        private readonly TcpListener _Listener;
        private readonly Dictionary<Guid, Connection> _Connections;
        private readonly Dictionary<Guid, Channel> _Channels;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;


        public Server()
        {
            const int port = 8898;
            IPAddress local = IPAddress.IPv6Loopback;
            _Listener = new TcpListener(local, port);
            _Connections = new Dictionary<Guid, Connection>();
            _Channels = new Dictionary<Guid, Channel>();
            _Channels.Add(Guid.NewGuid(), new Channel("Default, Test", true));

            _CancellationTokenSource = new CancellationTokenSource();
            _CancellationToken = _CancellationTokenSource.Token;
        }

        public async Task Start()
        {
            try
            {
                _Listener.Start();

                while (!_CancellationToken.IsCancellationRequested)
                {
                    await AcceptPendingConnections();
                }
            }
            catch (Exception ex) { }
        }

        private async ValueTask AcceptPendingConnections()
        {
            while (_Listener.Pending())
            {
                Guid guid = Guid.NewGuid();
                TcpClient client = await _Listener.AcceptTcpClientAsync();

                Log.Information($"Accepted new connection from {client.Client.RemoteEndPoint} with auto-generated GUID {guid}.");

                Connection connection = new Connection(guid, client);
                connection.PacketReceived += OnPacketReceived;
                _Connections.Add(guid, connection);

                Log.Information($"Connection from client {connection.Guid} established.");

                connection.BeginListen(_CancellationToken, Connection.DefaultLoopDelay);
            }
        }

        private static ValueTask OnPacketReceived(Connection connection, Packet packet)
        {
            Log.Verbose(packet.ToString());
            return default;
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
