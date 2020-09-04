#region

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<Guid, Connection> _ClientConnections;
        private readonly Dictionary<Guid, Channel> _Channels;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;


        public Server()
        {
            const int port = 8898;
            IPAddress local = IPAddress.IPv6Loopback;
            _Listener = new TcpListener(local, port);
            _ClientConnections = new Dictionary<Guid, Connection>();
            _Channels = new Dictionary<Guid, Channel>();
            Guid guid = Guid.NewGuid();
            _Channels.Add(guid, new Channel(guid, "Default, Test"));

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
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
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
                _ClientConnections.Add(guid, connection);

                Log.Information($"Connection from client {connection.Guid} established. Communicating server information.");
                await CommunicateServerInformation(connection);

                connection.BeginListen(_CancellationToken, Connection.DefaultLoopDelay);
            }
        }

        private async ValueTask CommunicateServerInformation(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(_CancellationToken, new Packet(utcTimestamp, PacketType.BeginIdentity, new byte[0]));

            await SendChannelList(connection);

            await connection.WriteAsync(_CancellationToken, new Packet(utcTimestamp, PacketType.EndIdentity, new byte[0]));
        }

        private async ValueTask SendChannelList(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(_CancellationToken,
                _Channels.Values.Select(channel => new Packet(utcTimestamp, PacketType.ChannelIdentity, channel.Serialize())));
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
                foreach (Connection connection in _ClientConnections.Values)
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
