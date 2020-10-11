#region

using Avalonia.Threading;
using CommandLine;
using Disfigure.Collections;
using Disfigure.GUI.Client.Commands;
using Disfigure.Modules;
using Disfigure.Net;
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
    public class ClientModuleViewModel : ViewModelBase
    {
        private static readonly Type[] _CommandTypes = new[] { typeof(Connect), typeof(Exit) };

        private static readonly Parser _Parser = new Parser(settings =>
        {
            settings.HelpWriter = Console.Error;
            settings.CaseSensitive = false;
            settings.CaseInsensitiveEnumValues = true;
        });

        private readonly ConcurrentChannel<Connection<Packet>> _PendingConnectionModels;
        private readonly ClientModule _ClientModule;

        public ControlBoxViewModel MessageBoxViewModel { get; }

        public ObservableCollection<ConnectionViewModel> ConnectionViewModels { get; }

        public ClientModuleViewModel()
        {
            _PendingConnectionModels = new ConcurrentChannel<Connection<Packet>>(true, false);

            ConnectionViewModels = new ObservableCollection<ConnectionViewModel>();

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(LogEventLevel.Verbose)
                .CreateLogger();

            _ClientModule = new ClientModule();
            _ClientModule.Connected += async connection => await _PendingConnectionModels.AddAsync(connection);

            MessageBoxViewModel = new ControlBoxViewModel();
            MessageBoxViewModel.ContentFlushed += MessageBoxContentFlushedCallback;

            Task.Run(() => AddConnectionsDispatched(CancellationToken.None));
        }

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

        private async Task AddConnectionsDispatched(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Connection<Packet> connection = await _PendingConnectionModels.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => ConnectionViewModels.Add(new ConnectionViewModel(connection)));
            }
        }
    }
}