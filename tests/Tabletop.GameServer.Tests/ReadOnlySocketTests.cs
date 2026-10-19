using System.Net.WebSockets;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>A socket opened as `/ws?watch=1` (the client's replay): a participant or an admin joins as a watcher, ended game or running,
/// gets the whole log, and changes nothing.</summary>
public class ReadOnlySocketTests
{
    readonly InMemorySeeder db = new();
    static string Admin(Guid account) => TestKeys.Mint(account, roles: new[] { "stytch_user", "admin" });

    async Task<SeededGame> WithLog(GameStatus status, params EventPayload[] payloads)
    {
        var seeded = await db.SeedGameAsync(status, teams: 2);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, payloads.Select((p, i) => new GameEvent(i + 1, null, p, null)).ToList(), CancellationToken.None);
        return seeded;
    }

    Task<SeededGame> Ended() => WithLog(GameStatus.Ended, new PhaseStarted("deployment"), new ScoreSet(Guid.CreateVersion7(), 1), new GameEnded("over", []));

    static async Task<IReadOnlyList<long>> CatchUpAsync(TestClient client)
    {
        Assert.IsType<BoardMessage>(await client.ReceiveAsync());
        var seqs = Assert.IsType<EventsMessage>(await client.ReceiveAsync()).Events.Select(e => e.Seq).ToList();
        await client.ExpectAsync<AtRest>();
        return seqs;
    }

    [Fact]
    public async Task A_participant_replays_an_ended_game_as_a_watcher_and_is_refused_everything_that_changes_it()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await Ended();
        await using var replay = await TestClient.OpenAsync(factory, watch: true);
        var welcome = await replay.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(Guid.Empty, welcome.ParticipantId);
        Assert.Equal(GameStatus.Ended, welcome.Game.Status);
        Assert.Equal([1L, 2L, 3L], await CatchUpAsync(replay));

        var command = Guid.CreateVersion7();
        await replay.SendAsync(new CommandMessage(command, new Undo(1)));
        Assert.Equal((command, RefusalReason.Watcher), Assert.IsType<Refused>(await replay.ReceiveAsync()) is var c ? (c.MessageId, c.Reason) : default);
        var edit = Guid.CreateVersion7();
        await replay.SendAsync(new EditMessage(edit, new CreateTeam()));
        Assert.Equal((edit, RefusalReason.Watcher), Assert.IsType<Refused>(await replay.ReceiveAsync()) is var e ? (e.MessageId, e.Reason) : default);
        await replay.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        Assert.Equal(RefusalReason.Watcher, Assert.IsType<Refused>(await replay.ReceiveAsync()).Reason);
        await replay.SendAsync(new PingMessage(new Ping(Guid.Empty, new(9, 9, 0), null)));
        Assert.Equal(RefusalReason.Watcher, Assert.IsType<Refused>(await replay.ReceiveAsync()).Reason);
        Assert.Equal(3, db.EventCount(seeded.GameId));
    }

    [Fact]
    public async Task A_participant_replaying_a_running_game_follows_its_live_events_and_is_absent_from_presence()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await WithLog(GameStatus.Playing, new PhaseStarted("deployment"), new ScoreSet(Guid.CreateVersion7(), 1));
        await using var player = await TestClient.OpenAsync(factory);
        await player.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await player.ExpectAsync<AtRest>();
        await using var replay = await TestClient.OpenAsync(factory, watch: true);
        await replay.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        Assert.Equal([1L, 2L], await CatchUpAsync(replay));

        await player.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 4)));
        Assert.Equal(3L, (await replay.ExpectAsync<EventsMessage>()).Events.Single().Seq);
        await replay.DisposeAsync();
        await player.ExpectAsync<AtRest>();
        await player.AssertSilentAsync(TimeSpan.FromMilliseconds(400));                          // no presence_ended for a read-only socket
    }

    [Fact]
    public async Task An_admin_non_participant_watches_an_ended_game_with_or_without_the_read_only_flag()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await Ended();
        var admin = await db.SeedAccountAsync("Admin");
        foreach (var watch in new[] { false, true })
        {
            await using var watcher = await TestClient.OpenAsync(factory, watch);
            Assert.Equal(Guid.Empty, (await watcher.ConnectAsync(admin, seeded.GameId, jwt: Admin(admin))).ParticipantId);
            Assert.Equal([1L, 2L, 3L], await CatchUpAsync(watcher));
        }
        Assert.DoesNotContain(db.GameRows.ParticipantsOf(seeded.GameId), p => p.AccountId == admin);
    }

    [Fact]
    public async Task An_admin_who_is_a_participant_watches_on_a_read_only_socket()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await Ended();
        await using var replay = await TestClient.OpenAsync(factory, watch: true);
        Assert.Equal(Guid.Empty, (await replay.ConnectAsync(seeded.CreatorAccountId, seeded.GameId, jwt: Admin(seeded.CreatorAccountId))).ParticipantId);
        await CatchUpAsync(replay);
        var command = Guid.CreateVersion7();
        await replay.SendAsync(new CommandMessage(command, new Undo(1)));
        Assert.Equal(RefusalReason.Watcher, Assert.IsType<Refused>(await replay.ReceiveAsync()).Reason);
    }

    [Fact]
    public async Task A_read_only_socket_authorizes_no_one_new()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await Ended();
        var stranger = await db.SeedAccountAsync("Stranger");
        await using var client = await TestClient.OpenAsync(factory, watch: true);
        await client.SendAsync(new Connect(TestKeys.Mint(stranger, roles: new[] { "stytch_user" }), seeded.GameId, ProtocolVersion.Current));
        Assert.Equal(RejectReason.NotAParticipant, Assert.IsType<Rejected>(await client.ReceiveAsync()).Reason);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await client.WaitClosedAsync());
    }
}
