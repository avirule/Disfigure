#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure.Server
{
    public class Server : IDisposable
    {
        private readonly CancellationToken _CancellationToken;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly Dictionary<Guid, Channel> _Channels;
        private readonly Dictionary<Guid, Connection> _ClientConnections;
        private readonly TcpListener _Listener;


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
            _Listener.Start();

            await AcceptConnections();
        }

        private async ValueTask AcceptConnections()
        {
            try
            {
                while (!_CancellationToken.IsCancellationRequested)
                {
                    TcpClient client = await _Listener.AcceptTcpClientAsync();
                    Log.Information($"Accepted new connection from {client.Client.RemoteEndPoint}.");

                    Guid guid = Guid.NewGuid();
                    Log.Debug($"Auto-generated GUID for client {client.Client.RemoteEndPoint}: {guid}");

                    Connection connection = new Connection(guid, client);
                    await connection.SendEncryptionKeys(true, _CancellationToken);
                    connection.TextPacketReceived += OnTextPacketReceived;
                    connection.Disconnected += OnDisconnected;
                    _ClientConnections.Add(guid, connection);
                    connection.BeginListen(_CancellationToken);
                    Log.Debug($"Connection from client {connection.Guid} established.");

                    connection.PacketResetEvents[PacketType.EncryptionKeys].WaitOne();

                    await CommunicateServerInformation(connection);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        private async ValueTask CommunicateServerInformation(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            //await connection.WriteAsync(_CancellationToken, new Packet(utcTimestamp, PacketType.BeginIdentity, new byte[0]));

            await SendChannelList(connection);

            //await connection.WriteAsync(_CancellationToken, new Packet(utcTimestamp, PacketType.EndIdentity, new byte[0]));
        }

        private async ValueTask SendChannelList(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            //await connection.WriteAsync(_CancellationToken, _Channels.Values.Select(channel => new Packet(utcTimestamp, PacketType.ChannelIdentity, channel.Serialize())));
        }

        #region Events

        private ValueTask OnDisconnected(Connection connection)
        {
            Log.Information($"Connection {connection.Guid} closed.");
            _ClientConnections.Remove(connection.Guid);
            return default;
        }

        private static ValueTask OnTextPacketReceived(Connection connection, Packet packet)
        {
            Log.Verbose(packet.ToString());
            return default;
        }

        #endregion

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
