#region

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Server
{
    public class Server : Module
    {
        private readonly IPAddress _HostAddress;
        private readonly int _HostPort;

        public Server(LogEventLevel logEventLevel, IPAddress ip, int port) : base(logEventLevel)
        {
            _HostAddress = ip;
            _HostPort = port;
        }

        public override void Start()
        {
            ConsoleLineRead += OnConsoleLineRead;

            Task.Run(AcceptConnections);

            ReadConsoleLoop();
        }

        private async ValueTask AcceptConnections()
        {
            async ValueTask CommunicateServerIdentities(Connection connection)
            {
                DateTime utcTimestamp = DateTime.UtcNow;
                await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);

                if (Channels.Count > 0)
                {
                    await connection.WriteAsync(Channels.Values.Select(channel => (PacketType.ChannelIdentity, utcTimestamp, channel.Serialize())),
                        CancellationToken);
                }

                await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);
            }

            try
            {
                TcpListener listener = new TcpListener(_HostAddress, _HostPort);
                listener.Start();

                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync();
                    Log.Information($"Accepted new connection from {tcpClient.Client.RemoteEndPoint}.");

                    Connection connection = await EstablishConnectionAsync(tcpClient, true);
                    connection.PacketReceived += OnPacketReceived;

                    await CommunicateServerIdentities(connection);
                }
            }
            catch (SocketException exception) when (exception.ErrorCode == 10048)
            {
                Log.Fatal("Another instance of Disfigure.Server is already running.");
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
            finally
            {
                CancellationTokenSource.Cancel();
            }
        }

        private static void OnConsoleLineRead(string command)
        {
            switch (command)
            {
                case "avg":
                    PacketDiagnosticGroup? packetDiagnosticGroup = DiagnosticsProvider.GetGroup<PacketDiagnosticGroup>();

                    if (packetDiagnosticGroup is { })
                    {
                        double avgConstruction = packetDiagnosticGroup.ConstructionTimes.Average(time => ((TimeSpan)time).TotalMilliseconds);
                        double avgDecryption = packetDiagnosticGroup.DecryptionTimes.Average(time => ((TimeSpan)time).TotalMilliseconds);
                        Log.Information($"Construction: {avgConstruction:0.00}ms");
                        Log.Information($"Decryption: {avgDecryption:0.00}ms");
                    }

                    break;
            }
        }

        #region Events

        private async ValueTask OnPacketReceived(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Text:
                case PacketType.Media:
                    foreach (Connection conn in Connections.Where(conn => !conn.Equals(connection)))
                    {
                        await conn.WriteAsync(packet.Type, packet.UtcTimestamp, packet.Content, CancellationToken);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion
    }
}
