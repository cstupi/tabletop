using Microsoft.Extensions.DependencyInjection;
using Tabletop.Geometry;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Game-flow spec → Waiting for players: ending setup always opens the game, and deployment waits while a team has no player or no army.</summary>
public class WaitingForPlayersTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static CommandMessage Cmd(Command c) => new(Guid.CreateVersion7(), c);
    GameServerFactory Factory() => new(db) { ConfigureTestServices = s => s.AddSingleton<IDiceSource>(new ScriptedDice(6, 1)) };   // team 1 rolls 6, team 2 rolls 1

    /// <summary>The creator alone on team 1 with an army, team 2 empty, setup ended.</summary>
    async Task<(SeededGame Seeded, TestClient Creator)> EndedSetupAlone(GameServerFactory factory)
    {
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 0);
        await db.SeedParticipantAsync(seeded.GameId, seeded.CreatorAccountId, seeded.TeamIds[0]);
        var army = await db.SeedArmyAsync(seeded.CreatorAccountId);
        var c = await TestClient.OpenAsync(factory);
        await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await c.SendAsync(Edit(new PickArmy(army.Id))); await c.ExpectAsync<GameInfoMessage>();
        await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
        await c.SendAsync(Edit(new EndSetup()));
        Assert.Equal(GameStatus.Playing, (await c.ExpectAsync<GameInfoMessage>()).Game.Status);
        await c.ExpectAsync<BoardMessage>();
        Assert.Empty((await c.ExpectAsync<EventsMessage>()).Events);
        return (seeded, c);
    }

    [Fact]
    public async Task Ending_setup_with_an_empty_team_opens_the_game_and_deployment_waits_with_an_empty_log()
    {
        using var factory = Factory();
        var (seeded, creator) = await EndedSetupAlone(factory);
        await using var _ = creator;
        Assert.Equal(0L, (await creator.ExpectAsync<AtRest>()).Seq);                           // no roll-off: the log is still empty
        Assert.Equal(0, db.EventCount(seeded.GameId));

        await creator.SendAsync(Cmd(new SetScore(seeded.TeamIds[0], 1)));
        var refused = await creator.ExpectAsync<Refused>();
        Assert.Equal(RefusalReason.WrongStatus, refused.Reason);
        Assert.Contains("Waiting for a player on Team 2", refused.Detail);
        await creator.SendAsync(Edit(new CreateTeam()));
        Assert.Equal(RefusalReason.WrongStatus, (await creator.ExpectAsync<Refused>()).Reason);   // everything but the empty team and its army is frozen
        Assert.Equal(0, db.EventCount(seeded.GameId));
    }

    [Fact]
    public async Task A_second_player_who_takes_the_empty_team_and_brings_an_army_starts_the_roll_off()
    {
        using var factory = Factory();
        var (seeded, creator) = await EndedSetupAlone(factory);
        await using var _ = creator;
        var late = await db.SeedAccountAsync("Late");
        await db.SeedParticipantAsync(seeded.GameId, late, null);                                // management's join: a row with no team
        var hers = await db.SeedArmyAsync(late);
        await using var second = await TestClient.OpenAsync(factory);
        await second.ConnectAsync(late, seeded.GameId);
        var board = (await second.ExpectAsync<BoardMessage>()).Board;

        await second.SendAsync(Edit(new PickTeam(seeded.TeamIds[0])));
        Assert.Equal(RefusalReason.WrongStatus, (await second.ExpectAsync<Refused>()).Reason);   // a held team stays held
        await second.SendAsync(Edit(new PickTeam(seeded.TeamIds[1])));
        var info = (await second.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal(seeded.TeamIds[1], info.Participants.Single(p => p.AccountId == late).TeamId);
        Assert.Equal("Team 2 has no army", Assert.Single(WaitingForPlayers.Teams(info, board)).Text);
        Assert.Equal(0, db.EventCount(seeded.GameId));

        await second.SendAsync(Edit(new PickArmy(hers.Id)));
        await creator.ExpectAsync<GameInfoMessage>();
        var joined = (await creator.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal(2, joined.Armies.Count);
        Assert.Equal(joined.Armies.OrderBy(a => a.ParticipantId).Select(a => a.ParticipantId), joined.Armies.Select(a => a.ParticipantId));
        Assert.Empty((await creator.ExpectAsync<EventsMessage>()).Events);
        var opened = new List<GameEvent>();
        while (opened.Count < 3) opened.AddRange((await creator.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], opened.Select(e => e.Kind));   // the roll-off, gated
        Assert.NotNull(db.GameRows.ParticipantsOf(seeded.GameId).Single(p => p.AccountId == late).ArmySnapshot);

        // A fresh process reads the late army from its row: the same board, the same log.
        using var again = Factory();
        await using var third = await TestClient.OpenAsync(again);
        await third.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        var rehydrated = (await third.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal(joined.Armies.Select(a => (a.ParticipantId, a.Snapshot.Id)), rehydrated.Armies.Select(a => (a.ParticipantId, a.Snapshot.Id)));
    }

    /// <summary>Owner ruling: "You need an army on your team." The creator placed Cid on team 1 in the lobby; he picked no army, and the team has
    /// hers, so ending setup opens deployment at once: Cid commands the team's units with her.</summary>
    [Fact]
    public async Task A_team_of_two_with_one_army_does_not_wait_and_the_lobby_names_each_army()
    {
        using var factory = Factory();
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 0);
        await db.SeedParticipantAsync(seeded.GameId, seeded.CreatorAccountId, seeded.TeamIds[0]);
        var cid = await db.SeedAccountAsync("Cid");
        var cidSeat = await db.SeedParticipantAsync(seeded.GameId, cid, null);
        var bob = await db.SeedAccountAsync("Bob");
        await db.SeedParticipantAsync(seeded.GameId, bob, seeded.TeamIds[1]);
        var (mine, bobs) = (await db.SeedArmyAsync(seeded.CreatorAccountId), await db.SeedArmyAsync(bob));
        await using var c = await TestClient.OpenAsync(factory);
        await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await using var b = await TestClient.OpenAsync(factory);
        await b.ConnectAsync(bob, seeded.GameId);
        await c.SendAsync(Edit(new PickTeam(seeded.TeamIds[0], cidSeat.ParticipantId)));        // the creator places Cid on her team
        await c.ExpectAsync<GameInfoMessage>();
        await c.SendAsync(Edit(new PickArmy(mine.Id))); await c.ExpectAsync<GameInfoMessage>();
        await b.SendAsync(Edit(new PickArmy(bobs.Id)));
        var lobby = (await c.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal([mine.Name, null], lobby.Participants.Where(p => p.TeamId == seeded.TeamIds[0]).OrderBy(p => p.AccountId == cid).Select(p => p.ArmyName));
        await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
        await c.SendAsync(Edit(new EndSetup()));
        var info = (await c.ExpectAsync<GameInfoMessage>()).Game;
        var board = (await c.ExpectAsync<BoardMessage>()).Board;
        Assert.Empty(WaitingForPlayers.Teams(info, board));
        Assert.Equal(2, board.Armies.Count);
        var opened = new List<GameEvent>();
        while (opened.Count < 3) opened.AddRange((await c.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], opened.Select(e => e.Kind));   // the roll-off
    }

    [Fact]
    public async Task The_creator_may_take_the_empty_team_too_and_play_both_sides()
    {
        using var factory = Factory();
        var (seeded, creator) = await EndedSetupAlone(factory);
        await using var _ = creator;
        var second = await db.SeedArmyAsync(seeded.CreatorAccountId);
        await creator.SendAsync(Edit(new PickTeam(seeded.TeamIds[1])));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal([seeded.TeamIds[0], seeded.TeamIds[1]], info.Participants.Where(p => p.AccountId == seeded.CreatorAccountId).Select(p => p.TeamId));
        await creator.SendAsync(Edit(new PickArmy(second.Id, seeded.TeamIds[1])));
        await creator.ExpectAsync<BoardMessage>();
        var opened = new List<GameEvent>();
        while (opened.Count < 3) opened.AddRange((await creator.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal("ask_opened", opened[2].Kind);
    }

    [Fact]
    public async Task One_account_on_both_teams_answers_for_each_and_deploys_both_armies()
    {
        using var factory = Factory();
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 0);
        var seat1 = await db.SeedParticipantAsync(seeded.GameId, seeded.CreatorAccountId, null);
        var (armyA, armyB) = (await db.SeedArmyAsync(seeded.CreatorAccountId), await db.SeedArmyAsync(seeded.CreatorAccountId));
        await using var c = await TestClient.OpenAsync(factory);
        await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await c.SendAsync(Edit(new PickTeam(seeded.TeamIds[0]))); await c.ExpectAsync<GameInfoMessage>();
        await c.SendAsync(Edit(new PickTeam(seeded.TeamIds[1])));
        var info = (await c.ExpectAsync<GameInfoMessage>()).Game;
        var seat2 = info.Participants.Single(p => p.TeamId == seeded.TeamIds[1]);
        Assert.Equal(seeded.CreatorAccountId, seat2.AccountId);
        Assert.Equal(seat1.ParticipantId, info.Participants.Single(p => p.TeamId == seeded.TeamIds[0]).Id);   // the account's first row took team 1; a new row holds team 2
        await c.SendAsync(Edit(new PickArmy(armyA.Id, seeded.TeamIds[0]))); await c.ExpectAsync<GameInfoMessage>();
        await c.SendAsync(Edit(new PickArmy(armyB.Id, seeded.TeamIds[1]))); await c.ExpectAsync<GameInfoMessage>();
        await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
        await c.SendAsync(Edit(new EndSetup())); await c.ExpectAsync<GameInfoMessage>();
        var board = (await c.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal(2, board.Armies.Count);

        var events = new List<GameEvent>();
        async Task<Ask> NextAsk(int atLeast)
        {
            while (events.Count < atLeast || events[^1].Payload is not AskOpened) events.AddRange((await c.ExpectAsync<EventsMessage>()).Events);
            return ((AskOpened)events[^1].Payload).Ask;
        }
        var gate = await NextAsk(3);
        Assert.Equal(2, gate.Teams.Count);
        await c.SendAsync(Cmd(new AnswerAsk(gate.Id, new Confirmed())));                         // no team named: the first of mine the Ask addresses
        var deploy1 = await NextAsk(events.Count + 1);
        Assert.Equal((AskQuestions.Deploy, seeded.TeamIds[0]), (deploy1.Question, deploy1.Teams.Single()));
        Assert.Equal(seeded.TeamIds[0], events.Select(e => e.Payload).OfType<AskAnswered>().First().TeamId);

        await c.SendAsync(Cmd(new AnswerAsk(deploy1.Id, new SelectedPieces([armyA.Units[0].Id]), seeded.TeamIds[1])));
        Assert.Equal(RefusalReason.NotPermitted, (await c.ExpectAsync<Refused>()).Reason);      // named team 2: this Ask is team 1's
        await c.SendAsync(Cmd(new AnswerAsk(deploy1.Id, new SelectedPieces([armyA.Units[0].Id]), seeded.TeamIds[0])));
        var mark = events.Count;
        while (events.Count < mark + 2) events.AddRange((await c.ExpectAsync<EventsMessage>()).Events);
        await c.SendAsync(Cmd(new MoveModels([new ModelRelocation(armyA.Units[0].Models[0].Id, new OnShelf(), new OnTable(new(new(12, 42, 0), new(180))))])));
        var deploy2 = await NextAsk(events.Count + 1);
        Assert.Equal(seeded.TeamIds[1], deploy2.Teams.Single());
        Assert.Equal(seat1.ParticipantId, events.Single(e => e.Payload is ModelsMoved).Actor);

        await c.SendAsync(Cmd(new AnswerAsk(deploy2.Id, new SelectedPieces([armyB.Units[0].Id]))));
        mark = events.Count;
        while (events.Count < mark + 2) events.AddRange((await c.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(seat2.Id, events[mark].Actor);                                              // team 2's answer is team 2's seat's
        await c.SendAsync(Cmd(new MoveModels([new ModelRelocation(armyB.Units[0].Models[0].Id, new OnShelf(), new OnTable(new(new(12, 6, 0), new(0))))])));
        mark = events.Count;
        while (events.Count <= mark || events[^1].Payload is not (AskOpened or TurnEnded or RoundStarted or PhaseStarted)) events.AddRange((await c.ExpectAsync<EventsMessage>()).Events);
        var moved = events.Skip(mark).First(e => e.Payload is ModelsMoved);
        Assert.Equal((seat2.Id, (string?)null), (moved.Actor, moved.Warning));                    // the hand move placed team 2's pick: legal, as team 2
    }
}
