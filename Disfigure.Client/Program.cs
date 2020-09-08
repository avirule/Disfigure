#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Verbose().CreateLogger();

            Client client = new Client();
            Connection server = await client.EstablishConnection(new IPEndPoint(IPAddress.IPv6Loopback, 8898), TimeSpan.FromSeconds(0.5d));    

            await server.WriteAsync(PacketType.Text, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with emoji 🍑"), CancellationToken.None);

            Console.ReadLine();
        }
    }
}
