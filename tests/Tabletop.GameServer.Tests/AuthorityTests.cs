using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review H3: who may configure, take a held team, and act once playing.</summary>
public class AuthorityTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Other, TestClient Third)> LobbyAsync()
    {
        var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2, spectators: 1);
        var creator = await TestClient.OpenAsync(factory); var other = await TestClient.OpenAsync(factory); var third = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId); await creator.ExpectAsync<AtRest>();
        await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId); await other.ExpectAsync<AtRest>();
        await third.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId); await third.ExpectAsync<AtRest>();
        return (factory, seeded, creator, other, third);
    }

    [Fact]
    public async Task Only_the_creator_configures_the_game()
    {
        var (f, g, creator, other, third) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; await using var ___ = third; using var ____ = f;
        await other.SendAsync(Edit(new SetConfiguration("Mine", g.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Silent, false)));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        Assert.Equal("Friday", db.GameRows.Game(g.GameId)!.Name);
    }

    [Fact]
    public async Task A_team_another_participant_holds_is_joined_only_by_the_creators_placing()
    {
        var (f, g, creator, other, third) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; await using var ___ = third; using var ____ = f;
        var spectator = g.Participants[2].ParticipantId;
        await third.SendAsync(Edit(new PickTeam(g.TeamIds[0])));
        Assert.Equal(RefusalReason.NotPermitted, (await third.ExpectAsync<Refused>()).Reason);
        await third.SendAsync(Edit(new PickTeam(g.TeamIds[0], g.Participants[1].ParticipantId)));
        Assert.Equal(RefusalReason.NotPermitted, (await third.ExpectAsync<Refused>()).Reason);
        Assert.Null(db.GameRows.Participant(spectator)!.TeamId);

        await creator.SendAsync(Edit(new PickTeam(g.TeamIds[0], spectator)));
        Assert.Equal(g.TeamIds[0], (await third.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.Id == spectator).TeamId);

        await creator.SendAsync(Edit(new CreateTeam()));
        var empty = (await third.ExpectAsync<GameInfoMessage>()).Game.Teams[2].Id;
        await third.SendAsync(Edit(new PickTeam(empty)));
        Assert.Equal(empty, (await third.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.Id == spectator).TeamId);
    }

    [Fact]
    public async Task From_playing_on_a_teamless_participant_watches()
    {
        using var factory = GameServerFactory.WithoutScheduler(db);
        var seeded = await db.SeedGameAsync(GameStatus.Playing, teams: 2, spectators: 1);
        await db.SeedTableStateAsync(seeded.GameId);
        await db.Log.AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);
        await using var player = await TestClient.OpenAsync(factory);
        await player.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await player.ExpectAsync<AtRest>();
        await using var late = await TestClient.OpenAsync(factory);
        await late.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId);
        await late.ExpectAsync<AtRest>();

        foreach (var command in new Command[] { new SetScore(seeded.TeamIds[0], 9), new Undo(0), new SetRound(3) })
        {
            var id = Guid.NewGuid();
            await late.SendAsync(new CommandMessage(id, command));
            var refused = await late.ExpectAsync<Refused>();
            Assert.Equal((id, RefusalReason.Watcher), (refused.MessageId, refused.Reason));
        }
        await late.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        Assert.Equal(RefusalReason.Watcher, (await late.ExpectAsync<Refused>()).Reason);
        await late.SendAsync(new PingMessage(new Ping(Guid.Empty, new(9, 9, 0), null)));
        Assert.Equal(RefusalReason.Watcher, (await late.ExpectAsync<Refused>()).Reason);
        var query = Guid.NewGuid();
        await late.SendAsync(new SightQuery(query, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(query, (await late.ExpectAsync<SightAnswer>()).QueryId);
        await player.AssertSilentAsync(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, db.EventCount(seeded.GameId));
    }
}
