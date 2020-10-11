#region

using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

#endregion

namespace Disfigure.Modules
{
    public class ServerModule : Module<Packet>
    {
        private readonly IPEndPoint _HostAddress;

        public string FriendlyName { get; }

        public ServerModule(IPEndPoint hostAddress, string? friendlyName = null)
        {
            _HostAddress = hostAddress;

            FriendlyName = friendlyName ?? hostAddress.ToString();
        }

        #region Runtime

        /// <summary>
        ///     Begins accepting network connections.
        /// </summary>
        /// <remarks>
        ///     This is run on the ThreadPool.
        /// </remarks>
        public void AcceptConnections(PacketSerializerAsync<Packet> packetSerializerAsync, PacketFactoryAsync<Packet> packetFactoryAsync) =>
            Task.Run(() => AcceptConnectionsInternal(packetSerializerAsync, packetFactoryAsync));

        private async Task AcceptConnectionsInternal(PacketSerializerAsync<Packet> packetSerializerAsync,
            PacketFactoryAsync<Packet> packetFactoryAsync)
        {
            try
            {
                TcpListener listener = new TcpListener(_HostAddress);
                listener.Start();

                Log.Information($"Module is now listening on {_HostAddress}.");

                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync();
                    Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                    Connection<Packet> connection = new Connection<Packet>(tcpClient, new ECDHEncryptionProvider(), packetSerializerAsync,
                        packetFactoryAsync);
                    RegisterConnection(connection);

                    await connection.FinalizeAsync(CancellationToken);
                    await connection.WriteAsync(new Packet(PacketType.Identity, DateTime.UtcNow, Encoding.Unicode.GetBytes(FriendlyName)), CancellationToken);
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

        #endregion
    }
}