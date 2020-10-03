#region

using System;
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
        /// <param name="retriesParameters"><see cref="Connection.RetryParameters" /> to reference retry parameters from.</param>
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

        /// <summary>
        ///     Construct and send a verified pong <see cref="BasicPacket" /> to connection.
        /// </summary>
        /// <param name="connection"><see cref="Connection" /> to send pong <see cref="BasicPacket" /> to.</param>
        /// <param name="pingContents"><see cref="Guid" /> bytes to pong back to <see cref="Connection" />.</param>
        // public static async ValueTask PongAsync(Connection<BasicPacket> connection, byte[] pingContents)
        // {
        //     Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
        //     await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, pingContents, CancellationToken.None).ConfigureAwait(false);
        // }
    }
}
