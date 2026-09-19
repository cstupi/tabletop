using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public sealed class TestClient(WebSocket socket) : IAsyncDisposable
{
    public static async Task<TestClient> OpenAsync(GameServerFactory factory) =>
        new(await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None));

    public async Task SendAsync(SocketMessage message) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    public async Task<SocketMessage> ReceiveAsync(TimeSpan? timeout = null)
    {
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

    public ValueTask DisposeAsync() { socket.Dispose(); return ValueTask.CompletedTask; }
}
