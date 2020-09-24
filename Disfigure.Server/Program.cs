#region

using System;
using System.Net;
using System.Threading.Tasks;
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
                using Server server = new Server(LogEventLevel.Verbose, IPAddress.IPv6Loopback, port);
                Task.Run(server.AcceptConnections);
                Task.Run(server.PingPongLoop);

                while (!server.CancellationToken.IsCancellationRequested)
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
