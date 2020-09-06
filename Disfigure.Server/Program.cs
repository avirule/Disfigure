#region

using System;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.Server
{
    internal class Program
    {
        private static async Task Main()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Verbose().CreateLogger();

            using (Server server = new Server())
            {
                await server.Start();
            }

            Console.ReadLine();
        }
    }
}
