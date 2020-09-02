#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DisfigureCore;
using Serilog;

#endregion

namespace DisfigureServer
{
    public class Server
    {
        private readonly TcpListener _Listener;
        private readonly Dictionary<Guid, Connection> _Connections;

        private readonly Channel<Message> _Messages;
        private readonly ChannelWriter<Message> _Writer;
        private readonly ChannelReader<Message> _Reader;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;


        public Server()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

            const int port = 8898;
            IPAddress local = IPAddress.IPv6Loopback;
            _Listener = new TcpListener(local, port);
            _Connections = new Dictionary<Guid, Connection>();

            _Messages = Channel.CreateUnbounded<Message>();
            _Reader = _Messages.Reader;
            _Writer = _Messages.Writer;

            _CancellationTokenSource = new CancellationTokenSource();
            _CancellationToken = _CancellationTokenSource.Token;
        }

        public async Task Start()
        {
            try
            {
                _Listener.Start();

                while (!_CancellationToken.IsCancellationRequested)
                {
                    await AcceptPendingConnections();
                }
            }
            catch (Exception ex) { }
        }

        private async ValueTask AcceptPendingConnections()
        {
            while (_Listener.Pending())
            {
                Guid guid = Guid.NewGuid();
                TcpClient client = await _Listener.AcceptTcpClientAsync();
                Connection connection = new Connection(guid, client);
                connection.MessageReceived += OnMessageReceived;
                _Connections.Add(guid, connection);

                await Task.Run(() => connection.BeginListenAsync(_CancellationToken), _CancellationToken);
            }
        }

        private static ValueTask OnMessageReceived(Connection connection, Message message)
        {
            Log.Information(message.ToString());
            return default;
        }
    }
}
