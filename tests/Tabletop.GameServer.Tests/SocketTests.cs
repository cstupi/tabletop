using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SocketTests
{
    readonly InMemorySeeder db = new();
    [Fact]
    public async Task Connecting_at_lobby_yields_welcome_and_no_board()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, spectators: 1);
        await using var client = await TestClient.OpenAsync(factory);
        var welcome = await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(seeded.Participants[0].ParticipantId, welcome.ParticipantId);
        Assert.Equal(GameStatus.Lobby, welcome.Game.Status);
        Assert.Equal(3, welcome.Game.Participants.Count);
        Assert.Equal(0, (await client.ExpectAsync<AtRest>()).Seq);   // the connect sequence ends at rest even where there is no log
        await client.AssertSilentAsync(TimeSpan.FromMilliseconds(300));
    }

    [Theory]
    [InlineData("version", RejectReason.UpgradeRequired)] [InlineData("jwt", RejectReason.InvalidSession)] [InlineData("stranger", RejectReason.NotAParticipant)] [InlineData("no-game", RejectReason.NotAParticipant)]
    public async Task A_bad_connect_is_rejected_then_closed(string flaw, RejectReason expected)
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var stranger = await db.SeedAccountAsync("Stranger");
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(flaw switch
        {
            "version" => new Connect(TestKeys.Mint(seeded.CreatorAccountId), seeded.GameId, 0),
            "jwt" => new Connect("not.a.jwt", seeded.GameId, ProtocolVersion.Current),
            "stranger" => new Connect(TestKeys.Mint(stranger), seeded.GameId, ProtocolVersion.Current),
            _ => new Connect(TestKeys.Mint(seeded.CreatorAccountId), Guid.CreateVersion7(), ProtocolVersion.Current),
        });
        Assert.Equal(expected, Assert.IsType<Rejected>(await client.ReceiveAsync()).Reason);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_first_frame_that_is_not_connect_is_a_protocol_error()
    {
        using var factory = new GameServerFactory(db);
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(new PingMessage(new Ping(Guid.Empty, new(0, 0, 0), null)));
        Assert.Equal(WebSocketCloseStatus.ProtocolError, await client.WaitClosedAsync());
    }

    /// <summary>Ruling 1: a verifier failure (JWKS unreachable, say) is "could not check", not "no" — the socket closes
    /// 1011 rather than getting Rejected.</summary>
    [Fact]
    public async Task A_verifier_exception_closes_internal_server_error_not_rejected()
    {
        using var factory = new GameServerFactory(db) { ConfigureTestServices = services => services.AddSingleton<ISessionVerifier>(new ThrowingVerifier()) };
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(new Connect(TestKeys.Mint(seeded.CreatorAccountId), seeded.GameId, ProtocolVersion.Current));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task Connecting_at_playing_yields_welcome_board_and_the_log_then_live_events()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null), new GameEvent(2, null, new ScoreSet(seeded.TeamIds[0], 1), null)], CancellationToken.None);

        await using var a = await TestClient.OpenAsync(factory);
        await using var b = await TestClient.OpenAsync(factory);
        var welcome = await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(GameStatus.Playing, welcome.Game.Status);
        Assert.Empty((await a.ExpectAsync<BoardMessage>()).Board.Armies);
        Assert.Equal([1L, 2L], (await a.ExpectAsync<EventsMessage>()).Events.Select(e => e.Seq));
        await b.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await b.ExpectAsync<BoardMessage>(); await b.ExpectAsync<EventsMessage>();

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 7)));
        var live = await a.ExpectAsync<EventsMessage>();
        Assert.Equal(3, live.Events.Single().Seq);
        Assert.Equal(seeded.Participants[0].ParticipantId, live.Events.Single().Actor);
        Assert.Equal(7, ((ScoreSet)(await b.ExpectAsync<EventsMessage>()).Events.Single().Payload).Score);
        await b.ExpectAsync<AtRest>();   // drain the rest frame this broadcast owes b, so the next silence check isn't fooled by a stale frame
        Assert.Contains(await db.Log.ReadAsync(seeded.GameId, 0, CancellationToken.None), e => e.Seq == 3);

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(RefusalReason.Unknown, (await a.ExpectAsync<Refused>()).Reason);
        await b.AssertSilentAsync(TimeSpan.FromMilliseconds(200));   // a refusal appends nothing, so b gets neither events nor a rest frame
    }

    [Fact]
    public async Task At_rest_closes_the_connect_sequence_and_follows_every_batch()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);

        await using var a = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.IsType<BoardMessage>(await a.ReceiveAsync());
        Assert.Equal([1L], Assert.IsType<EventsMessage>(await a.ReceiveAsync()).Events.Select(e => e.Seq));
        Assert.IsType<ModelFacts>(await a.ReceiveAsync());
        Assert.Equal(1L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // the whole log, the facts, then rest, with nothing between
        await a.AssertSilentAsync(TimeSpan.FromMilliseconds(300));                     // an idle game says nothing more

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 5)));
        Assert.Equal(2L, Assert.IsType<EventsMessage>(await a.ReceiveAsync()).Events.Single().Seq);
        Assert.Equal(2L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // after the batch's events, never before them

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(await a.ReceiveAsync()).Reason);
        Assert.Equal(2L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // refused: the high-water mark stands and only the sender hears it
    }

    [Fact]
    public async Task Reconnecting_rebuilds_from_the_log_and_replaces_the_older_socket()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await db.SeedGameAsync(GameStatus.Playing);
        await db.SeedTableStateAsync(seeded.GameId);
        await using var first = await TestClient.OpenAsync(factory);
        await first.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await first.ExpectAsync<EventsMessage>();
        await first.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[1], 2)));
        await first.ExpectAsync<EventsMessage>();

        await using var second = await TestClient.OpenAsync(factory);
        var back = await second.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[0], back.Game.Participants.Single(p => p.Id == back.ParticipantId).TeamId);   // the rejoin returns the slot; nothing re-picks
        await second.ExpectAsync<BoardMessage>();
        Assert.Equal([1L], (await second.ExpectAsync<EventsMessage>()).Events.Select(e => e.Seq));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await first.WaitClosedAsync());
    }

    [Fact]
    public async Task The_last_socket_leaving_releases_the_session_and_the_next_hydrates_again()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await db.SeedGameAsync(GameStatus.Playing);
        await db.SeedTableStateAsync(seeded.GameId);
        var registry = factory.Services.GetRequiredService<Tabletop.GameServer.Session.SessionRegistry>();
        await using (var client = await TestClient.OpenAsync(factory))
        {
            await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await client.ExpectAsync<EventsMessage>();
            Assert.Contains(registry.Live, s => s.GameId == seeded.GameId);
            await client.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 3)));
            await client.ExpectAsync<EventsMessage>();
        }
        await Wait.UntilAsync(() => !registry.Live.Any(s => s.GameId == seeded.GameId), "the session was not released within 5s of the last socket leaving");
        Assert.DoesNotContain(registry.Live, s => s.GameId == seeded.GameId);
        await using var again = await TestClient.OpenAsync(factory);
        await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await again.ExpectAsync<BoardMessage>();
        Assert.Equal(3, ((ScoreSet)(await again.ExpectAsync<EventsMessage>()).Events.Single().Payload).Score);
    }
}

/// <summary>A verifier standing in for a JWKS fetch that failed: it cannot say yes or no, only that it could not check.</summary>
public sealed class ThrowingVerifier : ISessionVerifier
{
    public Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct) => throw new HttpRequestException("jwks unreachable");
}
