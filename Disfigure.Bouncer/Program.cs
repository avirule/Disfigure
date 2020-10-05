#region

using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Disfigure.Diagnostics;
using Disfigure.Modules;
using Disfigure.Net;
using Disfigure.Net.Packets;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Bouncer
{
    internal class Program
    {
        private static BouncerModule<BasicPacket> _Module;

        private static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

            ServerModuleConfiguration configuration = new ServerModuleConfiguration(Assembly.GetExecutingAssembly().GetName().Name, false);

            Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Is(configuration.LogLevel).CreateLogger();

            DiagnosticsProvider.EnableGroup<PacketDiagnosticGroup>();

            _Module = new BouncerModule<BasicPacket>(new IPEndPoint(configuration.HostingIPAddress, configuration.HostingPort));
            _Module.Connected += BasicPacket.SendEncryptionKeys;
            _Module.PacketReceived += PacketReceivedCallback;
            _Module.ServerConnected += BasicPacket.SendEncryptionKeys;
            _Module.ServerPacketReceived += ServerPacketReceivedCallback;

            _Module.AcceptConnections(BasicPacket.EncryptorAsync, BasicPacket.FactoryAsync);
            BasicPacket.PingPongLoop(_Module, TimeSpan.FromSeconds(5d), _Module.CancellationToken);

            while (!_Module.CancellationToken.IsCancellationRequested)
            {
                Console.ReadLine();
            }
        }

        private static async ValueTask PacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.AssignRemoteKeys(packet.Content);
                    break;
                case PacketType.Connect:
                    SerializableEndPoint serializableEndPoint = new SerializableEndPoint(packet.Content);
                    await _Module.EstablishServerConnectionAsync((IPEndPoint)serializableEndPoint, BasicPacket.EncryptorAsync,
                        BasicPacket.FactoryAsync);
                    break;
            }
        }

        private static async ValueTask ServerPacketReceivedCallback(Connection<BasicPacket> connection, BasicPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.EncryptionKeys:
                    connection.AssignRemoteKeys(packet.Content);
                    break;
                case PacketType.Ping:
                    await connection.WriteAsync(new BasicPacket(PacketType.Pong, DateTime.UtcNow, packet.Content), CancellationToken.None);
                    break;
            }
        }
    }
}
