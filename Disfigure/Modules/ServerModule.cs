#region

using System;
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
    public class ServerModule<TPacket> : Module<TPacket> where TPacket : IPacket<TPacket>
    {
        private readonly IPEndPoint _HostAddress;

        public ServerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel) => _HostAddress = hostAddress;


        #region Runtime

        /// <summary>
        ///     Begins accepting network connections.
        /// </summary>
        /// <remarks>
        ///     This is run on the ThreadPool.
        /// </remarks>
        public void AcceptConnections(PacketEncryptorAsync<TPacket> packetEncryptorAsync, PacketFactoryAsync<TPacket> packetFactoryAsync) =>
            Task.Run(() => AcceptConnectionsInternal(packetEncryptorAsync, packetFactoryAsync));

        private async ValueTask AcceptConnectionsInternal(PacketEncryptorAsync<TPacket> packetEncryptorAsync,
            PacketFactoryAsync<TPacket> packetFactoryAsync)
        {
            try
            {
                TcpListener listener = new TcpListener(_HostAddress);
                listener.Start();

                Log.Information($"{GetType().FullName} now listening on {_HostAddress}.");

                while (!CancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync();
                    Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                    Connection<TPacket> connection = new Connection<TPacket>(tcpClient, packetEncryptorAsync, packetFactoryAsync);
                    RegisterConnection(connection);

                    await connection.StartAsync(CancellationToken);
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
