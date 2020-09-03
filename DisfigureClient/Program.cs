#region

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DisfigureCore;
using DisfigureCore.Net;
using Serilog;

#endregion

namespace DisfigureClient
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

            Client client = new Client();
            await client.EstablishConnection(new IPEndPoint(IPAddress.IPv6Loopback, 8898), TimeSpan.FromSeconds(0.5d));

            Guid channel = Guid.NewGuid();
            Packet packet = new Packet(DateTime.UtcNow, PacketType.Text, channel, Encoding.Unicode.GetBytes("test message with emoji 🍑"));
            Packet packet2 = new Packet(DateTime.UtcNow, PacketType.Text, channel, Encoding.Unicode.GetBytes("test message with emoji2 🍑"));
            Packet packet3 = new Packet(DateTime.UtcNow, PacketType.Text, channel, Encoding.Unicode.GetBytes("test message with emoji3 🍑"));
            Packet packet4 = new Packet(DateTime.UtcNow, PacketType.Text, channel, Encoding.Unicode.GetBytes("test message with emoji4 🍑"));
            Packet packet5 = new Packet(DateTime.UtcNow, PacketType.Text, channel, Encoding.Unicode.GetBytes("test message with 🍑"));
            await client.Connections.FirstOrDefault().Value.WriteAsync(CancellationToken.None, packet, packet2, packet3, packet4, packet5);

            Console.ReadLine();
        }
    }
}
