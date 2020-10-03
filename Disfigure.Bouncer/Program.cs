#region

using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Serilog;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            static async ValueTask ServerPacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket basicPacket)
            {
                if (basicPacket.Type != PacketType.Ping)
                {
                    return;
                }

                Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
                await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, basicPacket.Content, CancellationToken.None).ConfigureAwait(false);
            }

            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            ServerModuleConfiguration configuration = new ServerModuleConfiguration(Assembly.GetExecutingAssembly().GetName().Name, false);

            BouncerModule<BasicPacket> bouncerModule = new BouncerModule<BasicPacket>(configuration.LogLevel, new IPEndPoint(configuration.HostingIPAddress,
                configuration.HostingPort));
            bouncerModule.ServerPacketReceived += ServerPacketReceivedCallback;

            bouncerModule.AcceptConnections(BasicPacket.Factory);
            //bouncerModule.PingPongLoop();

            while (!bouncerModule.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }

}
