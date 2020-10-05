#region

using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;

#endregion

namespace Disfigure.Server.CLI
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

                ServerModuleConfiguration configuration = new ServerModuleConfiguration(Assembly.GetExecutingAssembly().GetName().Name, false);

                Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(configuration.LogLevel).CreateLogger();

                DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

                using ServerModule module = new ServerModule(new IPEndPoint(configuration.HostingIPAddress, configuration.HostingPort));
                module.Connected += Packet.SendEncryptionKeys;
                module.PacketReceived += PacketReceivedCallback;

                module.AcceptConnections(Packet.EncryptorAsync, Packet.FactoryAsync);
                Packet.PingPongLoop(module, TimeSpan.FromSeconds(5d), module.CancellationToken);

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

        private static ValueTask PacketReceivedCallback(Connection<Packet> connection, Packet packet)
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
