# Task 5: `SchedulerBase` and the alternating-activation shape

**Files:**
- Create: `server/game/Scheduler/SchedulerBase.cs`, `server/game/Scheduler/AlternatingActivationScheduler.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (`Rounds` becomes `SchedulerBase.RoundsPhase`), `libs/Tabletop.Protocol/EventApplier.cs` (`RoundStarted` copies `Order` before clearing), `server/game/Dispatch/MenuTranslation.cs` (`SetRound` copies the projection's order)
- Test: `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/SchedulerHarness.cs`, `tests/Tabletop.GameServer.Tests/AlternatingSchedulerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; `CommandDispatcher`, `Applied`, `Refused`, `Actor` (skeleton); `RoundStarted`, `TurnStarted`, `TurnEnded`, `PhaseStarted`, `ActionRecordCleared` (Protocol); the interpreter plan's `Engine.IScheduler`, `Engine.RulesEngine`, `Interpreter.ISequenceStarter`, `Properties.PropertySet`, `Dice.IDiceSource`, `IGameRules.OnRoundStarted/OnTurnEnded`, and its test infrastructure `Sim`, `TestProperties.Core`, `LambdaStep`, `StubRules` (the shape `FakeRules` follows).
- Produces: `abstract class SchedulerBase(PropertySet properties) : Engine.IScheduler` with `const string RoundsPhase = "rounds"`, `IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter)`, protected `PropertySet Properties`, `TurnCycle Cycle`, `Guid? NextTeam(GameState, SchedulerLog, IReadOnlyList<Guid> order)`, `IReadOnlyList<EventPayload> EndTurn(GameState, TurnContext turn, bool passed)`, virtual `bool IsFinished(GameState, SchedulerLog, Guid teamId)` (passed this round, or empty legal set), virtual `IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState, int round)` and `ShapeTurnEndChanges(GameState, TurnContext)` (empty), virtual `IReadOnlyList<EventPayload> BeforeFirstAsk(GameState, SchedulerLog, Guid teamId)` (empty), `TurnAsk ShapeAsk(GameState, TurnAsk)` (identity), `IReadOnlyList<EventPayload> OnEmpty(GameState, SchedulerLog, TurnContext)` (ends the turn), abstract `IReadOnlyList<EventPayload> OnPassed(GameState, SchedulerLog, TurnContext)` and `OnComplete(GameState, SchedulerLog, TurnContext)`, and `static Ask EndRoundAsk(IReadOnlyList<Guid> teams)` with the private `bool RoundEnded(GameState, SchedulerLog, IReadOnlyList<Guid> teams, out IReadOnlyList<EventPayload> gate)` that opens it and reads its answer; `sealed class AlternatingActivationScheduler(PropertySet) : SchedulerBase`. Test infrastructure `FakeRules : IGameRules` — the whole merged interface: Grimdark Future's bodies by default, except `PhasesBeforeRounds`/`PhasesInTurn` (settable, default empty), the settable hooks `FirstTeamHook` (default: the teams as given), `LegalTurnHook`, `RoundBoundaryHook` (default `HookDone`), `EndOfTurnHook`, `OrderReactionsHook`, and `SequenceHook` (default: the roll-off is Grimdark Future's real sequence; every other name is an **empty** `Sequence`, which the interpreter finishes in one `Advance` with no `step_completed`, so the harness's kind lists hold), plus `static FakeRules Gf()` — the same with Grimdark Future's `PhasesBeforeRounds`, `FirstTeam` and `RoundBoundaryStep`, the harness's default; `QueueDice : IDiceSource` (`Queue<int> Dice`; dequeues per draw, then a seeded `Random`); and `SchedulerHarness(IReadOnlyList<Army?> armies, Func<PropertySet, IScheduler>? scheduler = null, Enforcement enforcement = Warn, bool waits = false, int spectators = 0, IGameRules? rules = null, SchedulerShape shape = AlternatingActivation)` — a `Sim` over the real `RulesEngine(rules, TestProperties.Core, QueueDice, scheduler)` — with `GameState State`, `Queue<int> Dice`, `IReadOnlyList<Guid> Teams`, `IReadOnlyList<Guid> Units(int team)`, `IReadOnlyList<EventPayload> Run()`, `IReadOnlyList<EventPayload> Dispatch(Command command, int? team = null)`, `IReadOnlyList<EventPayload> Answer(int team, AskAnswer answer)`, `IReadOnlyList<EventPayload> Deploy(Guid unitId)`, `Ask? OpenAsk(int team)`, `Ask Open`, `IReadOnlyList<EventPayload> EndRound(int team = 0)` (answers the boundary Ask with `Confirmed`), `IReadOnlyList<EventPayload> ConfirmRollOff(int team = 0)` (releases the roll-off's confirm gate, which every roll-off waits at), `void SkipDeployment()` (which releases any gate it meets), `void Rehydrate()`, `IReadOnlyList<string> Kinds(long afterSeq)`, `IReadOnlyList<GameEvent> Log`, `Projection P`. Its default rules are `FakeRules.Gf()` — Grimdark Future's scheduling hooks with every action sequence empty, so an activation's frame finishes in the same `Advance` it starts and the kind lists below hold.

Spec: scheduler → The two shapes (alternating: one activation per turn; the order from the first-team policy at `round_started`; turns rotate skipping finished teams; a team finishes when its legal set is empty or it passes; the round ends when every team has finished; the record clears at `round_started`), Deployment (one unit a turn, alternating; a team with nothing to deploy is skipped; the phase ends when every team is skipped — the spec describes the phase once, not per shape, so the alternation is the same under both), The round boundary (hooks after the order is set and before the first turn), Re-entering a round, Nothing is an invariant (`SetActiveTeam`'s `turn_started` opens an Ask; `SetRound`'s `round_started` runs the boundary), The final round is the ruleset's, Enforcement (`strict` refuses an out-of-set answer at the dispatcher, so the scheduler never sees it and the turn stands; `warn`/`silent` let it through and the scheduler acts as if it were legal — Task 4 covers that half, and an unreadable answer follows the same dial by the same route (ruling 3), proved here as a theory over the three positions); a scheduler Ask left open by a menu edit that started another turn or round closes with no answers (the one `ask_closed` the engine emits, and never for an answer). **The round does not advance on its own — a player ends it** (ruling 4): when no team can take another turn the scheduler opens the `end_round` Ask to every team that has a participant and waits, and the first answer is what makes `BeginRound(round + 1)` run. That is one rule for both the ordinary boundary and the degenerate one — a round nobody could act in waits at the same Ask, so rounds never count up on their own and the engine still rests. A menu edit while that Ask is open does not withdraw it (an Ask stands as logged); `set_active_team` supersedes it with a turn and `set_round` with a round, closing it with no answers, and if it was closed unanswered the next boundary opens a fresh one. Rotation restarts from `Order[0]` at a round or phase start (page 14: Scouts "starting with the player that activates next"), otherwise continues after the last `turn_ended`.

- [ ] **Step 1: Write `FakeRules.cs` — the merged `IGameRules`, Grimdark Future's bodies except where a test replaces a hook**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// The scheduler tests' IGameRules: Grimdark Future's members unless a hook is set. By default no phases before round one, the same order
/// every round, no boundary hook, and every action sequence EMPTY — the interpreter finishes it in the Advance that started it, with no
/// step_completed — except the roll-off, which is Grimdark Future's real sequence. Gf() keeps Grimdark Future's deployment and roll-off.
/// </summary>
public sealed class FakeRules : IGameRules
{
    readonly IGameRules _inner = GrimdarkFutureRules.Instance;
    public string Id => "fake";

    /// <summary>Grimdark Future's scheduling — phases, first team, Ambushers — with the empty action sequences: the harness's default.</summary>
    public static FakeRules Gf() => new()
    {
        PhasesBeforeRounds = GrimdarkFutureRules.Instance.PhasesBeforeRounds,
        FirstTeamHook = GrimdarkFutureRules.Instance.FirstTeam,
        RoundBoundaryHook = GrimdarkFutureRules.Instance.RoundBoundaryStep,
    };

    // The scheduler plan's members.
    public IReadOnlyList<string> PhasesBeforeRounds { get; set; } = [];
    public IReadOnlyList<string> PhasesInTurn { get; set; } = [];
    public Func<GameState, int, IReadOnlyList<Guid>, FirstTeamDecision>? FirstTeamHook { get; set; }
    public Func<GameState, PropertySet, Guid, TurnAsk?>? LegalTurnHook { get; set; }
    public Func<GameState, PropertySet, int, IReadOnlyList<Guid>, HookResult>? RoundBoundaryHook { get; set; }
    public Func<GameState, Guid?, IReadOnlyList<Guid>, IReadOnlyList<Guid>?>? OrderReactionsHook { get; set; }

    public FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => FirstTeamHook?.Invoke(state, round, teams) ?? new OrderedTeams(teams);
    public TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => LegalTurnHook is { } h ? h(state, properties, teamId) : _inner.LegalTurn(state, properties, teamId);
    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => _inner.ReadAnswer(state, teamId, answer);
    public bool ActivationComplete(GameState state, Activation activation) => _inner.ActivationComplete(state, activation);
    public HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => RoundBoundaryHook?.Invoke(state, properties, round, order) ?? new HookDone();
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams) => OrderReactionsHook?.Invoke(state, actorTeamId, reactingTeams);

    // The interpreter plan's members.
    public Func<string, Sequence>? SequenceHook { get; set; }
    public Func<GameState, PropertySet, TurnContext, IReadOnlyList<SequenceStart>>? EndOfTurnHook { get; set; }

    public Sequence Sequence(string name) => name == GfNames.RollOffSeq ? _inner.Sequence(name) : SequenceHook?.Invoke(name) ?? new Sequence(name, []);
    public string? ActionSequence(string actionName) => _inner.ActionSequence(actionName);
    public RollSpec RollSpec(string roll) => _inner.RollSpec(roll);
    public string HitRoll => _inner.HitRoll;
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => [];
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => EndOfTurnHook?.Invoke(state, properties, turn) ?? _inner.EndOfTurnSequences(state, properties, turn);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => _inner.OnRoundStarted(state, properties, round);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => _inner.OnTurnEnded(state, properties, turn);
}
```

`new Sequence(name, [])` is legal (the constructor's only check is that no two consecutive steps share a name) and the interpreter's `Advance` sees `Position >= Steps.Count` at once and emits `sequence_finished` — no `step_completed`, so the harness's kind lists read `sequence_started, sequence_finished`. The end-of-turn morale test asks for `morale-test`, which is also empty here; the harness tests below never reach it (no wounds are dealt).

- [ ] **Step 2: Write `SchedulerHarness.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Dice for the turn tests: the next queued face, then a seeded Random. The engine draws each cursor index once, in order, so a queue is enough; Rehydrate redraws nothing.</summary>
public sealed class QueueDice : IDiceSource
{
    private readonly Random _random = new(7);
    public Queue<int> Dice { get; } = new();
    public int Draw(ulong seed, long index, int sides) => Dice.Count > 0 ? Dice.Dequeue() : _random.Next(1, sides + 1);
}

/// <summary>
/// The session's loop without the session, over the real engine: a Sim (interpreter plan Task 10) holding RulesEngine(rules, TestProperties.Core,
/// QueueDice, scheduler). Run() advances until the engine rests — interpreter while a frame exists, scheduler between sequences, exactly as
/// RulesEngine.Advance does it. Answers go through the real dispatcher. Default rules: FakeRules.Gf(), so action sequences are empty and finish at once.
/// </summary>
public sealed class SchedulerHarness
{
    readonly TestGames.Built _game;
    readonly Func<PropertySet, IScheduler> _scheduler;
    readonly IGameRules _rules;
    Sim _sim;

    public SchedulerHarness(IReadOnlyList<Army?> armies, Func<PropertySet, IScheduler>? scheduler = null, Enforcement enforcement = Enforcement.Warn,
        bool waits = false, int spectators = 0, IGameRules? rules = null, SchedulerShape shape = SchedulerShape.AlternatingActivation)
    {
        _rules = rules ?? FakeRules.Gf();
        _game = TestGames.Build(armies, shape, enforcement, waits, spectators, _rules);
        _scheduler = scheduler ?? (p => new AlternatingActivationScheduler(p));
        _sim = new Sim(_game.State, _rules, TestProperties.Core, Source, _scheduler(TestProperties.Core));
    }

    public GameState State => _sim.State;
    public QueueDice Source { get; } = new();
    public Queue<int> Dice => Source.Dice;
    public IReadOnlyList<Guid> Teams => _game.Teams;
    public IReadOnlyList<GameEvent> Log => State.Applier.Log;
    public Projection P => State.Projection;
    public IReadOnlyList<Guid> Units(int team) => TeamUnits.Of(State, Teams[team]).Select(u => u.Id).ToList();
    public Ask? OpenAsk(int team) => P.OpenAsks.Values.Select(a => a.Ask).FirstOrDefault(a => a.Teams.Contains(Teams[team]));
    public Ask Open => P.OpenAsks.Values.Select(a => a.Ask).Single();
    public IReadOnlyList<string> Kinds(long afterSeq) => Log.Where(e => e.Seq > afterSeq).Select(e => e.Kind).ToList();

    public void Append(IReadOnlyList<EventPayload> events, Guid? actor = null) => _sim.Append(events, actor);

    /// <summary>Until the engine rests: RulesEngine.Advance — interpreter while a frame exists, scheduler when the stack is empty and no framed Ask is open.</summary>
    public IReadOnlyList<EventPayload> Run() => _sim.Run();

    public IReadOnlyList<EventPayload> Dispatch(Command command, int? team = null)
    {
        var participant = team is { } t ? _game.Participants[t] : _game.Participants[^1];
        var outcome = _sim.Dispatch(command, participant, team is { } tt ? Teams[tt] : TeamUnits.TeamOf(State, participant));   // Dispatch.Applied / Dispatch.Refused: qualified, because Tabletop.Protocol has a Refused of its own
        if (outcome is Dispatch.Refused r) throw new InvalidOperationException($"{r.Reason}: {r.Detail}");
        return [.. ((Dispatch.Applied)outcome).Events, .. Run()];
    }

    public IReadOnlyList<EventPayload> Answer(int team, AskAnswer answer) => Dispatch(new AnswerAsk(OpenAsk(team)!.Id, answer), team);

    /// <summary>Ends the round: the boundary Ask is addressed to every team that has a participant, and any one of them may confirm it.</summary>
    public IReadOnlyList<EventPayload> EndRound(int team = 0) => Answer(team, new Confirmed());

    /// <summary>Releases the roll-off's confirm gate. The interpreter gates an actor-less root frame past position 0 whatever the wait setting says, with one unsealed Confirm to every team, so every roll-off waits here and any team may release it (interpreter plan Task 8). Its dice are editable until it is answered.</summary>
    public IReadOnlyList<EventPayload> ConfirmRollOff(int team = 0) => Answer(team, new Confirmed());

    public IReadOnlyList<EventPayload> Deploy(Guid unitId)
    {
        var team = Teams.ToList().IndexOf(TeamUnits.TeamOf(State, P.Units[unitId].ParticipantId)!.Value);
        return Dispatch(TestGames.Move(State, unitId), team);
    }

    /// <summary>Answers every deploy Ask with its first eligible unit and places it, until round one starts — releasing any roll-off gate it meets on the way, since deployment's roll-off and a free-for-all's round-one roll-off both wait at one.</summary>
    public void SkipDeployment()
    {
        Run();
        for (var i = 0; i < 200 && P.Scheduler.Round == 0; i++)
        {
            var ask = Open;
            if (ask.FrameId is not null) { ConfirmRollOff(); continue; }          // the interpreter's gate, not a deploy Ask
            var unit = ((PieceSelection)ask.Legal).Eligible[0];
            Answer(Teams.ToList().IndexOf(ask.Teams[0]), new SelectedPieces([unit]));
            Deploy(unit);
        }
    }

    /// <summary>A fresh applier over the same board and log, and a fresh engine: the one hydrate path. Run() afterwards must add nothing (and draws no dice: nothing is re-rolled).</summary>
    public void Rehydrate()
    {
        var applier = new EventApplier(State.Board, State.Info);
        applier.ApplyAll(State.Applier.Log);
        _sim = new Sim(new GameState(State.Info, State.Board, applier, State.Rules), _rules, TestProperties.Core, Source, _scheduler(TestProperties.Core));
    }
}
```

`Sim`'s constructor takes `(GameState, IGameRules, PropertySet, IDiceSource, IScheduler?)` and its `Dispatch(Command, Guid participant, Guid? team)` returns the `DispatchOutcome` — both are the interpreter plan's Task 10 shapes, used as they are.

- [ ] **Step 3: Write the failing tests `AlternatingSchedulerTests.cs`**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class AlternatingSchedulerTests
{
    static SchedulerHarness TwoTeams(int unitsA = 2, int unitsB = 1) => new([TestArmies.Build("A", unitsA, 2), TestArmies.Build("B", unitsB, 2)]);

    [Fact]
    public void A_fresh_game_rolls_off_then_alternates_deployment_skipping_a_team_with_nothing_left_then_starts_round_one()
    {
        var h = TwoTeams(); h.Dice.Enqueue(1); h.Dice.Enqueue(6);
        h.Run();
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], h.Kinds(0));                   // the roll-off waits at its confirm gate
        Assert.Equal((AskQuestions.Confirm, 0, (Guid?)null), (h.Open.Question, h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        var gate = h.P.LastSeq; h.ConfirmRollOff(team: 1);
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(gate));
        Assert.Equal((0, "deployment", h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));    // B rolled 6
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);
        var b = h.Units(1)[0]; var a = h.Units(0);
        h.Answer(1, new SelectedPieces([b]));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                              // waiting for the models
        var seq = h.P.LastSeq; h.Deploy(b);
        Assert.Equal(["models_moved", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                   // B has nothing left: skipped
        h.Answer(0, new SelectedPieces([a[1]])); seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_ended", "phase_started", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((1, SchedulerBase.RoundsPhase, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));  // the winner takes round one's first turn
        Assert.Equal(AskQuestions.Activate, h.Open.Question);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Turns_alternate_one_activation_each_the_round_ends_when_everyone_is_finished_and_records_clear_at_the_next_round()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[0], "Advance")));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((a[0], "Advance", "advance"), h.Log.Select(e => e.Payload).OfType<SequenceStarted>().Select(s => (s.Context.ActorUnitId, s.Context.Action, s.Sequence)).Single(s => s.Action is not null));   // the roll-off is the other frame
        Assert.Contains("Advance", h.P.Units[a[0]].Actions);
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Single(Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());   // only a[1] remains
        seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal((AskQuestions.EndRound, false, 1), (h.Open.Question, h.Open.AllowPass, h.P.Scheduler.Round));   // the round waits to be ended
        Assert.Equal(h.Teams, h.Open.Teams);                                                               // every team that has a participant; any one of them may answer
        Assert.Empty(h.Run());
        seq = h.P.LastSeq; h.EndRound(team: 1);                                                            // the team that did not finish first ends it: any player, any team
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var round2 = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal((2, 3), (round2.Round, round2.Changes.Count(c => c is ActionRecordCleared)));
        Assert.Equal([h.Teams[1], h.Teams[0]], round2.Order);                                              // B finished first
        Assert.All(h.P.Units.Values, u => Assert.Empty(u.Actions));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new Undo(seq));                                                                 // undo across the boundary: the answer is a logged fact, so replay un-ends the round
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));
        Assert.All(h.P.Units.Values, u => Assert.NotEmpty(u.Actions));                             // and round two's record clearing goes with it
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void A_pass_stands_for_the_round_and_a_cleared_record_re_enters_a_team_that_did_not_pass()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 3, 2), TestArmies.Build("B", 2, 2)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        var seq = h.P.LastSeq; h.Answer(1, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.True(((TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload).Passed);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(b[0]));                                                          // a menu edit while B has passed
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId); Assert.Single(h.P.OpenAsks);
        h.Answer(0, new Chosen(TestGames.Activate(a[2])));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                 // B was not re-asked: the pass stood, and the round waits
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);                                      // B finished (passed) before A's last turn
        h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        h.Answer(1, new Chosen(TestGames.Activate(b[1]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // B's legal set is empty: A goes again
        h.Dispatch(new ClearActionRecord(b[0]));
        h.Answer(0, new Chosen(TestGames.Activate(a[2])));
        Assert.Equal((2, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // B re-entered at its slot
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Hand_moves_never_end_a_turn_and_undo_reopens_what_it_undid()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0);
        var seq = h.P.LastSeq;
        h.Dispatch(TestGames.Move(h.State, a[0]), team: 0);
        Assert.Equal(["models_moved"], h.Kinds(seq));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var before = h.P.LastSeq;
        h.Answer(0, new Passed());
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new Undo(before));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal(AskQuestions.Activate, h.Open.Question); Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        Assert.DoesNotContain(h.Teams[0], h.P.Scheduler.Passed);
    }

    [Fact]
    public void Set_active_team_and_set_round_from_the_menu_are_followed()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        h.Dispatch(new SetActiveTeam(h.Teams[1]));
        Assert.Equal(h.Teams[1], h.Open.Teams.Single());                                                  // a fresh Ask for the team the menu named
        Assert.Single(h.P.OpenAsks);
        h.Dispatch(new SetRound(4));
        Assert.Equal((4, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                  // the boundary hook ran (nothing to do) and Order[0] took the first turn
        Assert.Single(h.P.OpenAsks);                                                                       // B's superseded Ask closed
    }

    [Fact]
    public void Under_strict_an_out_of_set_answer_is_refused_and_the_turn_stands()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 1, 2)], enforcement: Enforcement.Strict);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var ask = h.Open; var seq = h.P.LastSeq;
        Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen(TestGames.Activate(a[0], "Teleport"))));   // OutsideLegalSet under strict: refused by the dispatcher
        Assert.Equal(seq, h.P.LastSeq);                                                                        // nothing logged, so the scheduler never saw it
        Assert.Equal((ask.Id, h.Teams[0]), (h.Open.Id, h.P.Scheduler.ActiveTeamId!.Value));                     // the same Ask stands
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
    }

    /// <summary>
    /// An answer the ruleset cannot read takes the same three positions as an out-of-set one (ruling 3), because it is
    /// decided in the same place. The id here is both — which is what a real game produces, since a legal set only ever offers well-formed
    /// ids for units the log has. What the readability clause adds on its own is a legal set offering an id its own ruleset cannot read.
    /// </summary>
    [Theory]
    [InlineData(Enforcement.Strict, false)] [InlineData(Enforcement.Warn, true)] [InlineData(Enforcement.Silent, true)]
    public void An_unreadable_answer_follows_the_enforcement_dial(Enforcement enforcement, bool applied)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 1, 2)], enforcement: enforcement);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var ask = h.Open; var seq = h.P.LastSeq;
        if (!applied)
        {
            Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen("not-an-option")));
            Assert.Equal(seq, h.P.LastSeq);                                                                    // refused before anything was written
            Assert.Equal((ask.Id, h.Teams[0]), (h.Open.Id, h.P.Scheduler.ActiveTeamId!.Value));                 // the same Ask stands and the turn is still A's: nothing is waiting on it
            Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen("not-an-option")));           // and again, as often as the client likes
            h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));                                                   // a readable answer moves the game on
            Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
            return;
        }
        h.Answer(0, new Chosen("not-an-option"));
        Assert.True(((AskAnswered)h.Log.First(e => e.Seq > seq && e.Payload is AskAnswered).Payload).OutsideLegalSet);   // the warn warning rides on this; the string itself is the dispatcher's, pinned by CommandDispatcherTests
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.Equal((h.Teams[0], true), (ended.TeamId, ended.Passed));                                        // read as a pass, which stands for the round
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                      // A passed and B is done: the round waits for a player, not for A's turn
    }

    [Fact]
    public void Teams_with_nothing_are_skipped_spectators_are_never_asked_and_a_round_nobody_can_act_in_waits_to_be_ended()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), null], spectators: 1);
        h.Dice.Enqueue(3); h.Dice.Enqueue(5);
        h.Run(); h.ConfirmRollOff();
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                  // B won the roll-off but has nothing to deploy
        h.SkipDeployment();
        var a0 = h.Units(0)[0];
        h.Answer(0, new Chosen(TestGames.Activate(a0)));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                  // B never had a turn, so it is not asked to act — but it is asked to end the round
        h.EndRound(team: 1);
        Assert.Equal(2, h.P.Scheduler.Round);
        h.Dispatch(new RecordAction(a0, "Hold"));                                                          // marked from the menu: the open Ask stands
        Assert.Empty(h.Run()); Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new MoveModels(h.P.Units[a0].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);
        h.Answer(0, new Passed());
        h.EndRound();
        Assert.Equal((3, (Guid?)null, AskQuestions.EndRound), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // round 3 began; nobody can act, so it waits at its own boundary
        Assert.Empty(h.Run());
        var asked = h.Log.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask).ToList();
        Assert.All(asked.Where(a => a.FrameId is null && a.Question != AskQuestions.EndRound), a => Assert.Equal([h.Teams[0]], a.Teams));   // only A is ever asked to deploy or activate
        Assert.All(asked.Where(a => a.FrameId is null && a.Question == AskQuestions.EndRound), a => Assert.Equal(h.Teams, a.Teams));        // both teams may end a round; the spectator has no team and is in no Ask
        var empty = new SchedulerHarness([null, null]); empty.Run(); empty.ConfirmRollOff();
        Assert.Equal((1, AskQuestions.EndRound), (empty.P.Scheduler.Round, empty.Open.Question));          // round one began and stopped at its boundary
        Assert.Empty(empty.Run());
        empty.EndRound();
        Assert.Equal(2, empty.P.Scheduler.Round);                                                          // rounds count up only when a player says so
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter AlternatingScheduler`; expect compile errors — there is no scheduler to hand the harness. Write `SchedulerBase.cs`**

```csharp
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// The shared shape of both schedulers: the phases before round one (alternating one unit a turn), the round boundary (a player's
/// end_round answer, then the first-team policy, the roll-off, round_started with the hooks' changes, and the interactive boundary
/// hook), the rotation over teams, and one activation cycle per open turn. Shapes decide what a completed or passed cycle does from
/// round one on, and what they clear.
/// Implements the interpreter plan's Engine.IScheduler: called with no framed Ask open and an empty stack; starts sequences only
/// through the starter it is handed; holds the game's pinned set for the action rows and the three IGameRules hooks.
/// </summary>
public abstract class SchedulerBase(PropertySet properties) : IScheduler
{
    public const string RoundsPhase = "rounds";

    protected PropertySet Properties { get; } = properties;
    protected TurnCycle Cycle { get; } = new(properties);

    public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter)
    {
        var log = SchedulerLog.Of(state);
        var s = state.Projection.Scheduler;
        var teams = state.Info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList();          // frozen at the flip to playing: the same list orders every round of the game
        if (teams.Count == 0) return [];

        var stale = StaleAsks(state, log);
        if (stale.Count > 0) return stale;
        if (state.Projection.OpenAsks.Count > 0) return [];                                       // an unframed Ask of ours (or a boundary hook's) is live: wait for its answer
        if (s.Round >= 1 && s.Phase is { } phase && state.Rules.PhasesBeforeRounds.Contains(phase)) return [new PhaseStarted(RoundsPhase)];   // set_round from the menu mid-deployment: the pre-round phases are over
        if (s.ActiveTeamId is { } active) return InTurn(state, log, active, starter);
        if (log.RoundStartedAt == 0) return BeginRound(state, log, teams, state.Rules.PhasesBeforeRounds.Count > 0 ? 0 : 1, starter);

        if (s.Round >= 1 && log.SinceRound.All(e => e.Payload is not TurnStarted))
        {
            switch (state.Rules.RoundBoundaryStep(state, Properties, s.Round, s.Order))
            {
                case HookWaiting: return [];
                case HookEmit h: return h.Events;
                case HookStartSequences seqs: return seqs.Sequences.SelectMany(r => starter.Start(state, r.Sequence, r.Context)).ToList();
            }
        }

        if (NextTeam(state, log, s.Order) is { } next) return [new TurnStarted(next)];

        if (s.Round == 0)
        {
            var phases = state.Rules.PhasesBeforeRounds;
            var index = phases.ToList().IndexOf(s.Phase ?? "");
            return index + 1 < phases.Count ? [new PhaseStarted(phases[index + 1])] : BeginRound(state, log, teams, 1, starter);
        }
        // Round one on: the round is over, and it does not advance on its own — a player ends it.
        if (!RoundEnded(state, log, teams, out var gate)) return gate;
        return BeginRound(state, log, teams, s.Round + 1, starter);
    }

    /// <summary>Unframed, unsealed, no pass: the round boundary's Ask. Unsealed means the dispatcher closes it on the first answer, which is how any player on any team ends the round.</summary>
    public static Ask EndRoundAsk(IReadOnlyList<Guid> teams) =>
        new(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", teams, new Confirmation(), AllowPass: false, Sealed: false, WindowKind: null, FrameId: null);

    /// <summary>
    /// Has a player ended this round? True with an empty gate once the boundary Ask has closed with an answer; false with the Ask to
    /// open (or nothing to emit, when no team has a participant to ask). Read from the log since round_started, so undo-by-replay and
    /// a rehydrated server reach the same answer, and a round that already advanced is never re-asked.
    /// </summary>
    private bool RoundEnded(GameState state, SchedulerLog log, IReadOnlyList<Guid> teams, out IReadOnlyList<EventPayload> gate)
    {
        var opened = log.SinceRound.LastOrDefault(e => e.Payload is AskOpened o && o.Ask.Question == AskQuestions.EndRound);
        if (opened is { } o && log.SinceRound.Any(e => e.Seq > o.Seq && e.Payload is AskClosed c
            && c.AskId == ((AskOpened)o.Payload).Ask.Id && c.Answers.Count > 0))
        {
            gate = []; return true;                                                              // answered: BeginRound follows, with the hooks' changes on its round_started
        }
        var asked = teams.Where(t => state.Info.Participants.Any(p => p.TeamId == t)).ToList();   // a participant with no team is never addressed
        gate = asked.Count == 0 ? [] : [new AskOpened(EndRoundAsk(asked))];                        // nobody to ask: wait, as an empty legal set waits
        return false;
    }

    /// <summary>A scheduler Ask opened before the current turn or round began was superseded by a menu edit (set_active_team, set_round); it closes with no answers.</summary>
    private static IReadOnlyList<EventPayload> StaleAsks(GameState state, SchedulerLog log)
    {
        var cutoff = Math.Max(log.TurnStartedAt, log.RoundStartedAt);
        return log.Effective
            .Where(e => e.Seq < cutoff && e.Payload is AskOpened o && SchedulerLog.IsSchedulerAsk(o.Ask) && state.Projection.OpenAsks.ContainsKey(o.Ask.Id))
            .Select(e => (EventPayload)new AskClosed(((AskOpened)e.Payload).Ask.Id, [])).ToList();
    }

    /// <summary>The order from the first-team policy — a roll-off started through the interpreter and read back once finished (Task 3) — then round_started with the shape's and the ruleset's changes.</summary>
    private IReadOnlyList<EventPayload> BeginRound(GameState state, SchedulerLog log, IReadOnlyList<Guid> teams, int round, ISequenceStarter starter)
    {
        IReadOnlyList<Guid> order;
        switch (state.Rules.FirstTeam(state, round, teams))
        {
            case OrderedTeams o: order = o.Order.ToList(); break;                                  // a copy: a hook may hand back Projection.Scheduler.Order itself, which round_started's own apply clears first
            case RollOffNeeded r:
                switch (RollOff.Advance(state, log, r.Sequence, starter))
                {
                    case RollOff.Emit e: return e.Events;
                    case RollOff.Waiting: return [];
                    case RollOff.Resolved res: order = res.Order; break;
                    default: throw new NotSupportedException();
                }
                break;
            default: throw new NotSupportedException();
        }
        var changes = new List<StateChange>(ShapeRoundStartChanges(state, round));
        changes.AddRange(state.Rules.OnRoundStarted(state, Properties, round));
        var events = new List<EventPayload> { new RoundStarted(round, order, changes) };
        if (round == 0) events.Add(new PhaseStarted(state.Rules.PhasesBeforeRounds[0]));
        else if (round == 1) events.Add(new PhaseStarted(RoundsPhase));
        return events;
    }

    private IReadOnlyList<EventPayload> InTurn(GameState state, SchedulerLog log, Guid team, ISequenceStarter starter)
    {
        var beforeRounds = state.Projection.Scheduler.Round == 0;
        if (!beforeRounds && Cycle.Read(state, team, log) is NoAsk && BeforeFirstAsk(state, log, team) is { Count: > 0 } opening) return opening;
        switch (Cycle.Step(state, team, log, starter, beforeRounds ? null : ask => ShapeAsk(state, ask)))
        {
            case CycleEmit e: return e.Events;
            case CycleWaiting: return [];
            case CycleEmpty:
                var empty = new TurnContext(team, null, log.TurnStartedAt, log.TurnStartedAt);
                return beforeRounds ? EndTurn(state, empty, passed: false) : OnEmpty(state, log, empty);
            case CyclePassed p: return beforeRounds ? EndTurn(state, p.Turn, passed: true) : OnPassed(state, log, p.Turn);
            case CycleComplete c: return beforeRounds ? EndTurn(state, c.Turn, passed: false) : OnComplete(state, log, c.Turn);
            default: throw new NotSupportedException();
        }
    }

    /// <summary>Order[0] after a round or phase start; otherwise the team after the last turn_ended; finished teams skipped; null when every team is finished.</summary>
    protected Guid? NextTeam(GameState state, SchedulerLog log, IReadOnlyList<Guid> order)
    {
        if (order.Count == 0) return null;
        var last = log.LastTurnEndedTeam(log.SinceRotation);
        var start = last is null ? 0 : (TeamUnits.IndexOf(order, last.Value) + 1) % order.Count;
        for (var i = 0; i < order.Count; i++)
        {
            var team = order[(start + i) % order.Count];
            if (!IsFinished(state, log, team)) return team;
        }
        return null;
    }

    /// <summary>Passed this round, or nothing legal to do — read again every time (scheduler → Re-entering a round).</summary>
    protected virtual bool IsFinished(GameState state, SchedulerLog log, Guid teamId) =>
        state.Projection.Scheduler.Passed.Contains(teamId) || state.Rules.LegalTurn(state, Properties, teamId) is null;

    protected IReadOnlyList<EventPayload> EndTurn(GameState state, TurnContext turn, bool passed)
    {
        var changes = new List<StateChange>(ShapeTurnEndChanges(state, turn));
        changes.AddRange(state.Rules.OnTurnEnded(state, Properties, turn));
        return [new TurnEnded(turn.TeamId, passed, changes)];
    }

    /// <summary>ActionRecordCleared for every unit that has an action on record: what a shape clears at a round start or a turn end.</summary>
    protected static IReadOnlyList<StateChange> ClearRecords(IEnumerable<UnitInPlay> units) =>
        units.Where(u => u.Actions.Count > 0).Select(u => (StateChange)new ActionRecordCleared(u.Id)).ToList();

    protected virtual IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState state, int round) => [];
    protected virtual IReadOnlyList<StateChange> ShapeTurnEndChanges(GameState state, TurnContext turn) => [];
    /// <summary>Events a shape emits when a turn (round one on) has opened and no Ask exists yet — the player-turn shape's first phase_started.</summary>
    protected virtual IReadOnlyList<EventPayload> BeforeFirstAsk(GameState state, SchedulerLog log, Guid teamId) => [];
    /// <summary>The shape's last word on a turn Ask from round one on.</summary>
    protected virtual TurnAsk ShapeAsk(GameState state, TurnAsk ask) => ask;
    /// <summary>A turn opened (by rotation or the menu) on a team with nothing legal: the turn ends unactivated.</summary>
    protected virtual IReadOnlyList<EventPayload> OnEmpty(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: false);
    protected abstract IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn);
    protected abstract IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn);
}
```

- [ ] **Step 5: Write `AlternatingActivationScheduler.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>One activation per turn; the record clears at round_started (scheduler → The two shapes; game flow → Two schedulers).</summary>
public sealed class AlternatingActivationScheduler(PropertySet properties) : SchedulerBase(properties)
{
    protected override IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: true);
    protected override IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: false);

    protected override IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState state, int round) =>
        round == 0 ? [] : ClearRecords(state.Projection.Units.Values);
}
```

- [ ] **Step 6: Make a `round_started` order survive its own apply, and point `GfNames.Rounds` at the constant**

`EventApplier` clears the projection's order before it fills it, so a `RoundStarted.Order` that is `Projection.Scheduler.Order` itself empties on apply and on replay. In `libs/Tabletop.Protocol/EventApplier.cs` the `RoundStarted` case copies first, and `MenuTranslation`'s `SetRound` passes a copy (`SchedulerBase.BeginRound` already does, in the block above):

```csharp
            case RoundStarted r:
                p.Scheduler.Round = r.Round; var order = r.Order.ToList(); p.Scheduler.OrderMut.Clear(); p.Scheduler.OrderMut.AddRange(order);
```

```csharp
            SetRound r => new RoundStarted(r.Round, p.Scheduler.Order.ToList(), []),
```

The Protocol test:

```csharp
    [Fact]
    public void A_round_started_whose_order_is_the_projections_own_list_keeps_that_order()
    {
        var a = Fresh();
        a.Apply(Ev(1, new RoundStarted(1, [Samples.Team1, Samples.Team2], [])));
        a.Apply(Ev(2, new RoundStarted(2, a.Projection.Scheduler.Order, [])));
        Assert.Equal([Samples.Team1, Samples.Team2], a.Projection.Scheduler.Order);
    }
```

In `GfNames.cs`, `Rounds` is `Scheduler.SchedulerBase.RoundsPhase` (the two spellings cannot drift):

```csharp
    public const string Rounds = Scheduler.SchedulerBase.RoundsPhase;
```

- [ ] **Step 7: Run `dotnet test --filter "AlternatingScheduler|EventApplier"`; expect 10 `AlternatingScheduler` passed (7 facts + the dial's 3 theory cases). Commit: `Add the scheduler base and the alternating-activation shape`.**
