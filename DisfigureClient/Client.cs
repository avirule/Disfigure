#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DisfigureCore;
using DisfigureCore.Net;
using Serilog;

#endregion

namespace DisfigureClient
{
    public class Client : IDisposable
    {
        private readonly Dictionary<Guid, Connection> _ServerConnections;
        private readonly Dictionary<Guid, Channel> _Channels;

        private readonly CancellationTokenSource _CancellationTokenSource;
        private readonly CancellationToken _CancellationToken;

        public IEnumerable<Connection> ServerConnections => _ServerConnections.Values;
        public IEnumerable<Channel> Channels => _Channels.Values;

        public Client()
        {
            // todo perhaps use a server descriptor or something
            _ServerConnections = new Dictionary<Guid, Connection>();
            _Channels = new Dictionary<Guid, Channel>();

            _CancellationTokenSource = new CancellationTokenSource();
            _CancellationToken = _CancellationTokenSource.Token;
        }

        public async ValueTask<Connection> EstablishConnection(IPEndPoint ipEndPoint, TimeSpan retryDelay)
        {
            const int maximum_retries = 5;

            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Information($"Attempting to establish connection to {ipEndPoint}.");

            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to establish connection to {ipEndPoint}. Retrying...");
                    await Task.Delay(retryDelay, _CancellationToken);

                    if (tries == maximum_retries)
                    {
                        Log.Error($"Failed to establish connection to {ipEndPoint}.");
                        return null!;
                    }

                    tries += 1;
                }
            }

            Guid guid = Guid.NewGuid();

            Log.Information($"Established connection to {ipEndPoint} with auto-generated GUID {guid}");

            return FinalizeConnection(guid, tcpClient);
        }

        private Connection FinalizeConnection(Guid guid, TcpClient tcpClient)
        {
            Connection connection = new Connection(guid, tcpClient);
            connection.PacketReceived += OnPacketReceived;
            _ServerConnections.Add(connection.Guid, connection);

            Log.Information($"Connection {connection.Guid} finalized.");

            connection.BeginListen(_CancellationToken, Connection.DefaultLoopDelay);
            return connection;
        }

        public static ValueTask WaitOnServerIdentityInformation(Connection connection)
        {
            ManualResetEvent manualResetEvent = new ManualResetEvent(connection.CompleteRemoteIdentity);
            ValueTask ManualReset(Connection connectionInternal, Packet packetInternal)
            {
                manualResetEvent.Set();
                return default;
            }

            connection.EndIdentityReceived += ManualReset;
            manualResetEvent.WaitOne();
            connection.EndIdentityReceived -= ManualReset;

            Log.Information("Server identity information received. Client may now operate freely.");

            return default;
        }

        #region Events

        private unsafe ValueTask OnPacketReceived(Connection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.ChannelIdentity:
                    Guid guid = new Guid(packet.Content[0..sizeof(Guid)]);
                    string name = Encoding.Unicode.GetString(packet.Content[sizeof(Guid)..]);

                    Channel channel = new Channel(guid, name);
                    _Channels.Add(channel.Guid, channel);

                    Log.Information($"Received identity information for channel: #{channel.Name} ({channel.Guid})");
                    break;
                default:
                    Log.Verbose(packet.ToString());
                    break;
            }

            return default;
        }

        #endregion

        #region Dispose

        private bool _Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (Connection connection in _ServerConnections.Values)
                {
                    connection?.Dispose();
                }
            }

            _CancellationTokenSource.Cancel();
            _Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
