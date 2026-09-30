# Task 1: The scheduler's types, the option id and the log reader

**Files:**
- Create: `server/game/Scheduler/SchedulerLog.cs`
- Read, do not modify: `server/game/Scheduler/SchedulerTypes.cs`, `server/game/Scheduler/ActivationOption.cs` — **the interpreter plan's Task 8 declares both**, in this namespace and to the letter of Steps 2 and 4 below, because that plan runs first and its `IGameRules` members and `StubRules` reference the types. Land it first. The blocks below are what must already be there; verify, do not rewrite
- Test: `tests/Tabletop.GameServer.Tests/ActivationOptionTests.cs`, `tests/Tabletop.GameServer.Tests/SchedulerLogTests.cs`

**Interfaces:**
- Consumes: `GameState` (skeleton); `Ask`, `LegalSet`, `AskAnswer`, `StateChange`, `EventPayload`, `GameEvent`, `EventApplier.EffectiveLog`, `SequenceContext`, `AskQuestions`, `AskOpened`, `AskClosed`, `RoundStarted`, `TurnStarted`, `TurnEnded`, `PhaseStarted` (Protocol); `SequenceStart`, `TurnAsk`, `Activation`, `TurnContext`, `HookResult`, `FirstTeamDecision`, `ActivationOption` (interpreter plan Task 8, which must have landed: it declares the seam, and this plan adds nothing to it — if the files are absent, stop rather than write them). The scheduler's side of the engine is the interpreter plan's `Engine.IScheduler` (its Task 10), not a type of this plan's.
- Produces (namespace `Tabletop.GameServer.Scheduler`): `sealed record TurnAsk(string Question, string Prompt, LegalSet Legal, bool AllowPass)`; `sealed record Activation(Guid UnitId, string Action, bool RunsSequence, IReadOnlyList<StateChange> Changes)`; `sealed record TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq)`; `abstract record HookResult` with `HookDone`, `HookWaiting`, `HookEmit(IReadOnlyList<EventPayload> Events)`, `HookStartSequences(IReadOnlyList<SequenceStart> Sequences)`; `abstract record FirstTeamDecision` with `OrderedTeams(IReadOnlyList<Guid> Order)` and `RollOffNeeded(string Sequence)` (the ruleset sequence that decides the order — OPR's `GfNames.RollOffSeq`; Task 3 starts it and reads its result); `static class ActivationOption { const string Idle = "Idle"; string Id(Guid unitId, string action); bool TryParse(string id, out Guid unitId, out string action); AskOption For(UnitInPlay unit, string action); }`; `sealed class SchedulerLog` with `static SchedulerLog Of(GameState state)`, `IReadOnlyList<GameEvent> Effective`, `long LastSeq`, `GameEvent? Last(Func<EventPayload, bool> match)`, `IReadOnlyList<GameEvent> After(long seq)`, `long RoundStartedAt`, `long PhaseStartedAt`, `long TurnStartedAt`, `long RotationStartAt` (the later of round and phase start), `IReadOnlyList<GameEvent> SinceRound`, `IReadOnlyList<GameEvent> SinceRotation`, `IReadOnlyList<GameEvent> SinceTurn`, `long CycleStartAt`, `IReadOnlyList<GameEvent> SinceCycle`, `static bool IsSchedulerAsk(Ask ask)`, `static bool IsTurnAsk(Ask ask)` (the four turn questions `TurnCycle` reads an answer for, which is the scope of the readability check `CommandDispatcher.Answer` gains in Task 4), `(AskOpened Opened, long OpenedAt, AskClosed? Closed, long ClosedAt)? LastSchedulerAsk(IReadOnlyList<GameEvent> slice)`, `IReadOnlyDictionary<Guid, long> LastTurnEndedByTeam(IReadOnlyList<GameEvent> slice)`, `Guid? LastTurnEndedTeam(IReadOnlyList<GameEvent> slice)`.

Spec: scheduler → The line (generic component, hooks), State (rebuilt from the log), The turn is an Ask (the answer names a unit and an action; the Ask is the interpreter's Ask type; `FrameId = null` per the contracts plan's `Ask` doc-comment), Events. A scheduler Ask is one whose `FrameId` is null and whose question is one of the engine-owned `AskQuestions` the scheduler issues: `Activate`, `ActOrEndPhase`, `Deploy`, `DeployScouts`, `DeployAmbusher`, `EndRound`. The first four are also the *turn* questions, `IsTurnAsk`: the ones whose answer goes through `ReadAnswer`, and so the ones the dispatcher probes for readability under the enforcement dial (Task 4, ruling 3). `EndRound` is the round boundary's (Task 5); it is a scheduler Ask for the same reason the others are — a menu edit that supersedes it closes it with no answers, and only `IsSchedulerAsk` finds it. The option id convention `"{unitId:N}/{action}"` exists so an out-of-set `Chosen` under `warn`/`silent` still names a unit and an action (scheduler → Enforcement).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class ActivationOptionTests
{
    [Fact]
    public void Id_round_trips_unit_and_action()
    {
        var unit = Guid.CreateVersion7();
        var id = ActivationOption.Id(unit, "Advance");
        Assert.Equal(unit.ToString("N") + "/Advance", id);
        Assert.True(ActivationOption.TryParse(id, out var u, out var a));
        Assert.Equal((unit, "Advance"), (u, a));
    }

    [Theory] [InlineData("")] [InlineData("nope")] [InlineData("zzz/Advance")] [InlineData("0193b0f0d1a27c6e9f0e1234567890ab/")]
    public void Malformed_ids_do_not_parse(string id) => Assert.False(ActivationOption.TryParse(id, out _, out _));

    [Fact]
    public void Option_highlights_the_unit_and_labels_with_its_name()
    {
        var p = Projection.Initial(Samples.Board, Samples.Game);
        var unit = p.Units[Samples.Unit];
        var option = ActivationOption.For(unit, "Rush");
        Assert.Equal([Samples.Unit], option.Pieces);
        Assert.Equal($"{unit.Definition.Name}: Rush", option.Label);
        Assert.Equal(ActivationOption.Id(Samples.Unit, "Rush"), option.Id);
    }
}
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class SchedulerLogTests
{
    static readonly Guid AskA = Guid.CreateVersion7(), AskB = Guid.CreateVersion7();
    static Ask Activate(Guid id, Guid team) => new(id, AskQuestions.Activate, "Activate", [team], new Options([]), true, false, null, null);
    static Ask Framed(Guid id, Guid team) => new(id, AskQuestions.Confirm, "Confirm", [team], new Confirmation(), false, false, null, Samples.Frame);
    static Ask EndRound(Guid id, Guid[] teams) => new(id, AskQuestions.EndRound, "End the round", teams, new Confirmation(), false, false, null, null);

    static GameState State(params EventPayload[] history)
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(Samples.Game, Samples.Board, applier, GrimdarkFutureRules.Instance);
    }

    [Fact]
    public void Slices_are_cut_at_the_last_round_phase_and_turn_events_of_the_effective_log()
    {
        var log = SchedulerLog.Of(State(
            new RoundStarted(1, [Samples.Team1, Samples.Team2], []), new PhaseStarted("rounds"),
            new TurnStarted(Samples.Team1), new TurnEnded(Samples.Team1, false, []),
            new TurnStarted(Samples.Team2), new AskOpened(Activate(AskA, Samples.Team2)),
            new Undone(5), new TurnStarted(Samples.Team2), new AskOpened(Activate(AskB, Samples.Team2))));
        Assert.Equal((1L, 2L, 8L, 2L, 8L), (log.RoundStartedAt, log.PhaseStartedAt, log.TurnStartedAt, log.RotationStartAt, log.CycleStartAt));
        Assert.Equal([9L], log.SinceTurn.Select(e => e.Seq));
        Assert.Equal([9L], log.SinceCycle.Select(e => e.Seq));
        Assert.Equal([3L, 4L, 8L, 9L], log.SinceRotation.Select(e => e.Seq));
        Assert.Equal(9, log.LastSeq);
        Assert.Equal(Samples.Team1, log.LastTurnEndedTeam(log.SinceRound));
        Assert.Equal(4, log.LastTurnEndedByTeam(log.SinceRound)[Samples.Team1]);
    }

    [Fact]
    public void Scheduler_asks_are_unframed_engine_questions_and_the_last_one_is_found_with_its_close()
    {
        Assert.True(SchedulerLog.IsSchedulerAsk(Activate(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsSchedulerAsk(Framed(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsSchedulerAsk(Activate(AskA, Samples.Team1) with { Question = "shoot_or_not" }));
        Assert.True(SchedulerLog.IsSchedulerAsk(EndRound(AskB, [Samples.Team1, Samples.Team2])));                        // the round boundary's own Ask (Task 5 emits it)
        Assert.True(SchedulerLog.IsTurnAsk(Activate(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsTurnAsk(EndRound(AskB, [Samples.Team1])));                                           // a confirmation, not a turn: ReadAnswer is not asked about it (Task 4)
        Assert.False(SchedulerLog.IsTurnAsk(Framed(AskA, Samples.Team1)));
        var log = SchedulerLog.Of(State(new TurnStarted(Samples.Team1),
            new AskOpened(Activate(AskA, Samples.Team1)), new AskAnswered(AskA, Samples.Team1, new Passed(), false),
            new AskClosed(AskA, [new TeamAnswer(Samples.Team1, new Passed())]), new AskOpened(Framed(AskB, Samples.Team1))));
        var found = log.LastSchedulerAsk(log.SinceTurn);
        Assert.NotNull(found);
        Assert.Equal((AskA, 2L, 4L), (found.Value.Opened.Ask.Id, found.Value.OpenedAt, found.Value.ClosedAt));
        Assert.IsType<Passed>(found.Value.Closed!.Answers[0].Answer);
        Assert.Null(SchedulerLog.Of(State(new TurnStarted(Samples.Team1))).LastSchedulerAsk(log.SinceTurn));
    }

    [Fact]
    public void An_empty_log_has_no_anchors()
    {
        var log = SchedulerLog.Of(State());
        Assert.Equal((0L, 0L, 0L, 0L, 0L), (log.RoundStartedAt, log.PhaseStartedAt, log.TurnStartedAt, log.RotationStartAt, log.LastSeq));
        Assert.Empty(log.SinceRound);
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "ActivationOption|SchedulerLog"`; expect `SchedulerLogTests` to fail to compile and `ActivationOptionTests` to pass. Verify `SchedulerTypes.cs` reads as follows** (the interpreter plan's Task 8 wrote it; if it is absent, that plan has not landed — stop)

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

// The scheduler's side of the shared engine is Tabletop.GameServer.Engine.IScheduler (interpreter plan, Task 10):
// IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter). SchedulerBase (Task 5) implements it.
// Declared by the interpreter plan's Task 8 (it runs first and references these); this plan reads them.

/// <summary>A team's turn Ask as GameRules computes it: the question, a prompt, the legal set, whether pass is allowed. Null from the hook means the legal set is empty.</summary>
public sealed record TurnAsk(string Question, string Prompt, LegalSet Legal, bool AllowPass);

/// <summary>What a turn answer means. RunsSequence: the interpreter starts the action's sequence. Changes: what the activation writes when it starts (OPR: action_recorded).</summary>
public sealed record Activation(Guid UnitId, string Action, bool RunsSequence, IReadOnlyList<StateChange> Changes);

/// <summary>The turn the end-of-turn hooks see. ActivationEndedAtSeq is the seq at which the activation's own work ended (its frame finished, or the answer closed for a sequence-less activation).</summary>
public sealed record TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq);

/// <summary>What a stepwise hook says: done, waiting on something in the log (an open Ask, models still on the shelf), events to emit, or sequences for the interpreter to start.</summary>
public abstract record HookResult;
public sealed record HookDone : HookResult;
public sealed record HookWaiting : HookResult;
public sealed record HookEmit(IReadOnlyList<EventPayload> Events) : HookResult;
/// <summary>Sequences for the interpreter to start, as SequenceStart(Sequence, Context) — the interpreter plan's record beside IGameRules.</summary>
public sealed record HookStartSequences(IReadOnlyList<SequenceStart> Sequences) : HookResult;

/// <summary>
/// Who goes first: an order, or "roll off" — the scheduler starts the named ruleset sequence through the interpreter and reads
/// the descending order back from its step_completed.Values place_1..place_n (pages 7, 50; Task 3).
/// </summary>
public abstract record FirstTeamDecision;
public sealed record OrderedTeams(IReadOnlyList<Guid> Order) : FirstTeamDecision;
public sealed record RollOffNeeded(string Sequence) : FirstTeamDecision;
```

- [ ] **Step 3: There is no interpreter interface to write.** The scheduler consumes the interpreter plan's `Interpreter.ISequenceStarter { IReadOnlyList<EventPayload> Start(GameState, string sequence, SequenceContext) }`, handed to it on every `Engine.IScheduler.Advance(state, starter)` call, and the `PropertySet` its factory receives. Nothing in `Rules/` is created by this task.

- [ ] **Step 4: Verify `ActivationOption.cs` reads as follows** (the interpreter plan's Task 8 wrote it)

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>The activate Ask's option id names the unit and the action, so an out-of-set answer still says what it meant.</summary>
public static class ActivationOption
{
    /// <summary>Not an action row: a Shaken unit's only activation (page 10), resolved by the scheduler with no sequence.</summary>
    public const string Idle = "Idle";

    public static string Id(Guid unitId, string action) => $"{unitId:N}/{action}";

    public static bool TryParse(string id, out Guid unitId, out string action)
    {
        unitId = default; action = "";
        var slash = id.IndexOf('/');
        if (slash != 32 || slash == id.Length - 1) return false;
        if (!Guid.TryParseExact(id[..slash], "N", out unitId)) return false;
        action = id[(slash + 1)..];
        return true;
    }

    public static AskOption For(UnitInPlay unit, string action) => new(Id(unit.Id, action), $"{unit.Definition.Name}: {action}", [unit.Id]);
}
```

- [ ] **Step 5: Write `SchedulerLog.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>The effective log (undone ranges removed) cut at the scheduler's anchors. Built afresh every call; nothing is cached across calls.</summary>
public sealed class SchedulerLog
{
    static readonly HashSet<string> TurnQuestions = [AskQuestions.Activate, AskQuestions.ActOrEndPhase, AskQuestions.Deploy, AskQuestions.DeployScouts];
    static readonly HashSet<string> Questions = [..TurnQuestions, AskQuestions.DeployAmbusher, AskQuestions.EndRound];

    private SchedulerLog(IReadOnlyList<GameEvent> effective) { Effective = effective; }

    public static SchedulerLog Of(GameState state) => new(EventApplier.EffectiveLog(state.Applier.Log));

    public IReadOnlyList<GameEvent> Effective { get; }
    public long LastSeq => Effective.Count == 0 ? 0 : Effective[^1].Seq;
    public GameEvent? Last(Func<EventPayload, bool> match) => Effective.LastOrDefault(e => match(e.Payload));
    public IReadOnlyList<GameEvent> After(long seq) => Effective.Where(e => e.Seq > seq).ToList();

    public long RoundStartedAt => Last(p => p is RoundStarted)?.Seq ?? 0;
    public long PhaseStartedAt => Last(p => p is PhaseStarted)?.Seq ?? 0;
    public long TurnStartedAt => Last(p => p is TurnStarted)?.Seq ?? 0;
    /// <summary>Where the rotation over teams restarts from Order[0]: a round or a phase start, whichever is later.</summary>
    public long RotationStartAt => Math.Max(RoundStartedAt, PhaseStartedAt);
    public IReadOnlyList<GameEvent> SinceRound => After(RoundStartedAt);
    public IReadOnlyList<GameEvent> SinceRotation => After(RotationStartAt);
    public IReadOnlyList<GameEvent> SinceTurn => After(TurnStartedAt);
    /// <summary>Where the current activation cycle starts: the turn, or a phase started inside it (player-turn phases).</summary>
    public long CycleStartAt => Math.Max(TurnStartedAt, PhaseStartedAt);
    public IReadOnlyList<GameEvent> SinceCycle => After(CycleStartAt);

    /// <summary>An Ask the scheduler issued: unframed, and one of the engine questions it owns.</summary>
    public static bool IsSchedulerAsk(Ask ask) => ask.FrameId is null && Questions.Contains(ask.Question);

    /// <summary>A scheduler Ask whose answer TurnCycle reads through IGameRules.ReadAnswer: the activate and deploy questions. The Ambusher Ask is read by RoundBoundaryStep and the boundary Ask is a confirmation, so neither is one.</summary>
    public static bool IsTurnAsk(Ask ask) => ask.FrameId is null && TurnQuestions.Contains(ask.Question);

    public (AskOpened Opened, long OpenedAt, AskClosed? Closed, long ClosedAt)? LastSchedulerAsk(IReadOnlyList<GameEvent> slice)
    {
        var opened = slice.LastOrDefault(e => e.Payload is AskOpened o && IsSchedulerAsk(o.Ask));
        if (opened is null) return null;
        var ask = (AskOpened)opened.Payload;
        var closed = slice.FirstOrDefault(e => e.Seq > opened.Seq && e.Payload is AskClosed c && c.AskId == ask.Ask.Id);
        return (ask, opened.Seq, closed?.Payload as AskClosed, closed?.Seq ?? 0);
    }

    public IReadOnlyDictionary<Guid, long> LastTurnEndedByTeam(IReadOnlyList<GameEvent> slice) =>
        slice.Where(e => e.Payload is TurnEnded).GroupBy(e => ((TurnEnded)e.Payload).TeamId).ToDictionary(g => g.Key, g => g.Max(e => e.Seq));

    public Guid? LastTurnEndedTeam(IReadOnlyList<GameEvent> slice) => (slice.LastOrDefault(e => e.Payload is TurnEnded)?.Payload as TurnEnded)?.TeamId;
}
```

- [ ] **Step 6: Run the two test classes; expect 9 passed (2 facts + 4 theory cases + 3 facts). Commit: `Add the scheduler's types, the option id and the log reader`.**
