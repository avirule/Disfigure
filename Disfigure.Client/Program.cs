#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Net;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.IPv6Loopback, 8899);
            TcpClient tcpClient = await ConnectionHelper.ConnectAsync(ipEndPoint, 5, TimeSpan.FromMilliseconds(500d),
                CancellationToken.None).Contextless();
            Connection connection = new Connection(tcpClient);
            connection.WaitForPacket(PacketType.EndIdentity);
            await connection.WriteAsync(PacketType.Connect, DateTime.UtcNow, new BinaryEndPoint(ipEndPoint).Data.ToArray(), CancellationToken.None)
                .Contextless();
        }
    }
}
