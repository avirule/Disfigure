#region

using System;
using System.Net;
using Disfigure.Diagnostics;
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

                using ServerModule serverModule = new ServerModule(LogEventLevel.Verbose, new IPEndPoint(IPAddress.IPv6Loopback, port));
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
