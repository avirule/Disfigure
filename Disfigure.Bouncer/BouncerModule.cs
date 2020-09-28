#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Client;
using Disfigure.Net;
using Disfigure.Server;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class BouncerModule : ServerModule
    {
        public BouncerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel, hostAddress)
        {
            PacketReceived += OnPacketReceivedCallback;

            AcceptConnections();
            PingPongLoop();
        }

        private async ValueTask ConnectAsync(IPEndPoint ipEndPoint, int maximumRetries, TimeSpan retryDelay)
        {
            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Debug($"Connecting to {ipEndPoint}.");

            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).Contextless();
                }
                catch (SocketException) when (tries >= maximumRetries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{maximumRetries})...");

                    await Task.Delay(retryDelay, CancellationToken).Contextless();
                }
            }

            Connection connection = await EstablishConnectionAsync(tcpClient).Contextless();
            connection.PacketReceived += OnServerPacketReceivedCallback;
            connection.WaitForPacket(PacketType.EndIdentity);
        }

        #region PacketReceived Events

        private async ValueTask OnServerPacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Connect:
                    
                    break;
                case PacketType.Disconnect:
                    break;
            }
        }

        private async ValueTask OnClientPacketReceivedCallback(Connection connection, Packet packet)
        {

        }

        private async ValueTask OnPacketReceivedCallback(Connection connection, Packet packet)
        {
            if (packet.Type == PacketType.Ping)
            {
                Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken).Contextless();
            }
        }

        #endregion
    }
}
