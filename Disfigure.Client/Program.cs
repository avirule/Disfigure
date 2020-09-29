#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(LogEventLevel.Verbose).CreateLogger();

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.IPv6Loopback, 8899);
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, 5, TimeSpan.FromMilliseconds(500d),
                CancellationToken.None).Contextless();
            Connection connection = new Connection(tcpClient);
            connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.Ping:
                        Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                        await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken.None).Contextless();
                        break;
                    default:
                        Log.Information(packet.ToString());
                        break;
                }
            };
            await connection.Finalize(CancellationToken.None);
            await connection.WriteAsync(PacketType.Connect, DateTime.UtcNow,
                new BinaryEndPoint(new IPEndPoint(IPAddress.IPv6Loopback, 8898)).Data.ToArray(), CancellationToken.None).Contextless();

            while (true)
            {
                Console.ReadKey();
            }
        }
    }
}
