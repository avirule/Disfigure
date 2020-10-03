#region

using System;
using System.Net;
using CommandLine;
using Disfigure.CLI;
using Disfigure.Diagnostics;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            ServerModuleOption? serverModuleOption = null;
            ModuleParser.Parser.ParseArguments<ServerModuleOption>(args).WithParsed(obj =>
                serverModuleOption = obj as ServerModuleOption);

            if (!IPAddress.TryParse(serverModuleOption!.IPAddress, out IPAddress ipAddress))
            {
                throw new ArgumentException("Hosting IP address is in incorrect format.", nameof(serverModuleOption.IPAddress));
            }

            BouncerModule bouncerModule = new BouncerModule(LogEventLevel.Verbose, new IPEndPoint(ipAddress, serverModuleOption.Port));
            bouncerModule.AcceptConnections();
            bouncerModule.PingPongLoop();

            while (!bouncerModule.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }
}
