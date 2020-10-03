#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Modules
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

        /// <summary>
        ///     Begins accepting network connections.
        /// </summary>
        /// <remarks>
        ///     This is run on the ThreadPool.
        /// </remarks>
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
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                    Connection connection = await ConnectionHelper.EstablishConnectionAsync(tcpClient, CancellationToken).ConfigureAwait(false);

                    if (!await RegisterConnection(connection).ConfigureAwait(false))
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
            catch (IOException exception) when (exception.InnerException is SocketException)
            {
                Log.Fatal("Remote host forcibly closed connection while connecting.");
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

        /// <inheritdoc />
        protected override async ValueTask<bool> RegisterConnection(Connection connection)
        {
            connection.PacketReceived += HandlePongPacketsCallback;

            return await base.RegisterConnection(connection).ConfigureAwait(false);
        }

        /// <summary>
        ///     Begins the Pong-Pong loop for ensuring connection lifetimes.
        /// </summary>
        /// <remarks>
        ///     This is run on the ThreadPool.
        /// </remarks>
        public void PingPongLoop() => Task.Run(PingPongLoopInternal);

        private async Task PingPongLoopInternal()
        {
            Stack<Connection> abandonedConnections = new Stack<Connection>();

            while (!CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_PingInterval).ConfigureAwait(false);

                foreach ((Guid connectionIdentity, Connection connection) in Connections)
                {
                    if (TryAllocatePing(connectionIdentity, out PendingPing? pendingPing))
                    {
                        await connection.WriteAsync(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray(), CancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Pending ping timed out. Queueing force disconnect."));
                        abandonedConnections.Push(connection);
                    }
                }

                while (abandonedConnections.TryPop(out Connection? connection))
                {
                    ForceDisconnect(connection);
                }
            }
        }

        /// <summary>
        ///     Attempts to allocate a new <see cref="PendingPing" /> for given <see cref="Connection.Identity" />.
        /// </summary>
        /// <param name="connectionIdentity"><see cref="Connection.Identity" /> to allocate for.</param>
        /// <param name="pendingPing"><see cref="PendingPing" /> that was allocated.</param>
        /// <returns><c>True</c> if operation succeeded; otherwise, <c>False</c>.</returns>
        private bool TryAllocatePing(Guid connectionIdentity, [NotNullWhen(true)] out PendingPing? pendingPing)
        {
            pendingPing = new PendingPing();
            return _PendingPings.TryAdd(connectionIdentity, pendingPing);
        }

        #endregion


        #region Handshakes

        /// <inheritdoc />
        protected override async ValueTask ShareIdentityAsync(Connection connection)
        {
            DateTime utcTimestamp = DateTime.UtcNow;
            await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).ConfigureAwait(false);

            await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).ConfigureAwait(false);
        }

        #endregion


        #region Events

        /// <inheritdoc />
        protected override async ValueTask DisconnectedCallback(Connection connection)
        {
            await base.DisconnectedCallback(connection).ConfigureAwait(false);

            _PendingPings.TryRemove(connection.Identity, out _);
        }

        private ValueTask HandlePongPacketsCallback(Connection connection, BasicPacket basicPacket)
        {
            if (basicPacket.Type != PacketType.Pong)
            {
                return default;
            }

            if (!_PendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
                return default;
            }
            else if (basicPacket.Content.Length != 16)
            {
                Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
                return default;
            }

            Guid pingIdentity = new Guid(basicPacket.Content.Span);
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
