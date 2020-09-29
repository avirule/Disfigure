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
            await connection.Finalize(CancellationToken).Contextless();
            _ServerConnections.TryAdd(connection.Identity, connection);
            await connection.WriteAsync(PacketType.Connected, DateTime.UtcNow, connection.Identity.ToByteArray(),
                CancellationToken).Contextless();

            return connection;
        }

        #region PacketReceived Events

        protected override async ValueTask PacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Connect:
                    IPEndPoint endPoint = (IPEndPoint)new BinaryEndPoint(packet.Content);
                    TcpClient tcpClient = await ConnectionHelper.ConnectAsync(endPoint, 5, TimeSpan.FromMilliseconds(500d), CancellationToken)
                        .Contextless();
                    Connection newConnection = await EstablishServerConnectionAsync(tcpClient);
                    newConnection.PacketReceived += ServerPacketReceivedCallback;
                    break;
                case PacketType.Disconnect:
                    break;
            }
        }

        private async ValueTask ServerPacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Ping:
                    await ConnectionHelper.PongAsync(connection, packet.Content);
                    break;
            }
        }

        #endregion
    }
}
