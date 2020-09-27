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
                using ServerModule serverModule = new ServerModule(LogEventLevel.Verbose, new IPEndPoint(IPAddress.IPv6Loopback, port));
                Task.Run(serverModule.AcceptConnections);
                Task.Run(serverModule.PingPongLoop);

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
