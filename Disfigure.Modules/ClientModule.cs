#region

using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.Modules
{
    public class ClientModule : Module<Packet>
    {
        public ClientModule()
        {
            Connected += Packet.SendEncryptionKeys;
            PacketReceived += PacketReceivedCallbackAsync;
        }

        public async Task<Connection<Packet>> ConnectAsync(IPEndPoint ipEndPoint)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken);
            Connection<Packet> connection = new Connection<Packet>(tcpClient, new ECDHEncryptionProvider(), Packet.SerializerAsync, Packet.FactoryAsync);
            RegisterConnection(connection);

            await connection.FinalizeAsync(CancellationToken);

            return connection;
        }

        #region Events

        private static async Task PacketReceivedCallbackAsync(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.ContentSpan);
                    break;

                case PacketType.Ping:
                    await connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.ContentSpan), CancellationToken.None);
                    break;
            }
        }

        #endregion
    }
}