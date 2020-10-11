#region

using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Disfigure.Collections;
using Disfigure.Modules;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.GUI.Client.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ChannelBag<string> _PendingMessages;
        private readonly ClientModule _ClientModule;

        public MessageBoxViewModel MessageBoxViewModel { get; }

        public ObservableCollection<string> Messages { get; }

        public MainWindowViewModel()
        {
            _PendingMessages = new ChannelBag<string>(true, false);

            MessageBoxViewModel = new MessageBoxViewModel(_ClientModule);
            Messages = new ObservableCollection<string>();

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(LogEventLevel.Verbose)
                .CreateLogger();

            _ClientModule = new ClientModule();
            _ClientModule.PacketWritten += async (connection, packet) =>
                await _PendingMessages.AddAsync(
                    $"OUT {string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString())}");
            _ClientModule.PacketReceived += async (connection, packet) =>
                await _PendingMessages.AddAsync(
                    $"INC {string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, packet.ToString())}");

            Task.Run(() => DispatchAddMessages(CancellationToken.None));
        }

        private async Task DispatchAddMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string message = await _PendingMessages.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(message));
            }
        }
    }
}
