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
        private readonly TcpListener _Listener;

        public Server(LogEventLevel logEventLevel, IPAddress ip, int port) : base(logEventLevel) => _Listener = new TcpListener(ip, port);

        public void Start()
        {
            _Listener.Start();

            Task.Run(AcceptConnections);

            ReadConsoleLoop();
        }

        private async ValueTask AcceptConnections()
        {
            async ValueTask CommunicateServerIdentities(Connection connection)
            {
                DateTime utcTimestamp = DateTime.UtcNow;
                await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);

                await connection.WriteAsync(Channels.Values.Select(channel => (PacketType.ChannelIdentity, utcTimestamp, channel.Serialize())),
                    CancellationToken);

                await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);
            }

            try
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync();
                    Log.Information($"Accepted new connection from {tcpClient.Client.RemoteEndPoint}.");

                    Connection connection = await EstablishConnectionAsync(tcpClient, true);
                    connection.TextPacketReceived += OnTextPacketReceived;

                    await CommunicateServerIdentities(connection);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        private void ReadConsoleLoop()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                string? command = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(command)) { }
                else if (command.Equals("avg"))
                {
                    PacketDiagnosticGroup packetDiagnosticGroup = DiagnosticsProvider.GetGroup<PacketDiagnosticGroup>();
                    double avgConstruction = packetDiagnosticGroup.ConstructionTimes.Average(time => ((TimeSpan)time).TotalMilliseconds);
                    double avgDecryption = packetDiagnosticGroup.DecryptionTimes.Average(time => ((TimeSpan)time).TotalMilliseconds);
                    Log.Information($"Construction: {avgConstruction:0.00}ms");
                    Log.Information($"Decryption: {avgDecryption:0.00}ms");
                }
            }
        }

        #region Events

        private async ValueTask OnTextPacketReceived(Connection connection, Packet packet)
        {
            await connection.WriteAsync(packet.Type, DateTime.UtcNow, packet.Content, CancellationToken);
        }

        #endregion
    }
}
