#region

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Client;
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

            _ClientModule = new ClientModule(LogEventLevel.Verbose);
            _CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_ServerModule.CancellationToken,
                _ClientModule.CancellationToken);

            Task.Run(_ServerModule.AcceptConnections);
            Task.Run(_ServerModule.PingPongLoop);
        }

        private void OnServerPacketReceived()
        {

        }
    }
}
