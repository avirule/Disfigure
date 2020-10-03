#region

using System;
using System.Net;
using CommandLine;
using Disfigure.CLI;
using Disfigure.Diagnostics;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

                ServerModuleOption? serverModuleOption = null;
                ModuleParser.Parser.ParseArguments<ServerModuleOption>(args).WithParsed(obj =>
                    serverModuleOption = obj as ServerModuleOption);

                if (!IPAddress.TryParse(serverModuleOption!.IPAddress, out IPAddress ipAddress))
                {
                    throw new ArgumentException("Hosting IP address is in incorrect format.", nameof(serverModuleOption.IPAddress));
                }

                using ServerModule serverModule = new ServerModule(LogEventLevel.Verbose, new IPEndPoint(ipAddress, serverModuleOption.Port));
                serverModule.AcceptConnections();
                serverModule.PingPongLoop();

                while (!serverModule.CancellationToken.IsCancellationRequested)
                {
                    Console.ReadKey();
                }
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
