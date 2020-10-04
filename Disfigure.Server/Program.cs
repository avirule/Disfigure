#region

using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

                ServerModuleConfiguration configuration = new ServerModuleConfiguration(Assembly.GetExecutingAssembly().GetName().Name, false);

                using ServerModule<BasicPacket> serverModule = new ServerModule<BasicPacket>(configuration.LogLevel,
                    new IPEndPoint(configuration.HostingIPAddress, configuration.HostingPort));
                serverModule.Connected += async conn => await conn.WriteAsync(new BasicPacket(PacketType.EncryptionKeys,
                    DateTime.UtcNow, conn.PublicKey), serverModule.CancellationToken);
                serverModule.ClientPacketReceived += ClientPacketReceivedCallback;

                serverModule.AcceptConnections(BasicPacket.EncryptorAsync, BasicPacket.FactoryAsync);
                BasicPacket.PingPongLoop(serverModule, TimeSpan.FromSeconds(5d), serverModule.CancellationToken);

                while (!serverModule.CancellationToken.IsCancellationRequested)
                {
                    Console.ReadKey();
                }
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }

        private static ValueTask ClientPacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.AssignRemoteKeys(packet.Content);
                    break;
            }

            return default;
        }
    }
}
