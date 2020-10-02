﻿#region

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
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None)
                .Contextless();
            Connection connection = new Connection(tcpClient);
            connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.Ping:
                        await ConnectionHelper.PongAsync(connection, packet.Content).Contextless();
                        break;
                    default:
                        Log.Information(packet.ToString());
                        break;
                }
            };
            await connection.Finalize(CancellationToken.None).Contextless();
            await connection.WriteAsync(PacketType.Connect, DateTime.UtcNow, new SerializableEndPoint(IPAddress.IPv6Loopback, 8898).Serialize(),
                CancellationToken.None).Contextless();

            while (CancellationToken.None.IsCancellationRequested)
            {
                Console.ReadKey();
            }
        }
    }
}
