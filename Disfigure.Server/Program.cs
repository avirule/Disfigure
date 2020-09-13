#region

using System;
using System.Net;
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
                server.Start();
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
