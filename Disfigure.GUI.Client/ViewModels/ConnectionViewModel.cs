using Avalonia.Threading;
using Disfigure.Collections;
using Disfigure.Net;
using Disfigure.Net.Packets;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Disfigure.GUI.Client.ViewModels
{
    public class ConnectionViewModel : ViewModelBase
    {
        private readonly ConcurrentChannel<string> _PendingMessages;

        private Connection<Packet> Connection { get; set; }

        public string Name => Connection?.RemoteEndPoint?.ToString() ?? "invalid address";
        public Guid Identity => Connection.Identity;

        public ObservableCollection<string> Messages { get; }

        public ConnectionViewModel(Connection<Packet> connection)
        {
            _PendingMessages = new ConcurrentChannel<string>(true, false);

            Messages = new ObservableCollection<string>();

            Connection = connection;
            Connection.PacketReceived += async (connection, packet) => await _PendingMessages.AddAsync($"INC {packet}");
            Connection.PacketWritten += async (connection, packet) => await _PendingMessages.AddAsync($"OUT {packet}");

            Task.Run(() => AddMessagesDispatched(CancellationToken.None));
        }

        private async Task AddMessagesDispatched(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string message = await _PendingMessages.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(message));
            }
        }
    }
}