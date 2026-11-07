using System.Net.WebSockets;
using System.Text;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>permessage-deflate on `/ws`, over a real Kestrel: TestServer's socket negotiates no extensions.</summary>
public class SocketCompressionTests
{
    readonly InMemorySeeder db = new();

    static ClientWebSocket Offering(WebSocketDeflateOptions? deflate)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.DangerousDeflateOptions = deflate;
        return socket;
    }

    static async Task<SocketMessage> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[4 * 1024 * 1024]; var total = 0;
        while (true)
        {
            var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException($"closed: {socket.CloseStatus} {socket.CloseStatusDescription}");
            total += r.Count;
            if (r.EndOfMessage) return WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total));
        }
    }

    static Task SendAsync(WebSocket socket, SocketMessage message, WebSocketMessageFlags flags = WebSocketMessageFlags.EndOfMessage) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, flags, CancellationToken.None).AsTask();

    static Uri SocketUri(GameServerFactory factory) => new UriBuilder(factory.ClientOptions.BaseAddress) { Scheme = "ws", Path = "/ws" }.Uri;

    [Fact]
    public async Task A_client_offering_deflate_gets_it_with_server_context_takeover_and_frames_round_trip()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        factory.UseKestrel();
        factory.StartServer();
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);

        using var socket = Offering(SocketCompression.Offer());
        await socket.ConnectAsync(SocketUri(factory), CancellationToken.None);
        var extensions = string.Join(",", socket.HttpResponseHeaders!["Sec-WebSocket-Extensions"]);
        Assert.Contains("permessage-deflate", extensions);
        Assert.DoesNotContain("server_no_context_takeover", extensions);

        // The JWT goes uncompressed, as every client sends it; the rest compressed both ways.
        await SendAsync(socket, new Connect(TestKeys.Mint(seeded.CreatorAccountId), seeded.GameId, ProtocolVersion.Current),
            SocketCompression.Uncompressed);
        Assert.IsType<Welcome>(await ReceiveAsync(socket));
        while (await ReceiveAsync(socket) is not AtRest) { }
        await SendAsync(socket, new CommandMessage(Guid.NewGuid(), new SetScore(seeded.TeamIds[0], 3)));
        var events = new List<GameEvent>();
        while (await ReceiveAsync(socket) is var m && m is not AtRest) if (m is EventsMessage e) events.AddRange(e.Events);
        Assert.Contains(events, e => e.Payload is ScoreSet);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }

    [Fact]
    public async Task A_client_offering_nothing_gets_no_extension()
    {
        using var factory = new GameServerFactory(db);
        factory.UseKestrel();
        factory.StartServer();
        using var socket = Offering(null);
        await socket.ConnectAsync(SocketUri(factory), CancellationToken.None);
        Assert.False(socket.HttpResponseHeaders!.ContainsKey("Sec-WebSocket-Extensions"));
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }
}
