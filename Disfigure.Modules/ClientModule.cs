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


namespace Disfigure.Modules
{
    public class ClientModule : Module
    {
        public ClientModule()
        {
            Connected += Packet.SendEncryptionKeys;
            PacketReceived += PacketReceivedCallbackAsync;
        }

        public async ValueTask<Connection> ConnectAsync(IPEndPoint ipEndPoint)
        {
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken);

            Connection connection =
                new Connection(tcpClient, new ECDHEncryptionProvider(), Packet.SerializerAsync, Packet.FactoryAsync);

            RegisterConnection(connection);

            await connection.FinalizeAsync(CancellationToken);

            return connection;
        }


        #region Events

        private static async ValueTask PacketReceivedCallbackAsync(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.ContentSpan);
                    break;

                case PacketType.Ping:
                    await connection.WriteAsync(Packet.Create(PacketType.Pong, DateTime.UtcNow, packet.ContentSpan), CancellationToken.None);
                    break;
            }
        }

        #endregion
    }
}
