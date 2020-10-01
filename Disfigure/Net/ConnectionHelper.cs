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
        public static Connection.MaximumRetry DefaultRetry = new Connection.MaximumRetry(5, 500);

        /// <summary>
        ///     Safely connects to a given <see cref="IPEndPoint"/>, with optional retry parameters.
        /// </summary>
        /// <param name="ipEndPoint"><see cref="IPEndPoint"/> to connect to.</param>
        /// <param name="maximumRetries"><see cref="Connection.MaximumRetry"/> to reference retry parameters from.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to observe when retrying.</param>
        /// <returns><see cref="TcpClient"/> representing complete connection.</returns>
        public static async ValueTask<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, Connection.MaximumRetry maximumRetries,
            CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Debug($"Connecting to {ipEndPoint}.");

            while (!cancellationToken.IsCancellationRequested && !tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).Contextless();
                }
                catch (SocketException) when (tries >= maximumRetries.Retries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{maximumRetries})...");

                    await Task.Delay(maximumRetries.RetryDelay, cancellationToken).Contextless();
                }
            }

            return tcpClient;
        }

        /// <summary>
        ///     Takes a <see cref="TcpClient"/> and finalizes a <see cref="Connection"/> object from it.
        /// </summary>
        /// <param name="tcpClient"><see cref="TcpClient"/> to finalize <see cref="Connection"/> from.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to observe.</param>
        /// <returns></returns>
        public static async ValueTask<Connection> EstablishConnectionAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            Connection connection = new Connection(tcpClient);
            await connection.Finalize(cancellationToken).Contextless();

            return connection;
        }

        /// <summary>
        ///     Construct and send a verified pong <see cref="Packet"/> to connection.
        /// </summary>
        /// <param name="connection"><see cref="Connection"/> to send pong <see cref="Packet"/> to.</param>
        /// <param name="pingContents"><see cref="Guid"/> bytes to pong back to <see cref="Connection"/>.</param>
        public static async ValueTask PongAsync(Connection connection, byte[] pingContents)
        {
            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
            await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, pingContents, CancellationToken.None).Contextless();
        }
    }
}
