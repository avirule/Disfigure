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
    public class BouncerModule : ServerModule
    {
        public BouncerModule(IPEndPoint hostAddress) : base(LogEventLevel.Verbose, hostAddress)
        {
            AcceptConnections();
            PingPongLoop();
        }
    }
}
