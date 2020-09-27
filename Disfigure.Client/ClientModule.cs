#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Client
{
    public class ClientModule : Module
    {
        public ClientModule(LogEventLevel minimumLogLevel) : base(minimumLogLevel) { }

        #region Connection

        public async ValueTask ConnectAsync(IPEndPoint ipEndPoint, int maximumRetries, TimeSpan retryDelay)
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
            connection.PacketReceived += OnPacketReceivedCallback;
            connection.WaitForPacket(PacketType.EndIdentity);
        }

        #endregion

        #region Events

        protected override async ValueTask OnPacketReceivedCallback(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.ChannelIdentity:
                    unsafe
                    {
                        Guid guid = new Guid(packet.Content[..sizeof(Guid)]);
                        string name = Encoding.Unicode.GetString(packet.Content[sizeof(Guid)..]);

                        Channel channel = new Channel(guid, name);
                        Channels.TryAdd(channel.Guid, channel);

                        Log.Debug($"Received identity information for channel: #{channel.Name} ({channel.Guid})");
                    }

                    break;
                case PacketType.Ping:
                    Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                    await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken).Contextless();
                    break;
            }

            await base.OnPacketReceivedCallback(connection, packet).Contextless();
        }

        #endregion
    }
}
