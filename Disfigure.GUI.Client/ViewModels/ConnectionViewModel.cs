using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Disfigure.Collections;
using Disfigure.Net;
using Disfigure.Net.Packets;
using ReactiveUI;

namespace Disfigure.GUI.Client.ViewModels
{
    public class ConnectionViewModel : ViewModelBase
    {
        private const string _SERVER_MESSAGE_FORMAT = "[{0}] > {1}";

        private readonly Connection<Packet> _Connection;
        private readonly ConcurrentChannel<Packet> _PendingMessages;
        private string _FriendlyName;

        private bool _IsRemoteClient;
        private int _SelectedMessageIndex;

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

        public string FriendlyName { get => _FriendlyName; set => this.RaiseAndSetIfChanged(ref _FriendlyName, value); }

        public int SelectedMessageIndex { get => _SelectedMessageIndex; set => this.RaiseAndSetIfChanged(ref _SelectedMessageIndex, value); }

        public Guid Identity => _Connection.Identity;

        public ObservableCollection<MessageViewModel> Messages { get; }

        private async Task AddMessagesDispatched(CancellationToken cancellationToken)
        {
            void AddMessageAndScroll(MessageViewModel messageViewModel)
            {
                Messages.Add(messageViewModel);
                SelectedMessageIndex = Messages.Count - 1;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                Packet packet = await _PendingMessages.TakeAsync(true, cancellationToken);

                string packetContent = Encoding.Unicode.GetString(packet.ContentSpan);
                string messageContent = _IsRemoteClient ? packetContent : string.Format(_SERVER_MESSAGE_FORMAT, packet.Type, packetContent);

                await Dispatcher.UIThread.InvokeAsync(() =>
                    AddMessageAndScroll(new MessageViewModel(FriendlyName, packet.UtcTimestamp.ToString("G"), messageContent)));
            }
        }

        private async ValueTask TryAssignIdentity(Connection<Packet> connection, Packet packet)
        {
            static (bool, string) ParseIdentityContent(ReadOnlySpan<byte> content)
            {
                bool isClient = MemoryMarshal.Read<bool>(content);
                string friendlyName = Encoding.Unicode.GetString(content.Slice(sizeof(bool)));

                return (isClient, friendlyName);
            }

            switch (packet.Type)
            {
                case PacketType.Identity:
                    (bool isClient, string friendlyName) = ParseIdentityContent(packet.ContentSpan);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _IsRemoteClient = isClient;
                        FriendlyName = friendlyName;
                    });

                    break;
            }
        }

        public async ValueTask WriteAsync(Packet packet, CancellationToken cancellation) => await _Connection.WriteAsync(packet, cancellation);
    }
}
