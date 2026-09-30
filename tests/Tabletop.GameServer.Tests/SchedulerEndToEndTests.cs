using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>The real process: Program.cs's registrations (the interpreter plan's RulesEngineFactory over the seeded properties row, this plan's SchedulerFactory), only the dice scripted so the roll-off is known. Deployment needs no action rows, so the bare properties row PostgresFixture seeds is enough.</summary>
[Collection("Postgres")]
public class SchedulerEndToEndTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static CommandMessage Cmd(Command c) => new(Guid.CreateVersion7(), c);

    [Fact]
    public async Task Ending_setup_rolls_off_opens_deployment_and_a_deploy_answer_plus_hand_moves_end_the_turn()
    {
        using var factory = new GameServerFactory { ConfigureTestServices = s => s.AddSingleton<IDiceSource>(new ScriptedDice(6, 1)) };   // team 1 rolls 6, team 2 rolls 1
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var armyA = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        var armyB = await pg.SeedArmyAsync(seeded.Participants[1].AccountId);
        await using var creator = await TestClient.OpenAsync(factory); await using var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId); await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await creator.SendAsync(Edit(new PickArmy(armyA.Id))); await creator.ExpectAsync<GameInfoMessage>(); await other.ExpectAsync<GameInfoMessage>();
        await other.SendAsync(Edit(new PickArmy(armyB.Id))); await creator.ExpectAsync<GameInfoMessage>(); await other.ExpectAsync<GameInfoMessage>();
        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>();
        await creator.SendAsync(Edit(new EndSetup()));
        await other.ExpectAsync<GameInfoMessage>(); var board = (await other.ExpectAsync<BoardMessage>()).Board;
        Assert.Empty((await other.ExpectAsync<EventsMessage>()).Events);

        var batches = new List<GameEvent>();
        while (batches.Count < 3) batches.AddRange((await other.ExpectAsync<EventsMessage>()).Events);         // the engine advanced after the flip and stopped at the roll-off's gate
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], batches.Select(e => e.Kind));
        Assert.All(batches, e => Assert.Null(e.Actor));                                                       // the engine acted
        var gate = ((AskOpened)batches[2].Payload).Ask;
        Assert.Equal((AskQuestions.Confirm, 2, false), (gate.Question, gate.Teams.Count, gate.Sealed));       // both teams may release it, and the first answer does
        Assert.NotNull(gate.FrameId);
        await other.SendAsync(Cmd(new AnswerAsk(gate.Id, new Confirmed())));                                  // the team that lost the roll-off releases it
        var released = new List<GameEvent>();
        while (released.Count < 8) released.AddRange((await other.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], released.Select(e => e.Kind));
        var ask = ((AskOpened)released[7].Payload).Ask;
        Assert.Equal((AskQuestions.Deploy, seeded.TeamIds[0]), (ask.Question, ask.Teams.Single()));           // the creator's team rolled 6
        var unit = Assert.Single(Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        Assert.Equal(11, await pg.CountAsync("select count(*) from game.game_event where game_id = $1", seeded.GameId));

        await other.SendAsync(Cmd(new AnswerAsk(ask.Id, new SelectedPieces([unit]))));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);                // not their team
        await creator.ExpectAsync<GameInfoMessage>(); await creator.ExpectAsync<BoardMessage>(); await creator.ExpectAsync<EventsMessage>();   // the creator's socket saw the flip and the same eleven events; drain them
        for (var seen = 0; seen < 11;) seen += (await creator.ExpectAsync<EventsMessage>()).Events.Count;
        await creator.SendAsync(Cmd(new AnswerAsk(ask.Id, new SelectedPieces([unit]))));
        var answered = (await creator.ExpectAsync<EventsMessage>()).Events;
        Assert.Equal(["ask_answered", "ask_closed"], answered.Select(e => e.Kind));
        var slot = board.Armies.Single(a => a.ParticipantId == seeded.Participants[0].ParticipantId).Snapshot.Units[0].Models[0];
        await creator.SendAsync(Cmd(new MoveModels([new ModelRelocation(slot.Id, new OnShelf(), new OnTable(new(new(12, 12, 0), new(0))))])));
        var after = new List<GameEvent>();
        while (after.Count < 4) after.AddRange((await creator.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(["models_moved", "turn_ended", "turn_started", "ask_opened"], after.Select(e => e.Kind));
        Assert.Equal(seeded.TeamIds[1], ((AskOpened)after[3].Payload).Ask.Teams.Single());
        Assert.Equal(seeded.Participants[0].ParticipantId, after[0].Actor);
    }

    [Fact]
    public async Task A_fresh_process_rehydrates_mid_deployment_and_re_serves_the_open_ask_without_adding_events()
    {
        GameServerFactory Factory() => new() { ConfigureTestServices = s => s.AddSingleton<IDiceSource>(new ScriptedDice(6, 1)) };
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        using (var first = Factory())
        {
            await using var c = await TestClient.OpenAsync(first);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.SendAsync(Edit(new PickArmy(army.Id))); await c.ExpectAsync<GameInfoMessage>();
            await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
            await c.SendAsync(Edit(new EndSetup())); await c.ExpectAsync<GameInfoMessage>(); await c.ExpectAsync<BoardMessage>();
            var opened = new List<GameEvent>();
            while (opened.Count < 3) opened.AddRange((await c.ExpectAsync<EventsMessage>()).Events);           // the roll-off's gate
            await c.SendAsync(Cmd(new AnswerAsk(((AskOpened)opened[2].Payload).Ask.Id, new Confirmed())));
            var got = 0; while (got < 8) got += (await c.ExpectAsync<EventsMessage>()).Events.Count;
        }
        var before = await pg.CountAsync("select count(*) from game.game_event where game_id = $1", seeded.GameId);
        using var second = Factory();
        await using var again = await TestClient.OpenAsync(second);
        await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await again.ExpectAsync<BoardMessage>();
        var replayed = new List<GameEvent>();
        while (replayed.Count < before) replayed.AddRange((await again.ExpectAsync<EventsMessage>()).Events);
        Assert.Equal(before, replayed.Count);
        Assert.IsType<AskOpened>(replayed[^1].Payload);
        await Task.Delay(200);
        Assert.Equal(before, await pg.CountAsync("select count(*) from game.game_event where game_id = $1", seeded.GameId));   // hydrate + Advance added nothing
    }
}
