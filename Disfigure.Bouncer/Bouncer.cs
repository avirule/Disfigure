#region

using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Client;
using Disfigure.Net;
using Disfigure.Server;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    public class Bouncer
    {
        /// <summary>
        ///     ServerModule represents the connections to user devices.
        /// </summary>
        private readonly ServerModule _ServerModule;

        /// <summary>
        ///     Client module represents the connection to servers.
        /// </summary>
        private readonly ClientModule _ClientModule;

        private readonly CancellationTokenSource _CancellationTokenSource;

        public CancellationToken CancellationToken => _CancellationTokenSource.Token;

        public Bouncer(IPEndPoint hostAddress)
        {
            _ServerModule = new ServerModule(LogEventLevel.Verbose, hostAddress);
            _ServerModule.PacketReceived += OnServerPacketReceived;

            _ClientModule = new ClientModule(LogEventLevel.Verbose);
            _CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_ServerModule.CancellationToken,
                _ClientModule.CancellationToken);

            _ServerModule.AcceptConnections();
            _ServerModule.PingPongLoop();
        }

        private ValueTask OnServerPacketReceived(Connection connection, Packet packet)
        {
            // packet WILL be decrypted by this point

            return default;
        }
    }
}
