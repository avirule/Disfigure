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
    public class Client : Module
    {
        public Client(LogEventLevel minimumLogLevel) : base(minimumLogLevel) => ConsoleLineRead += OnConsoleLineRead;

        public async ValueTask<Connection> ConnectAsync(IPEndPoint ipEndPoint, TimeSpan retryDelay)
        {
            const int maximum_retries = 5;

            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Debug($"Connecting to {ipEndPoint}.");

            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port);
                }
                catch (SocketException) when (tries >= maximum_retries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{maximum_retries})...");

                    await Task.Delay(retryDelay, CancellationToken);
                }
            }

            Connection connection = await EstablishConnectionAsync(tcpClient, false);
            connection.ChannelIdentityReceived += OnChannelIdentityReceived;
            connection.PingReceived += OnPingReceived;
            connection.WaitForPacket(PacketType.EndIdentity);
            return connection;
        }

        public override void Start()
        {
            ReadConsoleLoop();
        }

        #region Events

        private unsafe ValueTask OnChannelIdentityReceived(Connection connection, Packet packet)
        {
            Guid guid = new Guid(packet.Content[..sizeof(Guid)]);
            string name = Encoding.Unicode.GetString(packet.Content[sizeof(Guid)..]);

            Channel channel = new Channel(guid, name);
            Channels.Add(channel.Guid, channel);

            Log.Debug($"Received identity information for channel: #{channel.Name} ({channel.Guid})");
            return default;
        }

        private async ValueTask OnPingReceived(Connection connection, Packet packet)
        {
            //await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, packet.Content, CancellationToken);
            await Task.Delay(1);
        }

        private void OnConsoleLineRead(string line)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            byte[] bytes = Encoding.Unicode.GetBytes(line);

            foreach ((Guid _, Connection connection) in Connections)
            {
                Task.Run(() => connection.WriteAsync(PacketType.Text, utcTimestamp, bytes, CancellationToken));
            }
        }

        #endregion
    }
}
