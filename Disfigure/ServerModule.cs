#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure
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

                Log.Information($"{GetType().FullName} now listening on {_HostAddress}.");

                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync().Contextless();
                    Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                    Connection connection = await ConnectionHelper.EstablishConnectionAsync(tcpClient, CancellationToken).Contextless();

                    if (!await RegisterConnection(connection))
                    {
                        Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Connection with given identity already exists."));

                        connection.Dispose();
                    }
                }
            }
            catch (SocketException exception) when (exception.ErrorCode == 10048)
            {
                Log.Fatal($"Port {_HostAddress.Port} is already being listened on.");
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

        protected override async ValueTask<bool> RegisterConnection(Connection connection)
        {
            connection.PacketReceived += HandlePongPacketsCallback;

            return await base.RegisterConnection(connection);
        }

        public void PingPongLoop() => Task.Run(PingPongLoopInternal);

        private async Task PingPongLoopInternal()
        {
            Stack<Connection> abandonedConnections = new Stack<Connection>();

            while (!CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_PingInterval).Contextless();

                foreach ((Guid connectionIdentity, Connection connection) in Connections)
                {
                    if (_PendingPings.ContainsKey(connectionIdentity))
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Pending ping timed out. Queueing force disconnect."));
                        abandonedConnections.Push(connection);
                    }
                    else
                    {
                        PendingPing pendingPing = AllocateNewPing();
                        await connection.WriteAsync(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray(), CancellationToken)
                            .Contextless();
                    }
                }

                while (abandonedConnections.TryPop(out Connection? connection))
                {
                    ForceDisconnect(connection);
                }
            }
        }

        private PendingPing AllocateNewPing()
        {
            PendingPing pendingPing = new PendingPing();
            _PendingPings.TryAdd(pendingPing.Identity, pendingPing);
            return pendingPing;
        }

        #endregion


        #region Handshakes

        protected override async ValueTask ShareIdentityAsync(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).Contextless();

            await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).Contextless();
        }

        #endregion


        #region Events

        protected override ValueTask DisconnectedCallback(Connection connection)
        {
            base.DisconnectedCallback(connection);

            _PendingPings.TryRemove(connection.Identity, out _);

            return default;
        }

        private ValueTask HandlePongPacketsCallback(Connection connection, Packet packet)
        {
            if (packet.Type != PacketType.Pong)
            {
                return default;
            }

            if (!_PendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
                return default;
            }
            else if (packet.Content.Length != 16)
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
    }
}
