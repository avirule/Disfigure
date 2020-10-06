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
        public readonly struct RetryParameters
        {
            public readonly int Retries;
            public readonly TimeSpan Delay;

            public RetryParameters(int retries, long delayMilliseconds) => (Retries, Delay) = (retries, TimeSpan.FromMilliseconds(delayMilliseconds));
        }

        /// <summary>
        ///     Default retry parameters.
        /// </summary>
        /// <remarks>
        ///     Retries: 5, RetryDelay: 500 (ms)
        /// </remarks>
        public static RetryParameters DefaultRetryParameters = new RetryParameters(5, 500);

        /// <summary>
        ///     Safely connects to a given <see cref="IPEndPoint" />.
        /// </summary>
        /// <param name="ipEndPoint"><see cref="IPEndPoint" /> to connect to.</param>
        /// <param name="retryParameters"><see cref="RetryParameters" /> to reference retry parameters from.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken" /> to observe when retrying.</param>
        /// <returns><see cref="TcpClient" /> representing completed connection.</returns>
        public static async ValueTask<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, RetryParameters retryParameters,
            CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Information($"Connecting to {ipEndPoint}.");

            while (!cancellationToken.IsCancellationRequested && !tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port);
                }
                catch (SocketException) when (tries >= retryParameters.Retries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{retryParameters.Retries})...");

                    await Task.Delay(retryParameters.Delay, cancellationToken);
                }
            }

            return tcpClient;
        }
    }
}
