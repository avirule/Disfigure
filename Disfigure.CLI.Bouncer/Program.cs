#region

using Disfigure.Cryptography;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Disfigure.CLI.Bouncer
{
    internal class Program
    {
        private static BouncerModule? _Module;

        private static void Main(string[] args)
        {
            Host hostModuleOption = CLIParser.Parse<Host>(args);

            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(hostModuleOption.LogLevel).CreateLogger();

            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            _Module = new BouncerModule(new IPEndPoint(hostModuleOption.IPAddress, hostModuleOption.Port));
            _Module.Connected += Packet.SendEncryptionKeys;
            _Module.PacketReceived += PacketReceivedCallback;
            _Module.ServerConnected += Packet.SendEncryptionKeys;
            _Module.ServerPacketReceived += ServerPacketReceivedCallback;

            _Module.AcceptConnections(Packet.SerializerAsync, Packet.FactoryAsync);
            Packet.PingPongLoop(_Module, TimeSpan.FromSeconds(5d), _Module.CancellationToken);

            while (!_Module.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }

        private static async Task PacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.ContentSpan);
                    break;

                case PacketType.Connect when _Module is { }:
                    SerializableEndPoint serializableEndPoint = new SerializableEndPoint(packet.ContentSpan);
                    await _Module.EstablishServerConnectionAsync((IPEndPoint)serializableEndPoint, Packet.SerializerAsync, Packet.FactoryAsync);
                    break;
            }
        }

        private static async Task ServerPacketReceivedCallback(Connection<Packet> connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.EncryptionProviderAs<ECDHEncryptionProvider>().AssignRemoteKeys(packet.ContentSpan);
                    break;

                case PacketType.Ping:
                    await connection.WriteAsync(new Packet(PacketType.Pong, DateTime.UtcNow, packet.ContentSpan), CancellationToken.None);
                    break;
            }
        }
    }
}