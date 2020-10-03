#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Disfigure.Net;
using Serilog;
using Serilog.Events;

#endregion

namespace Disfigure.Modules
{
    public class ServerModule<TPacket> : Module<TPacket> where TPacket : IPacket
    {

    private readonly IPEndPoint _HostAddress;

    public ServerModule(LogEventLevel logEventLevel, IPEndPoint hostAddress) : base(logEventLevel)
    {
        _HostAddress = hostAddress;
    }


    #region Runtime

    /// <summary>
    ///     Begins accepting network connections.
    /// </summary>
    /// <remarks>
    ///     This is run on the ThreadPool.
    /// </remarks>
    public void AcceptConnections(PacketFactoryAsync<TPacket> packetFactoryAsync) => Task.Run(() => AcceptConnectionsInternal(packetFactoryAsync));

    private async ValueTask AcceptConnectionsInternal(PacketFactoryAsync<TPacket> packetFactoryAsync)
    {
        try
        {
            TcpListener listener = new TcpListener(_HostAddress);
            listener.Start();

            Log.Information($"{GetType().FullName} now listening on {_HostAddress}.");

            while (!CancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                Log.Information(string.Format(FormatHelper.CONNECTION_LOGGING, tcpClient.Client.RemoteEndPoint, "Connection accepted."));

                Connection<TPacket> connection = new Connection<TPacket>(tcpClient, packetFactoryAsync);
                await connection.Finalize(CancellationToken).ConfigureAwait(false);

                if (!await RegisterConnection(connection).ConfigureAwait(false))
                {
                    Log.Error(string.Format(FormatHelper.CONNECTION_LOGGING, connection.RemoteEndPoint,
                        "Connection with given identity already exists."));

                    connection.Dispose();
                }
            }
        }
        catch (SocketException exception) when (exception.ErrorCode == 10048)
        {
            Log.Fatal($"Port {_HostAddress.Port} is already being listened on.");
        }
        catch (IOException exception) when (exception.InnerException is SocketException)
        {
            Log.Fatal("Remote host forcibly closed connection while connecting.");
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
        }
        finally
        {
            CancellationTokenSource.Cancel();
        }
    }

    #endregion


    #region Handshakes

    /// <inheritdoc />
    protected override async ValueTask ShareIdentityAsync(Connection<TPacket> connection)
    {
        DateTime utcTimestamp = DateTime.UtcNow;
        await connection.WriteAsync(PacketType.BeginIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).ConfigureAwait(false);

        await connection.WriteAsync(PacketType.EndIdentity, utcTimestamp, Array.Empty<byte>(), CancellationToken).ConfigureAwait(false);
    }

    #endregion


    #region Events

    // private ValueTask HandlePongPacketsCallback(Connection<TPacket> connection, TPacket basicPacket)
    // {
    //     if (basicPacket.Type != PacketType.Pong)
    //     {
    //         return default;
    //     }
    //
    //     if (!_PendingPings.TryGetValue(connection.Identity, out PendingPing? pendingPing))
    //     {
    //         Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but no ping was pending.");
    //         return default;
    //     }
    //     else if (basicPacket.Content.Length != 16)
    //     {
    //         Log.Warning($"<{connection.RemoteEndPoint}> Ping identity was malformed (too few bytes).");
    //         return default;
    //     }
    //
    //     Guid pingIdentity = new Guid(basicPacket.Content.Span);
    //     if (pendingPing.Identity != pingIdentity)
    //     {
    //         Log.Warning($"<{connection.RemoteEndPoint}> Received pong, but ping identity didn't match.");
    //         return default;
    //     }
    //
    //     _PendingPings.TryRemove(connection.Identity, out _);
    //
    //     return default;
    // }

    #endregion

    }
}
