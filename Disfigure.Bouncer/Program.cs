#region

using System;
using System.Net;
using Disfigure.Diagnostics;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            BouncerModule bouncerModule = new BouncerModule(LogEventLevel.Verbose, new IPEndPoint(IPAddress.IPv6Loopback, 8899));

            while (!bouncerModule.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }
}
