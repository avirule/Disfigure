#region

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class BouncerModule : ServerModule
    {
        private readonly ConcurrentDictionary<Guid, Connection> _ServerConnections;

        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress)
        {
            _ServerConnections = new ConcurrentDictionary<Guid, Connection>();

            AcceptConnections();
            PingPongLoop();
        }

        private async ValueTask<Connection> EstablishServerConnectionAsync(TcpClient tcpClient)
        {
            Connection connection = new Connection(tcpClient);
            connection.PacketReceived += ServerPacketReceivedCallback;
            await connection.Finalize(CancellationToken).Contextless();
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }

        #region PacketReceived Events

        protected override async ValueTask PacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Connect:
                    TcpClient tcpClient = await ConnectionHelper.ConnectAsync((IPEndPoint)new BinaryEndPoint(packet.Content),
                        ConnectionHelper.DefaultRetry, CancellationToken).Contextless();
                    Connection serverConnection = await EstablishServerConnectionAsync(tcpClient);
                    await connection.WriteAsync(PacketType.Connected, DateTime.UtcNow, serverConnection.Identity.ToByteArray(),
                        CancellationToken).Contextless();
                    break;
                case PacketType.Disconnect:
                    break;
            }
        }

        private static async ValueTask ServerPacketReceivedCallback(Connection connection, Packet packet)
        {
            if (packet.Type != PacketType.Ping)
            {
                return;
            }

            await ConnectionHelper.PongAsync(connection, packet.Content);
        }

        #endregion
    }
}
