#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(LogEventLevel.Verbose).CreateLogger();

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Loopback, 8898);
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None);
            Connection<BasicPacket> connection = new Connection<BasicPacket>(tcpClient, BasicPacket.EncryptorAsync, BasicPacket.FactoryAsync);
            connection.Connected += BasicPacket.SendEncryptionKeys;
            connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.EncryptionKeys:
                        connection.AssignRemoteKeys(packet.Content);
                        break;
                    case PacketType.Ping:
                        await connection.WriteAsync(new BasicPacket(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                        break;
                    default:
                        Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                        break;
                }
            };

            await connection.StartAsync(CancellationToken.None);
            await connection.WriteAsync(new BasicPacket(PacketType.Connect, DateTime.UtcNow,
                new SerializableEndPoint(IPAddress.Loopback, 8898).Serialize()), CancellationToken.None);

            while (true)
            {
                Console.ReadKey();
            }
        }
    }
}
