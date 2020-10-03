#region

using System;
using System.Net;
using System.Reflection;
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

                serverModule.AcceptConnections(BasicPacket.Factory);

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
    }
}
