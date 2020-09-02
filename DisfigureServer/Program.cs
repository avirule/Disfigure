#region

using System;
using System.Threading.Tasks;

#endregion

namespace DisfigureServer
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // todo set up static logger

            Server server = new Server();

            await server.Start();

            Console.ReadLine();
        }
    }
}
