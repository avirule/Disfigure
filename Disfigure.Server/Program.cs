#region

using System.Net;
using Serilog.Events;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static void Main()
        {
            const int port = 8898;


            using Server server = new Server(LogEventLevel.Verbose, IPAddress.IPv6Loopback, port);
            server.Start();
        }
    }
}
