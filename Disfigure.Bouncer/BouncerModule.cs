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
    public class BouncerModule<TPacket> : ServerModule<TPacket> where TPacket : IPacket<TPacket>
    {
        private readonly ConcurrentDictionary<Guid, Connection<TPacket>> _ServerConnections;

        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection<TPacket>>();

        public async ValueTask<Connection<TPacket>> EstablishServerConnectionAsync(IPEndPoint ipEndPoint,
            PacketEncryptorAsync<TPacket> packetEncryptorAsync, PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken)
                ;
            Connection<TPacket> connection = new Connection<TPacket>(tcpClient, packetEncryptorAsync, packetFactoryAsync);
            connection.Connected += OnServerConnected;
            connection.PacketReceived += OnServerPacketReceived;
            await connection.StartAsync(CancellationToken);
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }


        #region Server PacketReceived Events

        public event PacketEventHandler<TPacket>? ServerPacketReceived;

        private async ValueTask OnServerPacketReceived(Connection<TPacket> connection, TPacket packet)
        {
            if (ServerPacketReceived is { })
            {
                await ServerPacketReceived(connection, packet);
            }
        }

        #endregion


        #region Server Connection Events

        public event ConnectionEventHandler<TPacket>? ServerConnected;

        private async ValueTask OnServerConnected(Connection<TPacket> connection)
        {
            if (ServerConnected is { })
            {
                await ServerConnected(connection);
            }
        }

        #endregion
    }
}
