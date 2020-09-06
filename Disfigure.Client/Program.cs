#region

using System;
using System.Collections.Generic;
using System.Linq;
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

            await Client.WaitOnCompleteIdentity(server);

            Channel channel = client.Channels.First();
            List<Packet> testPackets = new List<Packet>
            {
                Packet.BuildMMSPacket(channel, DateTime.UtcNow, PacketType.Text, Encoding.Unicode.GetBytes("test message with emoji 🍑")),
                Packet.BuildMMSPacket(channel, DateTime.UtcNow, PacketType.Text, Encoding.Unicode.GetBytes("test message with emoji2 🍑")),
                Packet.BuildMMSPacket(channel, DateTime.UtcNow, PacketType.Text, Encoding.Unicode.GetBytes("test message with emoji3 🍑")),
                Packet.BuildMMSPacket(channel, DateTime.UtcNow, PacketType.Text, Encoding.Unicode.GetBytes("test message with emoji4 🍑")),
                Packet.BuildMMSPacket(channel, DateTime.UtcNow, PacketType.Text, Encoding.Unicode.GetBytes("test message with 🍑"))
            };

            await client.ServerConnections.First().WriteAsync(CancellationToken.None, testPackets);

            Console.ReadLine();
        }
    }
}
