#region

using Serilog;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.CLI.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Host hostModuleOption = CLIParser.Parse<Host>(args);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(hostModuleOption.LogLevel)
                .CreateLogger();

            Modules.ClientModule clientModule = new Modules.ClientModule();
            IPEndPoint ipEndPoint = new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port);
            clientModule.PacketWritten += (connection, packet) =>
            {
                Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                return default;
            };
            clientModule.PacketReceived += (connection, packet) =>
            {
                Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString()));
                return default;
            };

            await clientModule.ConnectAsync(ipEndPoint);

            new AutoResetEvent(false).WaitOne();
        }
    }
}