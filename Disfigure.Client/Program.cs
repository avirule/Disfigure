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

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Loopback, 8899);
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None)
                .ConfigureAwait(false);
            Connection<BasicPacket> connection = new Connection<BasicPacket>(tcpClient, BasicPacket.Factory);
            connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.Ping:
                        Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                        await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken.None).ConfigureAwait(false);
                        break;
                    default:
                        Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                        break;
                }
            };
            await connection.Finalize(CancellationToken.None).ConfigureAwait(false);
            await connection.WriteAsync(PacketType.Connect, DateTime.UtcNow, new SerializableEndPoint(IPAddress.Loopback, 8898).Serialize(),
                CancellationToken.None).ConfigureAwait(false);

            while (true)
            {
                Console.ReadKey();
            }
        }
    }
}
