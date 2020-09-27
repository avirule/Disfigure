#region

using System;
using System.Net;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Client
{
    internal class Program
    {
        private static async Task Main()
        {
            try
            {
                using ClientModule clientModule = new ClientModule(LogEventLevel.Verbose);
                await clientModule.ConnectAsync(new IPEndPoint(IPAddress.IPv6Loopback, 8898), 5, TimeSpan.FromSeconds(0.5d));

                while (!clientModule.CancellationToken.IsCancellationRequested)
                {
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
