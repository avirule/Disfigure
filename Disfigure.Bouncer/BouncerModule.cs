#region

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Modules;
using Disfigure.Net;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class BouncerModule : ServerModule
    {
        private readonly ConcurrentDictionary<Guid, Connection<BasicPacket>> _ServerConnections;

        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection<BasicPacket>>();

        private async ValueTask<Connection<BasicPacket>> EstablishServerConnectionAsync(IPEndPoint ipEndPoint)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken)
                .ConfigureAwait(false);
            Connection<BasicPacket> connection = new Connection<BasicPacket>(tcpClient, BasicPacket.Factory);
            connection.PacketReceived += ServerPacketReceivedCallback;
            await connection.Finalize(CancellationToken).ConfigureAwait(false);
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }

        #region PacketReceived Events

        /// <inheritdoc />
        protected override async ValueTask PacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket basicPacket)
        {
            switch (basicPacket.Type)
            {
                case PacketType.Connect:
                    Connection<BasicPacket> serverConnection =
                        await EstablishServerConnectionAsync((IPEndPoint)new SerializableEndPoint(basicPacket.Content.Span)).ConfigureAwait(false);
                    await connection.WriteAsync(PacketType.Connected, DateTime.UtcNow, serverConnection.Identity.ToByteArray(), CancellationToken)
                        .ConfigureAwait(false);
                    break;
                case PacketType.Disconnect:
                    break;
            }
        }

        private static async ValueTask ServerPacketReceivedCallback(Connection connection, BasicPacket basicPacket)
        {
            if (basicPacket.Type != PacketType.Ping)
            {
                return;
            }

            await ConnectionHelper.PongAsync(connection, basicPacket.Content.ToArray()).ConfigureAwait(false);
        }

        #endregion
    }
}
