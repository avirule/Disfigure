#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;

#endregion

namespace Disfigure.CLI.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            HostModuleOption hostModuleOption = CLIParser.Parse<HostModuleOption>(args);

            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(hostModuleOption.LogLevel).CreateLogger();

            IPEndPoint ipEndPoint = new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port);
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None);
            Connection<Packet> connection = new Connection<Packet>(tcpClient, new ECDHEncryptionProvider(), Packet.SerializerAsync,
                Packet.FactoryAsync);
            connection.Connected += Packet.SendEncryptionKeys;
            connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.EncryptionKeys:
                        connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.Content);
                        break;
                    case PacketType.Ping:
                        await connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                        break;
                    default:
                        Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                        break;
                }
            };

            await connection.FinalizeAsync(CancellationToken.None);
            connection.EncryptionProviderAs<ECDHEncryptionProvider>().WaitForRemoteKeys(CancellationToken.None);

            await connection.WriteAsync(new Packet(PacketType.Connect, DateTime.UtcNow,
                new SerializableEndPoint(IPAddress.Loopback, 8998).Serialize()), CancellationToken.None);

            while (true)
            {
                Console.ReadKey();
            }
        }
    }
}
