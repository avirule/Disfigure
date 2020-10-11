using Avalonia.Threading;
using Disfigure.Collections;
using Disfigure.Net;
using Disfigure.Net.Packets;
using ReactiveUI;
using SharpDX.Text;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Disfigure.GUI.Client.ViewModels
{
    public class ConnectionViewModel : ViewModelBase
    {
        private readonly ConcurrentChannel<string> _PendingMessages;

        private string _FriendlyName;

        private Connection<Packet> Connection { get; set; }

        public string FriendlyName
        {
            get => _FriendlyName;
            set { this.RaiseAndSetIfChanged(ref _FriendlyName, value); }
        }

        public Guid Identity => Connection.Identity;

        public ObservableCollection<string> Messages { get; }

        public ConnectionViewModel(Connection<Packet> connection)
        {
            _PendingMessages = new ConcurrentChannel<string>(true, false);
            _FriendlyName = string.Empty;

            Messages = new ObservableCollection<string>();

            Connection = connection;
            Connection.PacketReceived += async (connection, packet) => await _PendingMessages.AddAsync($"INC {packet}");
            Connection.PacketWritten += async (connection, packet) => await _PendingMessages.AddAsync($"OUT {packet}");
            connection.PacketReceived += TryAssignIdentity;

            FriendlyName = connection.RemoteEndPoint.ToString() ?? "invalid address";

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

        private async Task TryAssignIdentity(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Identity:
                    string deserialized = Encoding.Unicode.GetString(packet.ContentSpan);
                    await Dispatcher.UIThread.InvokeAsync(() => FriendlyName = deserialized);
                    break;
            }
        }
    }
}