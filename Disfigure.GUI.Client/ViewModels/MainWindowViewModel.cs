#region

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommandLine;
using Disfigure.Collections;
using Disfigure.GUI.Client.Commands;
using Disfigure.Modules;
using Disfigure.Net.Packets;
using Serilog;
using Serilog.Events;
using SharpDX.Text;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.GUI.Client.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private static readonly Parser _Parser = new Parser(settings =>
        {
            settings.HelpWriter = Console.Error;
            settings.CaseSensitive = false;
            settings.CaseInsensitiveEnumValues = true;
        });

        private readonly ConcurrentChannel<string> _PendingMessages;
        private readonly ClientModule _ClientModule;

        public MessageBoxViewModel MessageBoxViewModel { get; }

        public ObservableCollection<string> Messages { get; }

        public MainWindowViewModel()
        {
            _PendingMessages = new ConcurrentChannel<string>(true, false);

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
            MessageBoxViewModel = new MessageBoxViewModel();
            MessageBoxViewModel.ContentFlushed += MessageBoxContentFlushedCallback;

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

        private static readonly Type[] _CommandTypes = new[] { typeof(Connect), typeof(Exit) };

        private void MessageBoxContentFlushedCallback(object? sender, string content)
        {
            if (content.StartsWith('/'))
            {
                string[] args = content.Substring(1).Split(' ');
                object? parsed = null;
                _Parser.ParseArguments(args, _CommandTypes).WithParsed(obj => parsed = obj);

                switch (parsed)
                {
                    case Connect connect:
                        Task.Run(() => _ClientModule.ConnectAsync(new System.Net.IPEndPoint(connect.IPAddress, connect.Port)));
                        break;

                    case Exit exit:
                        Environment.Exit(-1);
                        break;
                }
            }
            else
            {
                Task.Run(() => _ClientModule.ReadOnlyConnections.Values.First().WriteAsync(new Packet(PacketType.Text, DateTime.UtcNow, Encoding.Unicode.GetBytes(content)), CancellationToken.None));
            }
        }
    }
}