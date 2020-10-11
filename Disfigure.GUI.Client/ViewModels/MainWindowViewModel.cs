#region

using System.Collections.ObjectModel;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Disfigure.CLI;
using Disfigure.Collections;
using Disfigure.Modules;
using Serilog;

#endregion

namespace Disfigure.GUI.Client.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ChannelBag<string> _PendingMessages;

        private ClientModule _ClientModule;

        public ObservableCollection<string> Messages { get; set; }

        public MainWindowViewModel()
        {
            _PendingMessages = new ChannelBag<string>(true, false);
            Messages = new ObservableCollection<string>();

            HostModuleOption hostModuleOption = CLIParser.Parse<HostModuleOption>(new[]
            {
                "-l",
                "verbose",
                "127.0.0.1",
                "8998"
            });

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(hostModuleOption.LogLevel)
                .CreateLogger();

            IPEndPoint ipEndPoint = new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port);

            _ClientModule = new ClientModule();
            _ClientModule.PacketWritten += async (connection, packet) =>
                await _PendingMessages.AddAsync(
                    $"OUT {string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString())}");
            _ClientModule.PacketReceived += async (connection, packet) =>
                await _PendingMessages.AddAsync(
                    $"INC {string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString())}");


            Task.Run(() => _ClientModule.ConnectAsync(ipEndPoint));
            Task.Run(() => MessageAdditionLoop(CancellationToken.None));
        }

        private async Task MessageAdditionLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string message = await _PendingMessages.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(message));
            }
        }
    }
}
