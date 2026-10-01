# Task 10: The scheduler's factory, its registration, and the socket end to end

**Files:**
- Create: `server/game/Scheduler/SchedulerFactory.cs`
- Modify: `server/game/Program.cs` (replace the `NoSchedulerFactory` registration the interpreter plan's Task 10 left), `CLAUDE.md` (one Layout line)
- Test: `tests/Tabletop.GameServer.Tests/SchedulerFactoryTests.cs`, `tests/Tabletop.GameServer.Tests/SchedulerEndToEndTests.cs`; modify `tests/Tabletop.GameServer.Tests/Infrastructure/GameServerFactory.cs` (`WithoutScheduler()`), `tests/Tabletop.GameServer.Tests/SocketTests.cs` and `tests/Tabletop.GameServer.Tests/EndSetupTests.cs` (Step 3)

**Interfaces:**
- Consumes: the interpreter plan's `Engine.ISchedulerFactory`, `Engine.RulesEngine`, `Engine.RulesEngineFactory`, `Properties.IPropertiesStore`/`InMemoryPropertiesStore`, `Dice.IDiceSource`/`ScriptedDice`, `TestProperties.Core`; both shapes (Tasks 5, 7); test infrastructure `PostgresFixture`, `GameServerFactory`, `TestClient` (skeleton).
- Produces: `sealed class SchedulerFactory : ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }` — picks the shape from `game.SchedulerShape`. `Program.cs` registers it as `Engine.ISchedulerFactory` in place of `NoSchedulerFactory`; `IRulesEngineFactory` stays the interpreter plan's `RulesEngineFactory`. This plan defines no `RulesEngine` and no `IRulesEngine` implementation.

Spec: skeleton → Interfaces needed 2 ("one `IRulesEngine` implementation between them"), which is the interpreter plan's `Engine.RulesEngine` with the scheduler behind `Engine.IScheduler`; scheduler → The line (the scheduler is an engine component beside the interpreter); setup phase → The lifecycle (the flip: "the log opens with it as initial state, and the scheduler begins deployment"). `RulesEngine.Advance` runs the interpreter while a frame exists and the scheduler only when the stack is empty and no framed Ask is open — which is also how the harness ran throughout. The factory reads `GameInfo.SchedulerShape` once per game because scheduler → Setup configuration is fixed once playing settles that the shape cannot change after the flip; a group that wants the other shape starts a new game.

- [ ] **Step 1: Write the failing factory tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SchedulerFactoryTests
{
    [Fact]
    public void The_factory_picks_the_shape_from_the_game_and_the_engine_factory_composes_it()
    {
        var factory = new SchedulerFactory();
        var alternating = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        var playerTurn = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], shape: SchedulerShape.PlayerTurn);
        Assert.IsType<AlternatingActivationScheduler>(factory.Create(GrimdarkFutureRules.Instance, alternating.Info, TestProperties.Core));
        Assert.IsType<PlayerTurnScheduler>(factory.Create(GrimdarkFutureRules.Instance, playerTurn.Info, TestProperties.Core));
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var engines = new RulesEngineFactory(store, new ScriptedDice(6, 1), factory);
        var engine = Assert.IsType<RulesEngine>(engines.Create(GrimdarkFutureRules.Instance, alternating.Info with { PropertiesHash = TestProperties.Core.ContentHash }));
        var first = engine.Advance(alternating.State);
        Assert.IsType<SequenceStarted>(Assert.Single(first));                                            // the scheduler spoke through the engine: the roll-off begins
        Assert.Equal(GfNames.RollOffSeq, ((SequenceStarted)first[0]).Sequence);
    }
}
```

- [ ] **Step 2: Write `SchedulerFactory.cs`**

```csharp
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Two implementations behind one interface, selected by game.scheduler_shape (scheduler → The line). Registered as Engine.ISchedulerFactory; RulesEngineFactory calls it per game with the pinned set.</summary>
public sealed class SchedulerFactory : ISchedulerFactory
{
    public IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties) => game.SchedulerShape switch
    {
        SchedulerShape.AlternatingActivation => new AlternatingActivationScheduler(properties),
        SchedulerShape.PlayerTurn => new PlayerTurnScheduler(properties),
        _ => throw new NotSupportedException($"No scheduler for shape {game.SchedulerShape}."),
    };
}
```

- [ ] **Step 3: Register.** In `Program.cs` replace `builder.Services.AddSingleton<ISchedulerFactory, NoSchedulerFactory>();` (interpreter plan Task 10, Step 5) with `builder.Services.AddSingleton<ISchedulerFactory, SchedulerFactory>();`. The `IDiceSource`, `IPropertiesStore` and `IRulesEngineFactory` registrations are the interpreter plan's and do not change. `NoScheduler`/`NoSchedulerFactory` stay in the tree for tests that need no scheduler.

With the real scheduler registered, a hydrated `Playing` game whose log has no roll-off starts one on its first `Advance`. Five pre-existing tests seed such a game and assert on session mechanics, so they run on `GameServerFactory.WithoutScheduler()`: four in `SocketTests` and `EndSetupTests.A_fresh_process_hydrates_the_played_game_from_rows_and_log`. Add the factory method to `Infrastructure/GameServerFactory.cs`:

```csharp
    public static GameServerFactory WithoutScheduler() => new() { ConfigureTestServices = s => s.AddSingleton<Engine.ISchedulerFactory, Engine.NoSchedulerFactory>() };
```

- [ ] **Step 4: Write the end-to-end test**

```csharp
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
```

The creator's socket has an unread `GameInfo`, `Board` and the flip's eleven events when the other team's refused answer returns, so the test drains them before the creator's own `AnswerAsk`. The second team in the first test has an army but the second-process test gives it none; both are legal games and the second is skipped through deployment as a team with an empty legal set.

- [ ] **Step 5: `CLAUDE.md`** — under Layout, append one child bullet under the `server/game/` bullet, after the interpreter plan's children, editing nothing else: ``  - `Scheduler/` — the generic turn machinery behind `Engine.IScheduler` (two shapes, the turn as an Ask, the roll-off read back from the ruleset's sequence); its Grimdark Future hooks are the `GfRules.Scheduling.cs`/`GfRules.Deployment.cs` partials in `Rules/GrimdarkFuture/`, beside the interpreter's sequences.`` The ownership rule (contracts plan Task 16): a plan edits only the lines it owns.

- [ ] **Step 6: Run `dotnet test`; expect every project green (the two end-to-end tests need the Docker Postgres up: `docker compose up -d`). Commit: `Register the scheduler behind the rules engine's seam`.**
