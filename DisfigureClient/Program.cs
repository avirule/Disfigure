#region

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using DisfigureCore;

#endregion

namespace DisfigureClient
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            TcpClient tcpClient = new TcpClient();

            int tries = 0;
            while (!tcpClient.Connected)
            {
                try
                {
                    await tcpClient.ConnectAsync(IPAddress.IPv6Loopback, 8898);
                }
                catch (Exception)
                {
                    tries += 1;

                    if (tries > 10)
                    {
                        return;
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
            }

            NetworkStream stream = tcpClient.GetStream();
            Message message = new Message(DateTime.UtcNow, MessageType.Text, Encoding.Unicode.GetBytes("test message with emoji 🍑"));
            await stream.WriteAsync(message.Serialize());
            await stream.FlushAsync();
            stream.Close();
        }
    }
}
