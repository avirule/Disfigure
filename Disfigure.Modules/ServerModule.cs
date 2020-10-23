#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Disfigure.Cryptography;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;

#endregion


namespace Disfigure.Modules
{
    public class ServerModule : Module<Packet>
    {
        private readonly IPEndPoint _HostAddress;
        private readonly List<string> _Channels;

        public ServerModule(IPEndPoint hostAddress, string? friendlyName = null)
        {
            _HostAddress = hostAddress;
            _Channels = new List<string>
            {
                "test1", "test2"
            };

            FriendlyName = friendlyName ?? hostAddress.ToString();
        }

        public string FriendlyName { get; }

        private Packet CreateIdentityPacket()
        {
            Span<byte> content = stackalloc byte[1024];

            bool isClient = false;
            MemoryMarshal.Write(content, ref isClient);
            int written = Encoding.Unicode.GetBytes(FriendlyName, content.Slice(sizeof(bool)));

            return new Packet(PacketType.Identity, DateTime.UtcNow, content.Slice(0, sizeof(bool) + written));
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
                    await connection.WriteAsync(CreateIdentityPacket(), CancellationToken);

                    foreach (string channel in _Channels)
                    {
                        await connection.WriteAsync(new Packet(PacketType.ChannelIdentity, DateTime.UtcNow, SerializeChannelIdentity(channel)),
                            CancellationToken);
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

        private static unsafe ReadOnlySpan<byte> SerializeChannelIdentity(string channel)
        {
            Guid guid = Guid.NewGuid();

            Span<byte> serialized = new byte[sizeof(Guid) + Encoding.Unicode.GetByteCount(channel)];
            MemoryMarshal.Write(serialized, ref guid);
            Encoding.Unicode.GetBytes(channel).CopyTo(serialized.Slice(sizeof(Guid)));

            return serialized;
        }

        #endregion
    }
}
