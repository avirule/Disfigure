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
        public static Connection.MaximumRetry DefaultRetry = new Connection.MaximumRetry(5, 500);

        public static async ValueTask<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, Connection.MaximumRetry maximumRetries,
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

        public static async ValueTask<Connection> EstablishConnectionAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            Connection connection = new Connection(tcpClient);
            await connection.Finalize(cancellationToken).Contextless();

            return connection;
        }

        public static async ValueTask PongAsync(Connection connection, byte[] pingContents)
        {
            Log.Verbose(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint, "Received ping, ponging..."));
            await connection.WriteAsync(PacketType.Pong, DateTime.UtcNow, pingContents, CancellationToken.None).Contextless();
        }
    }
}
