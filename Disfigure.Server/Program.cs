#region

using System;
using System.Net;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static async Task Main()
        {
            const int port = 8898;

            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Verbose().CreateLogger();

            using Server server = new Server(IPAddress.IPv6Loopback, port);
            await server.Start();

            Console.ReadLine();
        }
    }
}
