using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// A test's socket to the game server. The socket opens at the first frame, once that frame is serialized, as a real client
/// opens and sends at once: the server's handshake timeout runs from the open, so a socket opened ahead of its connect frame
/// (another socket's whole connect in between, or a cold serializer's first use) races that timeout on a loaded machine.
/// </summary>
public sealed class TestClient : IAsyncDisposable
{
    readonly GameServerFactory _factory;
    WebSocket? _socket;

    TestClient(GameServerFactory factory) => _factory = factory;

    /// <summary>Starts the server; the socket itself opens at the first frame.</summary>
    public static Task<TestClient> OpenAsync(GameServerFactory factory)
    {
        _ = factory.Server;
        return Task.FromResult(new TestClient(factory));
    }

    async Task<WebSocket> SocketAsync() =>
        _socket ??= await _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

    WebSocket Opened => _socket ?? throw new InvalidOperationException("nothing sent yet: the socket opens at the first frame");

    public async Task SendAsync(SocketMessage message)
    {
        var frame = Encoding.UTF8.GetBytes(WireJson.Serialize(message));
        await (await SocketAsync()).SendAsync(frame, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task<SocketMessage> ReceiveAsync(TimeSpan? timeout = null)
    {
        var socket = Opened;
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var buffer = new byte[4 * 1024 * 1024]; var total = 0;
        while (true)
        {
            var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException($"closed: {socket.CloseStatus} {socket.CloseStatusDescription}");
            total += r.Count;
            if (r.EndOfMessage) return WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total));
        }
    }

    public async Task<T> ExpectAsync<T>() where T : SocketMessage
    {
        for (var i = 0; i < 50; i++) if (await ReceiveAsync() is T t) return t;
        throw new InvalidOperationException($"no {typeof(T).Name} within 50 frames");
    }

    public async Task<Welcome> ConnectAsync(Guid accountId, Guid gameId, int? version = null, string? jwt = null)
    {
        await SendAsync(new Connect(jwt ?? TestKeys.Mint(accountId), gameId, version ?? ProtocolVersion.Current));
        return await ExpectAsync<Welcome>();
    }

    public async Task<WebSocketCloseStatus?> WaitClosedAsync()
    {
        var socket = Opened;
        var buffer = new byte[1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
        {
            var r = await socket.ReceiveAsync(buffer, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) break;
        }
        return socket.CloseStatus;
    }

    public async Task AssertSilentAsync(TimeSpan window)
    {
        try { var frame = await ReceiveAsync(window); throw new Xunit.Sdk.XunitException($"expected silence, got {frame.GetType().Name}"); }
        catch (OperationCanceledException) { }
    }

    public ValueTask DisposeAsync() { _socket?.Dispose(); return ValueTask.CompletedTask; }
}
