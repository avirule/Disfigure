#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DisfigureCore;
using Serilog;

#endregion

namespace DisfigureServer
{
    public class Server : IDisposable
    {
        private readonly TcpListener _Listener;
        private readonly Dictionary<Guid, Connection> _Connections;

        private readonly Channel<Packet> _PacketBuffer;
        private readonly ChannelWriter<Packet> _PacketWriter;
        private readonly ChannelReader<Packet> _PacketReader;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;


        public Server()
        {
            const int port = 8898;
            IPAddress local = IPAddress.IPv6Loopback;
            _Listener = new TcpListener(local, port);
            _Connections = new Dictionary<Guid, Connection>();

            _PacketBuffer = Channel.CreateUnbounded<Packet>();
            _PacketReader = _PacketBuffer.Reader;
            _PacketWriter = _PacketBuffer.Writer;

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
                connection.MessageReceived += OnMessageReceived;
                _Connections.Add(guid, connection);

                Log.Information($"Connection from client {connection.Guid} established.");

                connection.BeginListen(_CancellationToken, Connection.DefaultLoopDelay);
            }
        }

        private static ValueTask OnMessageReceived(Connection connection, Packet packet)
        {
            Log.Information(packet.ToString());
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
