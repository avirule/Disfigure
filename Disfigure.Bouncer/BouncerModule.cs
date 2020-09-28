#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Disfigure.Server;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class BouncerModule : ServerModule
    {
        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress)
        {
            PacketReceived += OnPacketReceivedCallback;

            AcceptConnections();
            PingPongLoop();
        }



        #region PacketReceived Events

        private async ValueTask OnServerPacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Connect:
                    IPEndPoint endPoint = (IPEndPoint)new BinaryEndPoint(packet.Content);
                    TcpClient tcpClient = await ConnectionHelper.ConnectAsync(endPoint, 5, TimeSpan.FromMilliseconds(500d), CancellationToken).Contextless();
                    Connection newConnection = await EstablishConnectionAsync(tcpClient).Contextless();
                    await connection.WriteAsync(PacketType.Connected, DateTime.UtcNow, newConnection.Identity.ToByteArray(),
                        CancellationToken).Contextless();
                    break;
                case PacketType.Disconnect:
                    break;
            }
        }

        private async ValueTask OnClientPacketReceivedCallback(Connection connection, Packet packet) { }

        private async ValueTask OnPacketReceivedCallback(Connection connection, Packet packet)
        {
            if (packet.Type == PacketType.Ping)
            {
                Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken).Contextless();
            }
        }

        #endregion
    }
}
