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
        private readonly ConcurrentChannel<Packet> _PendingMessages;

        private Connection<Packet> _Connection;
        private string _FriendlyName;

        public string FriendlyName
        {
            get => _FriendlyName;
            set { this.RaiseAndSetIfChanged(ref _FriendlyName, value); }
        }

        public Guid Identity => _Connection.Identity;

        public ObservableCollection<MessageViewModel> Messages { get; }

        public ConnectionViewModel(Connection<Packet> connection)
        {
            _PendingMessages = new ConcurrentChannel<Packet>(true, false);
            _FriendlyName = string.Empty;

            Messages = new ObservableCollection<MessageViewModel>();
            _Connection = connection;
            connection.PacketReceived += TryAssignIdentity;
            _Connection.PacketReceived += async (connection, packet) => await _PendingMessages.AddAsync(packet);
            _Connection.PacketWritten += async (connection, packet) => await _PendingMessages.AddAsync(packet);

            FriendlyName = connection.RemoteEndPoint.ToString() ?? "invalid address";

            Task.Run(() => AddMessagesDispatched(CancellationToken.None));
        }

        private async Task AddMessagesDispatched(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Packet packet = await _PendingMessages.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(new MessageViewModel(packet.Type.ToString(), packet.UtcTimestamp.ToString("G"), Encoding.Unicode.GetString(packet.ContentSpan))));
            }
        }

        private async ValueTask TryAssignIdentity(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Identity:
                    string deserialized = Encoding.Unicode.GetString(packet.ContentSpan);
                    await Dispatcher.UIThread.InvokeAsync(() => FriendlyName = deserialized);
                    break;
            }
        }

        public async ValueTask WriteAsync(Packet packet, CancellationToken cancellation) => await _Connection.WriteAsync(packet, cancellation);
    }
}