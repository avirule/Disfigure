#region

using System;
using System.Net;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            BouncerModule bouncerModule = new BouncerModule(new IPEndPoint(IPAddress.IPv6Loopback, 8899));

            while (!bouncerModule.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }
}
