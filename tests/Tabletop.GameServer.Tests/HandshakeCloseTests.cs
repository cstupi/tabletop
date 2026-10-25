using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Tabletop.GameServer.Session;

namespace Tabletop.GameServer.Tests;

/// <summary>A handshake close waits for the peer's close frame, so a client that decompresses asynchronously (Node's WebSocket)
/// reads the Rejected frame before the connection ends.</summary>
public class HandshakeCloseTests
{
    static async Task<(WebSocket Server, WebSocket Client, IDisposable Sockets)> PairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connecting = new TcpClient();
        await connecting.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = await listener.AcceptTcpClientAsync();
        var server = WebSocket.CreateFromStream(accepted.GetStream(), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
        var client = WebSocket.CreateFromStream(connecting.GetStream(), isServer: false, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
        return (server, client, new Both(accepted, connecting));
    }

    sealed class Both(IDisposable a, IDisposable b) : IDisposable { public void Dispose() { a.Dispose(); b.Dispose(); } }

    [Fact]
    public async Task The_close_waits_for_the_peers_close_frame()
    {
        var (server, client, sockets) = await PairAsync();
        using var _ = sockets;
        var closing = ConnectionHandshake.CloseAsync(server, WebSocketCloseStatus.NormalClosure, "NotAParticipant", TimeSpan.FromSeconds(5));

        var buffer = new byte[64];
        var received = await client.ReceiveAsync(buffer, CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        await Task.Delay(200);
        Assert.False(closing.IsCompleted);                       // still waiting for the client's answer

        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(WebSocketState.Closed, server.State);
    }

    [Fact]
    public async Task The_close_gives_up_after_the_grace()
    {
        var (server, _, sockets) = await PairAsync();
        using var __ = sockets;
        var watch = Stopwatch.StartNew();
        await ConnectionHandshake.CloseAsync(server, WebSocketCloseStatus.NormalClosure, "NotAParticipant", TimeSpan.FromMilliseconds(300)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2));
    }
}
