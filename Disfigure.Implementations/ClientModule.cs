#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;

#endregion

namespace Disfigure.Implementations
{
    public class ClientModule : Module<Packet>
    {
        public ClientModule() : base() {}

        public async Task ConnectAsync(IPEndPoint ipEndPoint)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None);
            Connection<Packet> connection = new Connection<Packet>(tcpClient, new ECDHEncryptionProvider(), Packet.SerializerAsync,
                Packet.FactoryAsync);
            RegisterConnection(connection);

            await connection.FinalizeAsync(CancellationToken.None);

            await connection.WriteAsync(new Packet(PacketType.Connect, DateTime.UtcNow,
                new SerializableEndPoint(IPAddress.Loopback, 8998).Serialize()), CancellationToken.None);
        }

        protected override void RegisterConnection(Connection<Packet> connection)
        {
            base.RegisterConnection(connection);

            connection.Connected += Packet.SendEncryptionKeys;
            connection.PacketReceived += PacketReceivedCallbackAsync;
        }

        #region Events

        private static async Task PacketReceivedCallbackAsync(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.Content);
                    break;
                case PacketType.Ping:
                    await connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                    break;
            }
        }

        #endregion
    }
}
