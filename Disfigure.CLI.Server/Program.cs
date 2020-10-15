#region

using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using System;
using System.Net;
using System.Threading.Tasks;
using DiagnosticsProviderNS;

#endregion

namespace Disfigure.CLI.Server
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Host hostVerb = CLIParser.Parse<Host>(args);

                Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(hostVerb.LogLevel).CreateLogger();

                DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

                using ServerModule module = new ServerModule(new IPEndPoint(hostVerb.IPAddress, hostVerb.Port), hostVerb.Name);
                module.Connected += Packet.SendEncryptionKeys;
                module.PacketReceived += PacketReceivedCallback;

                module.AcceptConnections(Packet.SerializerAsync, Packet.FactoryAsync);
                Packet.PingPongLoop(module, TimeSpan.FromSeconds(5d), module.CancellationToken);

                while (!module.CancellationToken.IsCancellationRequested)
                {
                    Console.ReadKey();
                }
            }
            finally
            {
                Log.Information("Press any key to exit.");
                Console.ReadKey();
            }
        }

        private static Task PacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.ContentSpan);
                    break;
            }

            return Task.CompletedTask;
        }
    }
}