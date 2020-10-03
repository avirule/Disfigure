#region

using System;
using System.Collections.Concurrent;
using System.ComponentModel.Design;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Modules;
using Disfigure.Net;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class BouncerModule<TPacket> : ServerModule<TPacket> where TPacket : IPacket
    {
        private readonly ConcurrentDictionary<Guid, Connection<TPacket>> _ServerConnections;

        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection<TPacket>>();

        public async ValueTask<Connection<TPacket>> EstablishServerConnectionAsync(IPEndPoint ipEndPoint,
            PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken)
                .ConfigureAwait(false);
            Connection<TPacket> connection = new Connection<TPacket>(tcpClient, packetFactoryAsync);
            connection.PacketReceived += OnPacketReceived;
            await connection.Finalize(CancellationToken).ConfigureAwait(false);
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }


        #region PacketReceived Events

        public event PacketEventHandler<TPacket>? ServerPacketReceived;

        private async ValueTask OnPacketReceived(Connection<TPacket> connection, TPacket packet)
        {
            if (ServerPacketReceived is { })
            {
                await ServerPacketReceived(connection, packet);
            }
        }

        #endregion
    }
}
