using System.Net.WebSockets;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review H4, M2, L1, L2: frame sizes, sockets per account, a replaced socket's reads, the session's expiry, frame rates.</summary>
public class ConnectionLimitsTests
{
    readonly InMemorySeeder db = new();

    async Task<SeededGame> PlayingWithLog()
    {
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);
        return seeded;
    }

    [Fact]
    public async Task A_connect_frame_over_8_KB_closes_1009()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(new Connect(new string('a', 9 * 1024), seeded.GameId, ProtocolVersion.Current));
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_command_frame_over_64_KB_closes_1009()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();
        await client.SendAsync(new CommandMessage(Guid.NewGuid(), new SetScore(seeded.TeamIds[0], 1)) { });
        await client.ExpectAsync<AtRest>();
        await client.SendAsync(new PingMessage(new Ping(Guid.Empty, new(1, 1, 0), null)));
        await client.SendAsync(new CommandMessage(Guid.NewGuid(), new RecordAction(Guid.NewGuid(), new string('x', 70 * 1024))));
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_replaced_socket_is_read_no_further()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        await using var old = await TestClient.OpenAsync(factory);
        await old.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await old.ExpectAsync<AtRest>();
        await using var fresh = await TestClient.OpenAsync(factory);
        await fresh.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await fresh.ExpectAsync<AtRest>();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await old.WaitClosedAsync());
        try { await old.SendAsync(new CommandMessage(Guid.NewGuid(), new SetScore(seeded.TeamIds[0], 7))); } catch (WebSocketException) { }
        await fresh.AssertSilentAsync(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, db.EventCount(seeded.GameId));
    }

    [Fact]
    public async Task A_fifth_socket_for_one_account_closes_its_oldest()
    {
        using var factory = new GameServerFactory(db);
        var account = await db.SeedAccountAsync("Busy");
        var clients = new List<TestClient>();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var game = await db.SeedGameAsync(GameStatus.Lobby);
                await db.SeedParticipantAsync(game.GameId, account, null);
                var c = await TestClient.OpenAsync(factory);
                clients.Add(c);
                await c.ConnectAsync(account, game.GameId);
                await c.ExpectAsync<AtRest>();
            }
            Assert.Equal(WebSocketCloseStatus.NormalClosure, await clients[0].WaitClosedAsync());
            await clients[1].AssertSilentAsync(TimeSpan.FromMilliseconds(200));
        }
        finally { foreach (var c in clients) await c.DisposeAsync(); }
    }

    [Fact]
    public async Task The_socket_closes_1008_when_the_session_expires()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId, jwt: TestKeys.Mint(seeded.CreatorAccountId, lifetime: TimeSpan.FromSeconds(2)));
        await client.ExpectAsync<AtRest>();
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task Pings_over_the_rate_are_dropped()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var a = await TestClient.OpenAsync(factory);
        await using var b = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await a.ExpectAsync<AtRest>();
        await b.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await b.ExpectAsync<AtRest>();
        for (var i = 0; i < 30; i++) await a.SendAsync(new PingMessage(new Ping(Guid.Empty, new(i, 0, 0), null)));
        var pings = 0;
        try { while (true) if (await b.ReceiveAsync(TimeSpan.FromMilliseconds(500)) is PingMessage) pings++; }
        catch (OperationCanceledException) { }
        Assert.InRange(pings, 1, 8);
    }
}
