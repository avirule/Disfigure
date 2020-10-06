#region

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;

#endregion

namespace Disfigure.CLI.Bouncer
{
    internal class Program
    {
        private static BouncerModule? _Module;

        private static void Main(string[] args)
        {
            HostModuleOption hostModuleOption = CLIParser.Parse<HostModuleOption>(args);

            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(hostModuleOption.LogLevel).CreateLogger();

            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            _Module = new BouncerModule(new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port));
            _Module.Connected += Packet.SendEncryptionKeys;
            _Module.PacketReceived += PacketReceivedCallback;
            _Module.ServerConnected += Packet.SendEncryptionKeys;
            _Module.ServerPacketReceived += ServerPacketReceivedCallback;

            _Module.AcceptConnections(Packet.EncryptorAsync, Packet.FactoryAsync);
            Packet.PingPongLoop(_Module, TimeSpan.FromSeconds(5d), _Module.CancellationToken);

            while (!_Module.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }

        private static async ValueTask PacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.AssignRemoteKeys(packet.Content);
                    break;
                case PacketType.Connect:
                    SerializableEndPoint serializableEndPoint = new SerializableEndPoint(packet.Content);
                    await _Module.EstablishServerConnectionAsync((IPEndPoint)serializableEndPoint, Packet.EncryptorAsync,
                        Packet.FactoryAsync);
                    break;
            }
        }

        private static async ValueTask ServerPacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.AssignRemoteKeys(packet.Content);
                    break;
                case PacketType.Ping:
                    await connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                    break;
            }
        }
    }
}
