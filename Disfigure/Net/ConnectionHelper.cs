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
        public static async ValueTask<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, int maximumRetries, TimeSpan retryDelay,
            CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new TcpClient();
            int tries = 0;

            Log.Debug($"Connecting to {ipEndPoint}.");

            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).Contextless();
                }
                catch (SocketException) when (tries >= maximumRetries)
                {
                    Log.Error($"Connection to {ipEndPoint} failed.");
                    throw;
                }
                catch (SocketException exception)
                {
                    tries += 1;

                    Log.Warning($"{exception.Message}. Retrying ({tries}/{maximumRetries})...");

                    await Task.Delay(retryDelay, cancellationToken).Contextless();
                }
            }

            return tcpClient;
        }
    }
}
