#region

using System;
using System.Net;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static void Main()
        {
            const int port = 8898;

            try
            {
                DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

                using ServerModule serverModule = new ServerModule(LogEventLevel.Verbose, new IPEndPoint(IPAddress.Loopback, 8899));

                serverModule.AcceptConnections();
                serverModule.PingPongLoop();

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
