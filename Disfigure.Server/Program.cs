#region

using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
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

                using ServerModule<BasicPacket> module = new ServerModule<BasicPacket>(configuration.LogLevel,
                    new IPEndPoint(configuration.HostingIPAddress, configuration.HostingPort));
                module.Connected += BasicPacket.SendEncryptionKeys;
                module.PacketReceived += PacketReceivedCallback;

                module.AcceptConnections(BasicPacket.EncryptorAsync, BasicPacket.FactoryAsync);
                BasicPacket.PingPongLoop(module, TimeSpan.FromSeconds(5d), module.CancellationToken);

                while (!module.CancellationToken.IsCancellationRequested)
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

        private static ValueTask PacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket packet)
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
