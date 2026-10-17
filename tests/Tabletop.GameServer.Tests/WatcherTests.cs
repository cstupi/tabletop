using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Admin watch spec: an admin non-participant joins as a watcher, gets what a teamless participant gets, and changes nothing.</summary>
public class WatcherTests
{
    readonly InMemorySeeder db = new();
    static string Admin(Guid account) => TestKeys.Mint(account, roles: new[] { "stytch_user", "admin" });

    async Task<SeededGame> PlayingWithLog()
    {
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null), new GameEvent(2, null, new ScoreSet(seeded.TeamIds[0], 1), null)], CancellationToken.None);
        return seeded;
    }

    [Fact]
    public async Task An_admin_non_participant_receives_welcome_board_the_whole_log_and_live_events()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        var admin = await db.SeedAccountAsync("Admin");
        await using var player = await TestClient.OpenAsync(factory);
        await player.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await player.ExpectAsync<AtRest>();

        await using var watcher = await TestClient.OpenAsync(factory);
        var welcome = await watcher.ConnectAsync(admin, seeded.GameId, jwt: Admin(admin));
        Assert.Equal(Guid.Empty, welcome.ParticipantId);                                          // no participant id in the game
        Assert.DoesNotContain(welcome.Game.Participants, p => p.AccountId == admin);              // absent from game info
        Assert.IsType<BoardMessage>(await watcher.ReceiveAsync());
        Assert.Equal([1L, 2L], Assert.IsType<EventsMessage>(await watcher.ReceiveAsync()).Events.Select(e => e.Seq));
        Assert.IsType<ModelFacts>(await watcher.ReceiveAsync());
        Assert.Equal(2L, Assert.IsType<AtRest>(await watcher.ReceiveAsync()).Seq);
        Assert.DoesNotContain(db.GameRows.ParticipantsOf(seeded.GameId), p => p.AccountId == admin);

        await player.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 4)));
        Assert.Equal(3L, (await watcher.ExpectAsync<EventsMessage>()).Events.Single().Seq);
        Assert.Equal(3L, (await watcher.ExpectAsync<AtRest>()).Seq);

        await player.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        Assert.Equal(seeded.Participants[0].ParticipantId, (await watcher.ExpectAsync<PresenceMessage>()).Presence.ParticipantId);
    }

    [Fact]
    public async Task A_watcher_is_refused_every_frame_that_changes_the_game_and_answered_a_sight_query()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        var admin = await db.SeedAccountAsync("Admin");
        await using var player = await TestClient.OpenAsync(factory);
        await player.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await player.ExpectAsync<AtRest>();
        await using var watcher = await TestClient.OpenAsync(factory);
        await watcher.ConnectAsync(admin, seeded.GameId, jwt: Admin(admin));
        await watcher.ExpectAsync<AtRest>();

        var command = Guid.CreateVersion7();
        await watcher.SendAsync(new CommandMessage(command, new SetScore(seeded.TeamIds[0], 9)));
        var refused = Assert.IsType<Refused>(await watcher.ReceiveAsync());
        Assert.Equal((command, RefusalReason.Watcher), (refused.MessageId, refused.Reason));
        var answer = Guid.CreateVersion7();
        await watcher.SendAsync(new CommandMessage(answer, new AnswerAsk(Guid.CreateVersion7(), new Confirmed())));
        Assert.Equal((answer, RefusalReason.Watcher), (Assert.IsType<Refused>(await watcher.ReceiveAsync()) is var a ? (a.MessageId, a.Reason) : default));
        var edit = Guid.CreateVersion7();
        await watcher.SendAsync(new EditMessage(edit, new CreateTeam()));
        Assert.Equal((edit, RefusalReason.Watcher), (Assert.IsType<Refused>(await watcher.ReceiveAsync()) is var e ? (e.MessageId, e.Reason) : default));
        await watcher.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        Assert.Equal(RefusalReason.Watcher, Assert.IsType<Refused>(await watcher.ReceiveAsync()).Reason);
        await watcher.SendAsync(new PingMessage(new Ping(Guid.Empty, new(9, 9, 0), null)));
        Assert.Equal(RefusalReason.Watcher, Assert.IsType<Refused>(await watcher.ReceiveAsync()).Reason);
        var query = Guid.CreateVersion7();
        await watcher.SendAsync(new SightQuery(query, Guid.CreateVersion7(), Guid.CreateVersion7()));
        Assert.Equal(query, Assert.IsType<SightAnswer>(await watcher.ReceiveAsync()).QueryId);

        await player.AssertSilentAsync(TimeSpan.FromMilliseconds(400));                          // nothing appended, relayed or broadcast
        Assert.Equal(2, db.EventCount(seeded.GameId));
    }

    [Fact]
    public async Task A_watcher_leaving_ends_no_presence_and_alone_keeps_the_game_in_memory_until_it_goes()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await PlayingWithLog();
        var admin = await db.SeedAccountAsync("Admin");
        await using var player = await TestClient.OpenAsync(factory);
        await player.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await player.ExpectAsync<AtRest>();
        var watcher = await TestClient.OpenAsync(factory);
        await watcher.ConnectAsync(admin, seeded.GameId, jwt: Admin(admin));
        await watcher.ExpectAsync<AtRest>();
        await watcher.DisposeAsync();
        await player.AssertSilentAsync(TimeSpan.FromMilliseconds(400));                          // no presence_ended for a watcher

        await player.DisposeAsync();
        await using var alone = await TestClient.OpenAsync(factory);
        await alone.ConnectAsync(admin, seeded.GameId, jwt: Admin(admin));
        await alone.ExpectAsync<AtRest>();
        Assert.Single(factory.Services.GetRequiredService<SessionRegistry>().Live);
    }

    [Fact]
    public async Task A_non_admin_non_participant_is_still_rejected()
    {
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var stranger = await db.SeedAccountAsync("Stranger");
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(new Connect(TestKeys.Mint(stranger, roles: new[] { "stytch_user" }), seeded.GameId, ProtocolVersion.Current));
        Assert.Equal(RejectReason.NotAParticipant, Assert.IsType<Rejected>(await client.ReceiveAsync()).Reason);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await client.WaitClosedAsync());
    }
}
