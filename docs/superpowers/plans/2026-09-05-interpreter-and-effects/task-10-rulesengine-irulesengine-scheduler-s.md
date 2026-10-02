# Task 10: `RulesEngine` — the one `IRulesEngine`, and the scheduler's seam

**Files:**
- Create: `server/game/Engine/IScheduler.cs`, `server/game/Engine/RulesEngine.cs`, `server/game/Engine/RulesEngineFactory.cs`
- Modify: `server/game/Program.cs` (replace `InertEngineFactory`; register `IDiceSource`, `ISchedulerFactory`), `server/game/Session/ConnectionHandshake.cs` (the skeleton's hydrate catch gains `Engine.PropertiesNotFoundException`)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/Sim.cs`, `tests/Tabletop.GameServer.Tests/RulesEngineTests.cs` (this plan's; the scheduler plan's engine tests are `SchedulerFactoryTests.cs`)

**Interfaces:**
- Consumes: Tasks 4–9; the skeleton's `IRulesEngine`, `IRulesEngineFactory`, `GameState`, `Actor`, `EngineResult`, `Emit`, `RefuseCommand`, `Legality`, `CommandDispatcher`, `RerollDice`, `SetDice`, `AddModifier`, `DiceRerolled`, `DiceEdited`, `ModifierAdded`, `RollModifier`, `RefusalReason`.
- Produces (namespace `Tabletop.GameServer.Engine`): `interface IScheduler { IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter); }` (`ISequenceStarter` is Task 8's, in `Interpreter/Steps.cs`, implemented by `SequenceInterpreter`); `interface ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }` (the set, because the scheduler reads the `action` rows' names for the turn Ask and passes the set to the three hooks); `sealed class NoScheduler : IScheduler` (returns `[]`) and `sealed class NoSchedulerFactory : ISchedulerFactory` — the registration until the scheduler plan replaces it; `sealed class RulesEngine(IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler scheduler) : IRulesEngine` with `SequenceInterpreter Interpreter { get; }`; `sealed class RulesEngineFactory(IPropertiesStore store, IDiceSource dice, ISchedulerFactory schedulers) : IRulesEngineFactory` (loads the game's set by `game.PropertiesHash`, cached by hash; throws `PropertiesNotFoundException(string hash)`); test infrastructure `Sim` (below).
- **The seam, stated for the scheduler plan.** `RulesEngine.Advance(state)`: (1) if any **framed** Ask is open (`OpenAsks` with `Ask.FrameId` non-null — every Ask the interpreter opens is framed) → `[]`; (2) if the resolution stack is non-empty → `Interpreter.Advance(state)`; (3) otherwise → `scheduler.Advance(state, Interpreter)`. Unframed Asks are the scheduler's (the turn Ask, the deploy Asks, a round-boundary Ask) and the scheduler is still called while one is open: it returns `[]` for an Ask of its own that is live, and it is what closes — with an empty answer list — an Ask of its own that a menu edit (`set_active_team`, `set_round`) superseded, the one `ask_closed` the engine ever writes. So the scheduler runs only between sequences: it opens the turn Ask (`Ask.FrameId = null`, question `AskQuestions.Activate`), reads its `ask_closed` from the log on a later call, emits its own `ActionRecorded` and — when `rules.ActionSequence(action)` is non-null — `starter.Start(state, sequence, new SequenceContext(unitId, [], [], action))`, sees `sequence_finished` for that frame on a later call, calls `rules.EndOfTurnSequences(state, properties, turn)` and starts what it returns, and when those have finished emits `turn_ended` with `rules.OnTurnEnded(state, properties, turn)`'s changes; `round_started` carries `rules.OnRoundStarted(state, properties, round)`'s changes. The roll-off is a ruleset sequence the scheduler starts by the name `FirstTeamDecision.RollOffNeeded(sequence)` gives it and reads back from the `step_completed` named `GfNames.RollOffOrderStep` (`place_1..place_n`, Task 11); it spans batches, because the boundary before that step is a confirm gate to every team (Task 8). `Judge`/`Execute` are the interpreter's alone (the scheduler has no commands; a pass is an Ask answer). `RulesEngine.Judge`: one predicate, `DiceLegal`: `RerollDice`/`SetDice` → `Legal` when the frame is on the stack, the roll is in one of its completed steps, every index is a die of that record and none repeats, and (`SetDice`) every raw face is in `1..Convention.Sides` of the record's roll; `AddModifier` → `Legal` when the frame is on the stack and `properties.HasRoll(roll)`; any other command → `Illegal`. `Execute` asks `DiceLegal` again, because `warn` and `silent` run a command `Judge` called illegal, and refuses what it rejects. `Execute`: `RerollDice` → one `DiceRerolled` with the named dice (all when empty) redrawn under the record's own threshold and modifiers at the log's cursor; `SetDice` → `DiceEdited` with each die re-judged from its new raw under the record's threshold and modifiers; `AddModifier` → `ModifierAdded(frame, roll, new RollModifier("player", amount))`; otherwise `RefuseCommand(Unknown, …)`.
- A reroll or edit does not re-run the step: later steps derive counts from the roll record as it now stands (Task 12–13 read `RollOutcome.Successes` and the step's settled `Values`), which is what "rerolls stay commands between steps" means with the wait setting on.

Spec: skeleton Task 6 (the seam; `Advance` called after every appended batch until empty); scheduler → The line, The turn is an Ask, Events; action sequences → One roll primitive ("Rerolls and hand-added modifiers stay commands between steps"); resolution → Players roll their own dice ("A reroll records which dice it replaced. A manual reroll is that with the selection set to all"), Rerolling does not break undo (visible, logged with an actor).

- [ ] **Step 1: Write `Sim.cs`, the harness every sequence test uses**

```csharp
using Refused = Tabletop.GameServer.Dispatch.Refused;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A playing game driven the way the session drives it: dispatch a command, append, advance until the engine rests.</summary>
public sealed class Sim
{
    public GameState State { get; }
    public RulesEngine Engine { get; }
    public CommandDispatcher Dispatcher { get; }
    public List<EventPayload> Emitted { get; } = new();

    public Sim(GameState state, IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler? scheduler = null)
    {
        State = state;
        Engine = new RulesEngine(rules, properties, dice, scheduler ?? new NoScheduler());
        Dispatcher = new CommandDispatcher(Engine);
    }

    public static Sim Gf(GameState state, params int[] dice) => new(state, GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice.Length == 0 ? [4] : dice));

    public void Append(IEnumerable<EventPayload> events, Guid? actor = null)
    {
        foreach (var e in events) { State.Applier.Apply(new GameEvent(State.Projection.LastSeq + 1, actor, e, null)); Emitted.Add(e); }
    }

    /// <summary>Advance until the engine returns nothing (the session's AdvanceAsync loop).</summary>
    public IReadOnlyList<EventPayload> Run()
    {
        var from = Emitted.Count;
        for (var i = 0; i < 1000; i++) { var events = Engine.Advance(State); if (events.Count == 0) return Emitted.Skip(from).ToList(); Append(events); }
        throw new InvalidOperationException("the engine did not rest in 1000 steps");
    }

    public IReadOnlyList<EventPayload> Start(string sequence, SequenceContext context) { Append(Engine.Interpreter.Start(State, sequence, context)); return Run(); }

    /// <summary>The open Ask addressed to the team (exactly one expected).</summary>
    public Ask AskFor(Guid team) => State.Projection.OpenAsks.Values.Single(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team)).Ask;
    public Ask? AskForOrNull(Guid team) => State.Projection.OpenAsks.Values.SingleOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team))?.Ask;

    public DispatchOutcome Dispatch(Command command, Guid participant, Guid? team)
    {
        var outcome = Dispatcher.Dispatch(State, command, new Actor(participant, team));
        if (outcome is Applied a) Append(a.Events, participant);
        return outcome;
    }

    /// <summary>Answer the team's one open Ask, then run. Fails the test when the dispatcher refuses.</summary>
    public IReadOnlyList<EventPayload> Answer(Guid team, AskAnswer answer, Guid? participant = null)
    {
        var ask = AskFor(team);
        var outcome = Dispatch(new AnswerAsk(ask.Id, answer), participant ?? State.Info.Participants.First(p => p.TeamId == team).Id, team);
        if (outcome is Refused r) throw new Xunit.Sdk.XunitException($"refused: {r.Reason} {r.Detail}");
        return Run();
    }

    /// <summary>Answer every open step-boundary Confirm, whoever it is addressed to, until none is open; then the open Asks are the steps' own.</summary>
    public void Confirm()
    {
        for (var i = 0; i < 100; i++)
        {
            if (State.Projection.OpenAsks.Values.FirstOrDefault(a => a.Ask.Question == AskQuestions.Confirm) is not { } open) return;
            var team = open.Ask.Teams.First(t => !open.Answers.ContainsKey(t));
            Answer(team, new Confirmed());
        }
        throw new InvalidOperationException("the step-boundary Confirms did not stop");
    }

    public IReadOnlyList<StepCompleted> Steps(string name) => Emitted.OfType<StepCompleted>().Where(s => s.Step == name).ToList();
    public UnitInPlay Unit(Guid id) => State.Projection.Units[id];
    public ModelInPlay Model(Guid id) => State.Projection.Models[id];
    public int Alive(Guid unitId) => State.Projection.Models.Values.Count(m => m.UnitId == unitId && m.Location is OnTable);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Refused = Tabletop.GameServer.Dispatch.Refused;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RulesEngineTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Roller() => new("roller", [new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [new RollModifier("x", 1)], new RollConvention(6, null), null, new HashSet<int>()), 3); return new Complete([]); }),
        new LambdaStep("wait", ctx => ctx.History.LastClosed() is null ? new OpenAsks([ctx.Ask("go", "Go", ctx.ActorTeam!.Value, new Options([new AskOption("go", "Go", [])]))]) : new Complete([]))]);

    /// <summary>A scheduler that opens one Ask when the stack is empty and nothing has been asked yet.</summary>
    sealed class OneAsk(Ask ask) : IScheduler
    {
        public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) =>
            state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? [] : [new AskOpened(ask)];
    }

    static Sim Make(IScheduler? scheduler = null) => new(State([A], [B]), new StubRules(Roller()), TestProperties.Core, new ScriptedDice(2, 5, 6, 1, 3), scheduler);

    [Fact]
    public void Advance_runs_the_interpreter_while_frames_exist_and_the_scheduler_when_they_do_not()
    {
        var turn = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [Samples.Team1], new Options([new AskOption("a", "A", [])]), true, false, null, null);
        var sim = Make(new OneAsk(turn));
        Assert.Equal(["ask_opened"], sim.Run().Select(EventKinds.Of));                  // empty stack: the scheduler speaks
        Assert.Empty(sim.Run());                                                          // an open Ask blocks everything
        sim.Answer(Samples.Team1, new Passed());
        sim.Append(sim.Engine.Interpreter.Start(sim.State, "roller", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "ask_opened"], sim.Run().Select(EventKinds.Of));  // a frame: the interpreter speaks, the scheduler is silent
    }

    [Fact]
    public void Dice_commands_naming_a_missing_or_repeated_die_or_an_impossible_face_are_illegal()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        var clay = new Actor(Samples.P1, Samples.Team1);
        Legality Judge(Command c) => sim.Engine.Judge(sim.State, c, clay);

        Assert.Equal(Legality.Illegal, Judge(new RerollDice(frame, roll.Id, [roll.Dice.Count])));
        Assert.Equal(Legality.Illegal, Judge(new RerollDice(frame, roll.Id, [-1])));
        Assert.Equal(Legality.Illegal, Judge(new RerollDice(frame, roll.Id, [0, 0])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(roll.Dice.Count, 3)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(-1, 3)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 3), new DieValue(0, 4)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 0)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 7)])));
        Assert.Equal(Legality.Legal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 6), new DieValue(1, 1)])));
    }

    [Fact]
    public void Reroll_set_dice_and_add_modifier_are_judged_and_executed_against_the_frame()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal([3, 6, 7], roll.Dice.Select(d => d.Modified));
        var clay = new Actor(Samples.P1, Samples.Team1);

        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new RerollDice(frame, roll.Id, [0]), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new RerollDice(Guid.CreateVersion7(), roll.Id, []), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new AddModifier(frame, "save", 1), clay));
        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new AddModifier(frame, "defense", -1), clay));

        var rerolled = Assert.IsType<Applied>(sim.Dispatch(new RerollDice(frame, roll.Id, [0]), Samples.P1, Samples.Team1));
        var rr = Assert.IsType<DiceRerolled>(rerolled.Events.Single());
        Assert.Equal((0, 2, 1, 2, false), (rr.Replaced[0].Index, rr.Replaced[0].Old.Raw, rr.Replaced[0].New.Raw, rr.Replaced[0].New.Modified, rr.Replaced[0].New.Success));   // the 4th scripted die, +1, under threshold 4
        Assert.Equal(1, sim.State.Projection.Stack[0].Steps[0].Rolls[0].Dice[0].Raw);
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));

        var edited = Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(1, 3)]), Samples.P1, Samples.Team1));
        var ed = Assert.IsType<DiceEdited>(edited.Events.Single());
        Assert.Equal((5, 3, 4, true), (ed.Replaced[0].Old.Raw, ed.Replaced[0].New.Raw, ed.Replaced[0].New.Modified, ed.Replaced[0].New.Success));
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));                                  // an edit draws nothing

        var mod = Assert.IsType<Applied>(sim.Dispatch(new AddModifier(frame, "hit", 2), Samples.P1, Samples.Team1));
        Assert.Equal(new RollModifier("player", 2), Assert.IsType<ModifierAdded>(mod.Events.Single()).Modifier);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(sim.Dispatch(new RerollDice(frame, Guid.CreateVersion7(), []), Samples.P1, Samples.Team1)).Reason);
    }

    [Fact]
    public void Execute_refuses_a_modifier_on_a_roll_the_pinned_set_does_not_define()
    {
        // Warn and silent execute what Judge called illegal, so Execute asks the same question again.
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var clay = new Actor(Samples.P1, Samples.Team1);
        Assert.IsType<RefuseCommand>(sim.Engine.Execute(sim.State, new AddModifier(frame, "save", 1), clay));
        Assert.IsType<Emit>(sim.Engine.Execute(sim.State, new AddModifier(frame, "defense", -1), clay));
    }

    [Fact]
    public void The_factory_loads_the_pinned_set_and_refuses_an_unknown_hash()
    {
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var factory = new RulesEngineFactory(store, new DiceSource(), new NoSchedulerFactory());
        var game = Samples.Game with { PropertiesHash = TestProperties.Core.ContentHash };
        Assert.IsType<RulesEngine>(factory.Create(GrimdarkFutureRules.Instance, game));
        Assert.Throws<PropertiesNotFoundException>(() => factory.Create(GrimdarkFutureRules.Instance, Samples.Game with { PropertiesHash = "sha256:nope" }));
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `IScheduler.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>
/// The scheduler plan's seam. Called by RulesEngine.Advance only when no framed Ask is open and the resolution stack is empty; returns the
/// scheduler's next payloads — round_started, turn_started, the turn Ask (Ask.FrameId null), the action's sequence_started via the
/// starter, turn_ended — or nothing. It reads the log for its own Asks' ask_closed and for sequence_finished, returns nothing while an
/// Ask of its own is live, and closes one of its own Asks that a menu edit superseded (the engine's one ask_closed).
/// </summary>
public interface IScheduler { IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter); }

/// <summary>Per game: the ruleset, the game, and its pinned set (the scheduler reads the action rows' names and hands the set to the hooks).</summary>
public interface ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }

/// <summary>Until the scheduler plan lands: nothing drives turns, so sequences start only through tests or the scheduler's future registration.</summary>
public sealed class NoScheduler : IScheduler { public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) => []; }
public sealed class NoSchedulerFactory : ISchedulerFactory { public IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties) => new NoScheduler(); }
```

- [ ] **Step 4: Write `RulesEngine.cs` and `RulesEngineFactory.cs`; make `Interpreter` implement `ISequenceStarter`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>The one IRulesEngine. Dice commands are judged and executed here; Advance is the interpreter while frames exist and the scheduler when they do not.</summary>
public sealed class RulesEngine(IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler scheduler) : IRulesEngine
{
    public SequenceInterpreter Interpreter { get; } = new(rules, properties, dice);

    public Legality Judge(GameState state, Command command, Actor actor) => command switch
    {
        RerollDice or SetDice or AddModifier => DiceLegal(state, command) ? Legality.Legal : Legality.Illegal,
        _ => Legality.Illegal,
    };

    /// <summary>The one predicate for a dice command: the roll exists on the frame, every index is a die of it and none repeats, and every set face is on the roll's convention; a modifier names a frame on the stack and a roll the pinned set defines. Judge asks it, and Execute asks it again because warn and silent run a command Judge called illegal.</summary>
    bool DiceLegal(GameState state, Command command) => command switch
    {
        RerollDice r => Find(state, r.FrameId, r.RollId) is { } rec && DistinctInRange(r.DiceIndices, rec.Dice.Count),
        SetDice s => Find(state, s.FrameId, s.RollId) is { } rec && DistinctInRange(s.Dice.Select(d => d.Index).ToList(), rec.Dice.Count)
            && s.Dice.All(d => d.Raw >= 1 && d.Raw <= PlanOf(rec).Convention.Sides),
        AddModifier m => state.Projection.Stack.Any(f => f.Id == m.FrameId) && properties.HasRoll(m.Roll),
        _ => false,
    };

    public EngineResult Execute(GameState state, Command command, Actor actor)
    {
        switch (command)
        {
            case RerollDice r when DiceLegal(state, r) && Find(state, r.FrameId, r.RollId) is { } record:
            {
                var roller = new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state));
                return new Emit([new DiceRerolled(r.FrameId, r.RollId, RollOutcome.Reroll(PlanOf(record), record, r.DiceIndices, roller))]);
            }
            case SetDice s when DiceLegal(state, s) && Find(state, s.FrameId, s.RollId) is { } record:
            {
                var plan = PlanOf(record); var roller = new Roller(dice, state.RngSeed, 0);
                return new Emit([new DiceEdited(s.FrameId, s.RollId, s.Dice.Select(d => new DieReplacement(d.Index, record.Dice[d.Index], roller.Judge(plan, d.Raw))).ToList())]);
            }
            case AddModifier m when DiceLegal(state, m):
                return new Emit([new ModifierAdded(m.FrameId, m.Roll, new RollModifier("player", m.Amount))]);
            default:
                return new RefuseCommand(RefusalReason.Unknown, $"{command.GetType().Name}: no such frame, roll or die");
        }
    }

    /// <summary>A framed Ask blocks everything; an unframed one is the scheduler's to wait on or close.</summary>
    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        if (state.Projection.OpenAsks.Values.Any(a => a.Ask.FrameId is not null)) return [];
        if (state.Projection.Stack.Count > 0) return Interpreter.Advance(state);
        return scheduler.Advance(state, Interpreter);
    }

    static bool DistinctInRange(IReadOnlyList<int> indices, int count) => indices.All(i => i >= 0 && i < count) && indices.Distinct().Count() == indices.Count;

    static RollRecord? Find(GameState state, Guid frameId, Guid rollId) =>
        state.Projection.Stack.FirstOrDefault(f => f.Id == frameId)?.Steps.SelectMany(s => s.Rolls).FirstOrDefault(r => r.Id == rollId);

    /// <summary>A record re-judged under its own threshold and modifiers, in the ruleset's convention for that roll; RequiredRaw cannot be read back and is not re-applied.</summary>
    RollPlan PlanOf(RollRecord record) => new(record.Roll, record.Threshold, record.Modifiers, rules.RollSpec(record.Roll).Convention, null, new HashSet<int>());
}

public sealed class PropertiesNotFoundException(string hash) : Exception($"No Properties set with hash {hash} is loaded; the game cannot run.");

/// <summary>Replaces InertEngineFactory. The pinned set is loaded once per hash; Create is synchronous, so the load blocks at hydration.</summary>
public sealed class RulesEngineFactory(IPropertiesStore store, IDiceSource dice, ISchedulerFactory schedulers) : IRulesEngineFactory
{
    private readonly Dictionary<string, PropertySet> _cache = new();

    public IRulesEngine Create(IGameRules rules, GameInfo game)
    {
        PropertySet set;
        lock (_cache)
        {
            if (!_cache.TryGetValue(game.PropertiesHash, out set!))
                _cache[game.PropertiesHash] = set = store.LoadAsync(game.PropertiesHash, CancellationToken.None).GetAwaiter().GetResult() ?? throw new PropertiesNotFoundException(game.PropertiesHash);
        }
        return new RulesEngine(rules, set, dice, schedulers.Create(rules, game, set));
    }
}
```

In `server/game/Session/ConnectionHandshake.cs` (skeleton Task 8) widen the hydrate catch to `catch (Exception ex) when (ex is GameNotFoundException or GameRowsInconsistentException or Rules.UnknownGameRulesException or Engine.PropertiesNotFoundException)` — the socket closes with `1011 cannot hydrate this game` instead of the process faulting.

`SequenceInterpreter` already implements `ISequenceStarter` (Task 8 declared it; the interface is defined here). A record's `RequiredRaw` (fatigue's unmodified-6s rule) is not in `RollRecord`, so a player reroll of a fatigued hit die is judged as `Modified >= Threshold`; Task 11 sets the threshold to 6 and no modifiers for that case, so the two agree (`Die.Success` is `Raw + 0 >= 6`).

- [ ] **Step 5: Register in `Program.cs`** — replace `builder.Services.AddSingleton<IRulesEngineFactory, InertEngineFactory>();` with:

```csharp
builder.Services.AddSingleton<IDiceSource, DiceSource>();
builder.Services.AddSingleton<ISchedulerFactory, NoSchedulerFactory>();   // the scheduler plan replaces this registration
builder.Services.AddSingleton<IRulesEngineFactory, RulesEngineFactory>();
```

`Hydrator` (skeleton Task 7) calls `Engines.Create` after reading the log; the `ConnectionHandshake` catch above is what turns a `PropertiesNotFoundException` there into a `1011` close. `InertEngine`/`InertEngineFactory` stay in the tree for the skeleton's tests. The scheduler plan's Task 10 replaces the `NoSchedulerFactory` line with `SchedulerFactory` and nothing else here.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect every earlier test green plus 5 here (the skeleton's `EndSetupTests` still uses `FixedAskEngineFactory` through `ConfigureTestServices`, so it is unaffected). Commit: `Add the rules engine: dice commands, and Advance split between interpreter and scheduler`.**
