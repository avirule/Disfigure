#region

using System;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
using ReactiveUI;

#endregion

namespace Disfigure.GUI.Client.ViewModels
{
    public class MessageBoxViewModel : ViewModelBase
    {
        private readonly ClientModule _ClientModule;

        private string _MessageBoxContent;

        public string MessageBoxContent
        {
            get => _MessageBoxContent;
            set => this.RaiseAndSetIfChanged(ref _MessageBoxContent, value);
        }

        public ReactiveCommand<Unit, Unit> SendMessageBoxContent { get; }

        public MessageBoxViewModel(ClientModule clientModule)
        {
            _ClientModule = clientModule;

            _MessageBoxContent = string.Empty;

            SendMessageBoxContent = ReactiveCommand.CreateFromTask(SendMessageContentsAsync);
        }

        private async Task SendMessageContentsAsync()
        {
            Connection<Packet>? connection = _ClientModule.ReadOnlyConnections.Values.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(MessageBoxContent) || connection is null)
            {
                return;
            }

            ReadOnlyMemory<byte> content = Encoding.Unicode.GetBytes(MessageBoxContent);

            await connection.WriteAsync(new Packet(PacketType.Text, DateTime.UtcNow, content.Span), CancellationToken.None);

            MessageBoxContent = string.Empty;
        }
    }
}
