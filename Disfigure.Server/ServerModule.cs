#region

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Server
{
    public class ServerModule : Module
    {
        private static readonly TimeSpan _PingInterval = TimeSpan.FromSeconds(5d);

        private readonly IPEndPoint _HostAddress;
        private readonly ConcurrentDictionary<Guid, PendingPing> _PendingPings;

        public ServerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel)
        {
            _HostAddress = hostAddress;
            _PendingPings = new ConcurrentDictionary<Guid, PendingPing>();
        }


        #region Runtime

        public void AcceptConnections() => Task.Run(AcceptConnectionsInternal);

        private async ValueTask AcceptConnectionsInternal()
        {
            try
            {
                TcpListener listener = new TcpListener(_HostAddress);
                listener.Start();

                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync();
                    Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                    Connection connection = await EstablishConnectionAsync(tcpClient);
                    connection.Disconnected += OnDisconnected;
                    connection.PacketReceived += OnPacketReceived;

                    await CommunicateServerIdentities(connection);
                }
            }
            catch (SocketException exception) when (exception.ErrorCode == 10048)
            {
                Log.Fatal($"Provided port is already being listened on (port {_HostAddress.Port}).");
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
            finally
            {
                CancellationTokenSource.Cancel();
            }
        }

        public void PingPongLoop() => Task.Run(PingPongLoopInternal);

        private async Task PingPongLoopInternal()
        {
            Stopwatch pingIntervalTimer = Stopwatch.StartNew();
            Stopwatch pingFrameTimer = new Stopwatch();

            while (!CancellationToken.IsCancellationRequested)
            {
                pingFrameTimer.Restart();

                await Task.Delay(100);

                if (pingIntervalTimer.Elapsed < _PingInterval)
                {
                    continue;
                }

                foreach ((Guid connectionIdentity, Connection connection) in Connections)
                {
                    if (_PendingPings.ContainsKey(connectionIdentity))
                    {
                        Log.Warning($"<{connection.RemoteEndPoint}> Pending ping timed out. Forcibly disconnecting.");
                        connection.Dispose();
                    }
                    else
                    {
                        PendingPing pendingPing = new PendingPing();
                        _PendingPings.TryAdd(connectionIdentity, pendingPing);
                        await connection.WriteAsync(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray(), CancellationToken);
                    }
                }

                pingIntervalTimer.Restart();
            }
        }

        #endregion


        #region Handshakes

        private async ValueTask CommunicateServerIdentities(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);

            if (Channels.Count > 0)
            {
                await connection.WriteAsync(Channels.Values.Select(channel => (PacketType.ChannelIdentity, utcTimestamp, channel.Serialize())),
                    CancellationToken);
            }

            await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken);
        }

        #endregion


        #region Events

        private ValueTask OnDisconnected(Connection connection)
        {
            Connections.TryRemove(connection.Identity, out _);
            _PendingPings.TryRemove(connection.Identity, out _);

            return default;
        }

        protected override async ValueTask OnPacketReceived(Connection connection, Packet packet)
        {
            // handle pingspongs
            if (packet.Type == PacketType.Pong)
            {
                PongReceived(connection, packet);
            }

            await base.OnPacketReceived(connection, packet);
        }

        private void PongReceived(Connection connection, Packet packet)
        {
            if (!_PendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
                return;
            }
            else if (packet.Content.Length != 16)
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
                return;
            }

            Guid pingIdentity = new Guid(packet.Content);
            if (pendingPing.Identity != pingIdentity)
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but ping identity didn't match.");
                return;
            }

            _PendingPings.TryRemove(connection.Identity, out _);
        }

        #endregion
    }
}
