#region

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.CLI.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            HostModuleOption hostModuleOption = CLIParser.Parse<HostModuleOption>(args);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(hostModuleOption.LogLevel)
                .CreateLogger();

            Modules.ClientModule clientModule = new Modules.ClientModule();
            IPEndPoint ipEndPoint = new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port);
            clientModule.PacketWritten += (connection, packet) =>
            {
                Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                return Task.CompletedTask;
            };
            clientModule.PacketReceived += (connection, packet) =>
            {
                Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                return Task.CompletedTask;
            };

            await clientModule.ConnectAsync(ipEndPoint);

            new AutoResetEvent(false).WaitOne();
        }
    }
}
