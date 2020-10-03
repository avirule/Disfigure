#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.Net
{
    public static class ConnectionHelper
    {
        /// <summary>
        ///     Default retry parameters.
        /// </summary>
        /// <remarks>
        ///     Retries: 5, RetryDelay: 500
        /// </remarks>
        public static RetryParameters DefaultRetryParameters = new RetryParameters(5, 500);

        /// <summary>
        ///     Safely connects to a given <see cref="IPEndPoint" />, with optional retry parameters.
        /// </summary>
        /// <param name="ipEndPoint"><see cref="IPEndPoint" /> to connect to.</param>
        /// <param name="retriesParameters"><see cref="RetryParameters" /> to reference retry parameters from.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe when retrying.</param>
        /// <returns><see cref="TcpClient" /> representing complete connection.</returns>
        public static async ValueTask<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, RetryParameters retriesParameters,
            CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Debug($"Connecting to {ipEndPoint}.");

            while (!cancellationToken.IsCancellationRequested && !tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).ConfigureAwait(false);
                }
                catch (SocketException) when (tries >= retriesParameters.Retries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{retriesParameters})...");

                    await Task.Delay(retriesParameters.Delay, cancellationToken).ConfigureAwait(false);
                }
            }

            return tcpClient;
        }

        public static async Task PingPongLoop<TPacket>(TimeSpan pingInterval, IReadOnlyDictionary<Guid, Connection<TPacket>> connections,
            Action<Guid> forceDisconnect, CancellationToken cancellationToken) where TPacket : IPacket
        {
            ConcurrentDictionary<Guid, PendingPing> pendingPings = new ConcurrentDictionary<Guid, PendingPing>();
            Stack<Guid> abandonedConnections = new Stack<Guid>();

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(pingInterval, cancellationToken).ConfigureAwait(false);

                foreach ((Guid connectionIdentity, Connection<TPacket> connection) in connections)
                {
                    PendingPing pendingPing = new PendingPing();

                    if (pendingPings.TryAdd(connectionIdentity, pendingPing))
                    {
                        await connection.WriteAsync(PacketType.Ping, DateTime.UtcNow, pendingPing.Identity.ToByteArray(), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warning(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                            "Pending ping timed out. Queueing force disconnect."));
                        abandonedConnections.Push(connectionIdentity);
                    }
                }

                while (abandonedConnections.TryPop(out Guid connectionIdentity))
                {
                    forceDisconnect(connectionIdentity);
                }
            }
        }
    }
}
