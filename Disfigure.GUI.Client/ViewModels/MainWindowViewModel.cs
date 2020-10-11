using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Disfigure.CLI;
using Disfigure.Collections;
using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using SharpDX.DXGI;

namespace Disfigure.GUI.Client.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ChannelBag<string> _PendingMessages;

        private Connection<Packet>? _Connection;

        public ObservableCollection<string> Messages { get; set; }

        public MainWindowViewModel()
        {
            HostModuleOption hostModuleOption = CLIParser.Parse<HostModuleOption>(new[] {"-l", "verbose", "127.0.0.1", "8998"});
            IPEndPoint ipEndPoint = new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(config => config.Console()).MinimumLevel.Is(hostModuleOption.LogLevel)
                .CreateLogger();

            _PendingMessages = new ChannelBag<string>(true, false);

            Messages = new ObservableCollection<string>();

            Task.Run(() => Start(ipEndPoint));
            Task.Run(() => MessageAdditionLoop(CancellationToken.None));
        }

        private async Task Start(IPEndPoint ipEndPoint)
        {

            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, ConnectionHelper.DefaultRetryParameters, CancellationToken.None);
            _Connection = new Connection<Packet>(tcpClient, new ECDHEncryptionProvider(), Packet.SerializerAsync, Packet.FactoryAsync);
            _Connection.Connected += Packet.SendEncryptionKeys;
            _Connection.PacketWritten += async (origin, packet) =>
            {
                await _PendingMessages.AddAsync(string.Format(FormatHelper.CONNECTION_LOGGING, _Connection.RemoteEndPoint, packet.ToString()));
            };
            _Connection.PacketReceived += async (origin, packet) =>
            {
                switch (packet.Type)
                {
                    case PacketType.EncryptionKeys:
                        _Connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.Content);
                        break;
                    case PacketType.Ping:
                        await _Connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                        break;
                }

                await _PendingMessages.AddAsync(string.Format(FormatHelper.CONNECTION_LOGGING, _Connection.RemoteEndPoint, packet.ToString()));
            };

            await _Connection.FinalizeAsync(CancellationToken.None);
        }

        private async Task MessageAdditionLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Log.Verbose("Waiting for more messages...");
                string message = await _PendingMessages.TakeAsync(true, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(message));
            }
        }
    }
}
