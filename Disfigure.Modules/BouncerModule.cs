#region

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;

#endregion


namespace Disfigure.Modules
{
    public class BouncerModule : ServerModule
    {
        private readonly ConcurrentDictionary<Guid, Connection> _ServerConnections;

        public BouncerModule(IPEndPoint hostAddress) : base(hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection>();

        public async Task<Connection> EstablishServerConnectionAsync(IPEndPoint ipEndPoint,
            PacketSerializerAsync packetSerializerAsync, PacketFactoryAsync packetFactoryAsync)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken);

            Connection connection = new Connection(tcpClient, new ECDHEncryptionProvider(), packetSerializerAsync,
                packetFactoryAsync);

            connection.Connected += OnServerConnected;
            connection.Disconnected += OnServerDisconnected;
            connection.PacketReceived += OnServerPacketReceived;
            await connection.FinalizeAsync(CancellationToken);
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }


        #region Server PacketReceived Events

        public event PacketEventHandler? ServerPacketReceived;

        private async ValueTask OnServerPacketReceived(Connection connection, Packet packet)
        {
            if (ServerPacketReceived is not null) await ServerPacketReceived(connection, packet);
        }

        #endregion


        #region Server Connection Events

        public event ConnectionEventHandler? ServerConnected;

        public event ConnectionEventHandler? ServerDisconnected;

        private async ValueTask OnServerConnected(Connection connection)
        {
            if (ServerConnected is not null) await ServerConnected(connection);
        }

        private async ValueTask OnServerDisconnected(Connection connection)
        {
            if (ServerDisconnected is not null) await ServerDisconnected(connection);
        }

        #endregion
    }
}
