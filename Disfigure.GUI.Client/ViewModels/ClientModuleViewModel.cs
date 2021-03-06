﻿#region

using Avalonia.Threading;
using CommandLine;
using Disfigure.Collections;
using Disfigure.GUI.Client.Commands;
using Disfigure.Modules;
using Disfigure.Net.Packets;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Text;
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

        private readonly ConcurrentChannel<ConnectionViewModel> _PendingConnectionViewModels;
        private readonly ClientModule _ClientModule;

        private ConnectionViewModel? _SelectedViewModel;

        public ObservableCollection<ConnectionViewModel> ConnectionViewModels { get; }
        public ControlBoxViewModel ControlBoxViewModel { get; }

        public ConnectionViewModel? SelectedViewModel
        {
            get => _SelectedViewModel;
            set { this.RaiseAndSetIfChanged(ref _SelectedViewModel, value); }
        }

        public ClientModuleViewModel()
        {
            _PendingConnectionViewModels = new ConcurrentChannel<ConnectionViewModel>(true, false);

            ConnectionViewModels = new ObservableCollection<ConnectionViewModel>();

            _ClientModule = new ClientModule();
            _ClientModule.Connected += async connection => await _PendingConnectionViewModels.AddAsync(new ConnectionViewModel(connection));

            ControlBoxViewModel = new ControlBoxViewModel();
            ControlBoxViewModel.ContentFlushed += MessageBoxContentFlushedCallback;

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
                Task.Run(() => SelectedViewModel?.WriteAsync(new Packet(PacketType.Text, DateTime.UtcNow, Encoding.Unicode.GetBytes(content)), CancellationToken.None));
            }
        }

        private async Task AddConnectionsDispatched(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConnectionViewModel connectionViewModel = await _PendingConnectionViewModels.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => SetSelectedViewModel(connectionViewModel));
            }
        }

        private void SetSelectedViewModel(ConnectionViewModel connectionViewModel)
        {
            ConnectionViewModels.Add(connectionViewModel);

            SelectedViewModel = connectionViewModel;
        }
    }
}