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
        private readonly ConcurrentDictionary<Guid, Connection<ECDHEncryptionProvider, Packet>> _ServerConnections;

        public BouncerModule(IPEndPoint hostAddress) : base(hostAddress) =>
            _ServerConnections = new ConcurrentDictionary<Guid, Connection<ECDHEncryptionProvider, Packet>>();

        public async ValueTask<Connection<ECDHEncryptionProvider, Packet>> EstablishServerConnectionAsync(IPEndPoint ipEndPoint,
            PacketSerializerAsync<Packet> packetSerializerAsync, PacketFactoryAsync<Packet> packetFactoryAsync)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken);
            Connection<ECDHEncryptionProvider, Packet> connection = new Connection<ECDHEncryptionProvider, Packet>(tcpClient, packetSerializerAsync,
                packetFactoryAsync);
            connection.Connected += OnServerConnected;
            connection.Disconnected += OnServerDisconnected;
            connection.PacketReceived += OnServerPacketReceived;
            await connection.StartAsync(CancellationToken);
            _ServerConnections.TryAdd(connection.Identity, connection);

            return connection;
        }


        #region Server PacketReceived Events

        public event PacketEventHandler<ECDHEncryptionProvider, Packet>? ServerPacketReceived;

        private async ValueTask OnServerPacketReceived(Connection<ECDHEncryptionProvider, Packet> connection, Packet packet)
        {
            if (ServerPacketReceived is { })
            {
                await ServerPacketReceived(connection, packet);
            }
        }

        #endregion


        #region Server Connection Events

        public event ConnectionEventHandler<ECDHEncryptionProvider, Packet>? ServerConnected;
        public event ConnectionEventHandler<ECDHEncryptionProvider, Packet>? ServerDisconnected;

        private async ValueTask OnServerConnected(Connection<ECDHEncryptionProvider, Packet> connection)
        {
            if (ServerConnected is { })
            {
                await ServerConnected(connection);
            }
        }

        private async ValueTask OnServerDisconnected(Connection<ECDHEncryptionProvider, Packet> connection)
        {
            if (ServerDisconnected is { })
            {
                await ServerDisconnected(connection);
            }
        }

        #endregion
    }
}
