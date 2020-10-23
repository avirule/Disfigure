#region

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Disfigure.Net;
using Disfigure.Net.Packets;
using ReactiveUI;

#endregion


namespace Disfigure.GUI.Client.ViewModels
{
    public class ConnectionViewModel : ViewModelBase
    {
        private const string _SERVER_MESSAGE_FORMAT = "[{0}] > {1}";

        private readonly Connection<Packet> _Connection;

        private string _FriendlyName;
        private bool _IsRemoteClient;

        public string FriendlyName { get => _FriendlyName; set => this.RaiseAndSetIfChanged(ref _FriendlyName, value); }
        public ObservableCollection<ChannelViewModel> Channels { get; }
        public Guid Identity => _Connection.Identity;

        public ConnectionViewModel(Connection<Packet> connection)
        {
            _FriendlyName = string.Empty;
            _Connection = connection;
            _Connection.PacketReceived += PacketReceivedCallback;
            _Connection.PacketWritten += PacketReceivedCallback;

            Channels = new ObservableCollection<ChannelViewModel>();
            FriendlyName = connection.RemoteEndPoint.ToString() ?? "invalid address";
        }

        private ChannelViewModel GetChannel(Guid identity) => Channels.First(channel => channel.Identity.Equals(identity));

        private async ValueTask PacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            static (bool, string) ParseIdentityImpl(Packet packet)
            {
                ReadOnlySpan<byte> content = packet.ContentSpan;
                return (MemoryMarshal.Read<bool>(content), Encoding.Unicode.GetString(content.Slice(sizeof(bool))));
            }

            static unsafe (Guid, Guid, MessageViewModel) ParseTextPacketImpl(Packet packet)
            {
                ReadOnlySpan<byte> content = packet.ContentSpan;
                Guid channelID = MemoryMarshal.Read<Guid>(content);
                Guid senderID = MemoryMarshal.Read<Guid>(content.Slice(sizeof(Guid)));

                MessageViewModel messageViewModel = new MessageViewModel("FriendlyName", packet.UtcTimestamp.ToString("G"),
                    Encoding.Unicode.GetString(content.Slice(sizeof(Guid) * 2)));

                return (channelID, senderID, messageViewModel);
            }

            static unsafe (Guid, string) ParseChannelIdentityImpl(Packet packet)
            {
                ReadOnlySpan<byte> content = packet.ContentSpan;
                Guid channelID = MemoryMarshal.Read<Guid>(content);
                string channelName = Encoding.Unicode.GetString(content.Slice(sizeof(Guid)));
                return (channelID, channelName);
            }

            switch (packet.Type)
            {
                case PacketType.Identity:
                    (bool isRemoteClient, string friendlyName) = ParseIdentityImpl(packet);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _IsRemoteClient = isRemoteClient;
                        FriendlyName = friendlyName;
                    });

                    break;
                case PacketType.ChannelIdentity:
                {
                    (Guid channelID, string channelName) = ParseChannelIdentityImpl(packet);
                    await Dispatcher.UIThread.InvokeAsync(() => Channels.Add(new ChannelViewModel(channelID, channelName)));
                    break;
                }
                case PacketType.Text:
                {
                    (Guid channelID, Guid senderID, MessageViewModel messageViewModel) = ParseTextPacketImpl(packet);
                    await Dispatcher.UIThread.InvokeAsync(() => GetChannel(channelID).Messages.Add(messageViewModel));
                    break;
                }
            }
        }

        public async ValueTask WriteAsync(Packet packet, CancellationToken cancellation) => await _Connection.WriteAsync(packet, cancellation);
    }
}
