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
            Bouncer bouncer = new Bouncer(new IPEndPoint(IPAddress.IPv6Loopback, 8899));

            while (!bouncer.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }
    }
}
