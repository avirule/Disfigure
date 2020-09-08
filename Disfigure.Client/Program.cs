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

            List<Packet> testPackets = new List<Packet>
            {
                new Packet(PacketType.Text, server.PublicKey, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with emoji 🍑")),
                new Packet(PacketType.Text, server.PublicKey, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with emoji2 🍑")),
                new Packet(PacketType.Text, server.PublicKey, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with emoji3 🍑")),
                new Packet(PacketType.Text, server.PublicKey, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with emoji4 🍑")),
                new Packet(PacketType.Text, server.PublicKey, DateTime.UtcNow, Encoding.Unicode.GetBytes("test message with 🍑"))
            };


            await server.WriteAsync(testPackets, CancellationToken.None);

            Console.ReadLine();
        }
    }
}
