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

        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection>();

        private async ValueTask<Connection> EstablishServerConnectionAsync(IPEndPoint ipEndPoint)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken).Contextless();
            Connection connection = new Connection(tcpClient);
            connection.PacketReceived += ServerPacketReceivedCallback;
            await connection.Finalize(CancellationToken).Contextless();
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }

        #region PacketReceived Events

        /// <inheritdoc />
        protected override async ValueTask PacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Connect:
                    Connection serverConnection =
                        await EstablishServerConnectionAsync((IPEndPoint)new SerializableEndPoint(packet.Content)).Contextless();
                    await connection.WriteAsync(PacketType.Connected, DateTime.UtcNow, serverConnection.Identity.ToByteArray(), CancellationToken)
                        .Contextless();
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

            await ConnectionHelper.PongAsync(connection, packet.Content).Contextless();
        }

        #endregion
    }
}
