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
    public async Task A_socket_closed_over_its_accounts_cap_is_read_no_further()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        await using var old = await TestClient.OpenAsync(factory);
        await old.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await old.ExpectAsync<AtRest>();
        var newer = new List<TestClient>();
        for (var i = 0; i < 4; i++)                                                          // one game, one account: the fifth socket closes the oldest
        {
            var c = await TestClient.OpenAsync(factory); newer.Add(c);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.ExpectAsync<AtRest>();
        }
        var fresh = newer[^1];
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await old.WaitClosedAsync());
        try { await old.SendAsync(new CommandMessage(Guid.NewGuid(), new SetScore(seeded.TeamIds[0], 7))); } catch (WebSocketException) { }
        await fresh.AssertSilentAsync(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, db.EventCount(seeded.GameId));
        foreach (var c in newer) await c.DisposeAsync();
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
    public async Task A_reauth_of_the_same_account_keeps_the_socket_past_its_first_expiry()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId, jwt: TestKeys.Mint(seeded.CreatorAccountId, lifetime: TimeSpan.FromSeconds(2)));
        await client.ExpectAsync<AtRest>();
        await client.SendAsync(new Reauth(Guid.NewGuid(), TestKeys.Mint(seeded.CreatorAccountId)));
        await client.AssertSilentAsync(TimeSpan.FromSeconds(3));
        await client.SendAsync(new CommandMessage(Guid.NewGuid(), new SetScore(seeded.TeamIds[0], 1)));
        await client.ExpectAsync<AtRest>();
    }

    [Fact]
    public async Task A_reauth_naming_another_account_is_refused_and_the_first_expiry_stands()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId, jwt: TestKeys.Mint(seeded.CreatorAccountId, lifetime: TimeSpan.FromSeconds(3)));
        await client.ExpectAsync<AtRest>();
        var id = Guid.NewGuid();
        await client.SendAsync(new Reauth(id, TestKeys.Mint(seeded.Participants[1].AccountId)));
        var refused = await client.ExpectAsync<Refused>();
        Assert.Equal((id, RefusalReason.NotPermitted), (refused.MessageId, refused.Reason));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_reauth_from_a_participant_who_has_left_is_refused_and_the_socket_closes()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();
        db.GameRows.RemoveParticipant(seeded.GameId, seeded.CreatorAccountId);
        var id = Guid.NewGuid();
        await client.SendAsync(new Reauth(id, TestKeys.Mint(seeded.CreatorAccountId)));
        Assert.Equal((id, RefusalReason.NotPermitted), (await client.ExpectAsync<Refused>()) is var r ? (r.MessageId, r.Reason) : default);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_watchers_reauth_after_the_game_is_deleted_is_refused_and_the_socket_closes()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var admin = await db.SeedAccountAsync("Admin");
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(admin, seeded.GameId, jwt: TestKeys.Mint(admin, roles: new[] { "admin" }));
        await client.ExpectAsync<AtRest>();
        db.GameRows.DeleteGame(seeded.GameId);
        var id = Guid.NewGuid();
        await client.SendAsync(new Reauth(id, TestKeys.Mint(admin, roles: new[] { "admin" })));
        Assert.Equal(id, (await client.ExpectAsync<Refused>()).MessageId);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_reauth_that_does_not_verify_is_refused()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();
        var id = Guid.NewGuid();
        await client.SendAsync(new Reauth(id, TestKeys.Mint(seeded.CreatorAccountId, issuer: "stytch.com/someone-else")));
        Assert.Equal(id, (await client.ExpectAsync<Refused>()).MessageId);
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
