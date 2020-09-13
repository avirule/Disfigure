#region

using System;
using System.Net;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main()
        {
            try
            {
                using Client client = new Client(LogEventLevel.Verbose);
                await client.ConnectAsync(new IPEndPoint(IPAddress.IPv6Loopback, 8898), TimeSpan.FromSeconds(0.5d));
                client.Start();
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
