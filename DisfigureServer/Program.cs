#region

using System;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace DisfigureServer
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

            using (Server server = new Server())
            {
                await server.Start();
            }

            Console.ReadLine();
        }
    }
}
