#region

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure.Server
{
    public class Server : Module
    {
        private readonly TcpListener _Listener;

        public Server(IPAddress ip, int port) => _Listener = new TcpListener(ip, port);

        public async Task Start()
        {
            _Listener.Start();

            await AcceptConnections();
        }

        private async ValueTask AcceptConnections()
        {
            try
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync();
                    Log.Information($"Accepted new connection from {tcpClient.Client.RemoteEndPoint}.");

                    Connection connection = await EstablishConnectionAsync(tcpClient, true);
                    connection.TextPacketReceived += OnTextPacketReceived;

                    await CommunicateServerIdentities(connection);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        private async ValueTask CommunicateServerIdentities(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);

            await SendChannelList(connection);

            await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);
        }

        private async ValueTask SendChannelList(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(Channels.Values.Select(channel => (PacketType.ChannelIdentity, utcTimestamp, channel.Serialize())),
                CancellationToken);
        }

        #region Events

        private async ValueTask OnTextPacketReceived(Connection connection, Packet packet)
        {
            await connection.WriteAsync(packet.Type, DateTime.UtcNow, packet.Content, CancellationToken);
        }

        #endregion
    }
}
