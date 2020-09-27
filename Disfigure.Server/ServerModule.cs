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

        public async ValueTask AcceptConnections()
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
                    connection.PongReceived += OnPongReceived;

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

        public async Task PingPongLoop()
        {
            Stopwatch pingIntervalTimer = Stopwatch.StartNew();
            Stopwatch pingFrameTimer = new Stopwatch();

            while (!CancellationToken.IsCancellationRequested)
            {
                pingFrameTimer.Restart();

                await Task.Delay(100);

                if (pingIntervalTimer.Elapsed >= _PingInterval)
                {
                    foreach ((Guid connectionIdentity, Connection connection) in Connections)
                    {
                        if (_PendingPings.ContainsKey(connectionIdentity))
                        {
                            continue;
                        }

                        PendingPing pendingPing = new PendingPing();
                        _PendingPings.TryAdd(connectionIdentity, pendingPing);
                        await connection.WriteAsync(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray(), CancellationToken);
                    }

                    pingIntervalTimer.Restart();
                }

                TimeoutExpiredPings(pingFrameTimer);
            }
        }

        private void TimeoutExpiredPings(Stopwatch pingFrameTimer)
        {
            foreach ((Guid connectionIdentity, PendingPing pendingPing) in _PendingPings)
            {
                pendingPing.PingLifetime += pingFrameTimer.Elapsed;

                if (pendingPing.PingLifetime < _PingInterval)
                {
                    continue;
                }

                if (Connections.TryGetValue(connectionIdentity, out Connection? connection))
                {
                    Log.Warning($"<{connection.RemoteEndPoint}> Pending ping timed out. Forcibly disconnecting.");
                    connection.Dispose();
                }
                else
                {
                    Log.Error($"Forgetting pending ping (identity {pendingPing.Identity}) because related connection does not exist.");
                    _PendingPings.TryRemove(connectionIdentity, out _);
                }
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

        private ValueTask OnPongReceived(Connection connection, Packet packet)
        {
            if (!_PendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
                return default;
            }

            if (packet.Content.Length != 16)
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
                return default;
            }

            Guid pingIdentity = new Guid(packet.Content);
            if (pendingPing.Identity != pingIdentity)
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but ping identity didn't match.");
                return default;
            }

            _PendingPings.TryRemove(connection.Identity, out _);

            return default;
        }

        #endregion


        #region Callback Events

        public event PacketEventHandler? PacketReceived;

        #endregion
    }
}
