#region

using System;
using System.Net;
using System.Reflection;
using Disfigure.Diagnostics;
using Disfigure.Modules;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            ServerModuleConfiguration configuration = new ServerModuleConfiguration(Assembly.GetExecutingAssembly().GetName().Name!, true);

            BouncerModule bouncerModule = new BouncerModule(configuration.LogLevel, new IPEndPoint(configuration.HostingIPAddress,
                configuration.HostingPort));

            bouncerModule.AcceptConnections();
            bouncerModule.PingPongLoop();

            while (!bouncerModule.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }
}
