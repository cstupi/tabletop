# Task 8: The interpreter — frames from the log, steps, outcomes, confirm gates

**Files:**
- Create: `server/game/Interpreter/Steps.cs`, `server/game/Interpreter/FrameReader.cs`, `server/game/Interpreter/StepContext.cs`, `server/game/Interpreter/SequenceInterpreter.cs`
- Modify: `server/game/Rules/IGameRules.cs` (the interpreter's members; the scheduler plan's Task 2 adds its own block later)
- Create: `server/game/Scheduler/SchedulerTypes.cs`, `server/game/Scheduler/ActivationOption.cs` — **the scheduler seam**, declared by this plan because it runs first and this task's interface members and `StubRules` reference the types (see Step 1)
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (this plan's partial of the skeleton's `GrimdarkFutureRules`, with `NotImplementedException` bodies until Tasks 11–17 fill them)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`, `tests/Tabletop.GameServer.Tests/InterpreterTests.cs`

**Interfaces:**
- Consumes: Tasks 5–7; `Projection.Stack`, `Frame`, `OpenAsk`, `Ask`, `AskQuestions.Confirm`, `Confirmation`, `SequenceStarted/Finished/Abandoned`, `StepCompleted` (with `Values`, Interfaces needed 1), `AskOpened/Closed`, `TeamAnswer` (Protocol).
- Produces (namespace `Tabletop.GameServer.Interpreter`): `interface IStep { string Name { get; } StepOutcome Run(StepContext ctx); }`; `sealed record Sequence(string Name, IRL<IStep> Steps)` (throws `ArgumentException` when two consecutive steps share a name); `static class StepRepeat { const string Key = "$repeat" }`; `abstract record StepOutcome` with `Complete(IRL<StateChange> Changes, IRD<string,int>? Values = null, bool Repeat = false)` (`Repeat`: the step runs again with a fresh history and the same position — how a move step takes its next segment; `SequenceInterpreter` writes the reserved key `StepRepeat.Key = "$repeat"` into the `StepCompleted.Values` it emits, so the repeat is recoverable from the log), `Finish(IRL<StateChange> Changes, IRD<string,int>? Values = null)` (completes the step and ends the sequence), `OpenAsks(IRL<Ask> Asks)`, `Push(string Sequence, SequenceContext Context)`; `sealed record AskRecord(Ask Ask, IRD<Guid, AskAnswer>? Answers)` with `bool Closed`, `AskAnswer? Single`; `sealed record ChildRecord(Guid FrameId, string Sequence, SequenceContext Context, bool Finished, bool Abandoned)`; `sealed record StepHistory(IRL<AskRecord> Asks, IRL<ChildRecord> Children)` with `bool Waiting` (an Ask still open), `AskRecord? LastClosed(string? question = null)`, `int Confirmed`; `sealed record FrameView(Frame Frame, int Position, IRD<string,int> Values, StepHistory History, FrameView? Parent)` with `Guid Id`, `SequenceContext Context`, `int Value(string key, int fallback = 0)`; `static class FrameReader { static IRL<FrameView> Read(GameState state); }` (bottom first); `interface ISequenceStarter { IRL<EventPayload> Start(GameState, string, SequenceContext); }`; `sealed class StepContext` with `GameState State`, `PropertySet Properties`, `IGameRules Rules`, `FrameView Frame`, `StepHistory History` (= `Frame.History`), `Projection Projection`, `Guid? ActorTeam`, `Guid? TeamOf(Guid unitId)`, `RollRecord Roll(RollPlan plan, int count)` (through a `Roller` at the log's cursor; records accumulate for the completing payload), `IRL<RollRecord> Rolls`, `IRL<DiceRerolled> ForcedRerolls`, `RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null, int? threshold = null)` (threshold from `Rules.RollSpec(roll)` unless the ruleset passes one, which is how a target number read off a model's slot rather than the unit's reaches a plan), `Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null)` (an Ask on this frame); `sealed class SequenceInterpreter(IGameRules rules, PropertySet properties, IDiceSource dice) : ISequenceStarter` (named so because it lives in the `Interpreter` namespace) with `IRL<EventPayload> Advance(GameState state)` and `IRL<EventPayload> Start(GameState state, string sequence, SequenceContext context)`; `static class Teams { static Guid? Of(GameState, Guid unitId); }`.
- Produces on `IGameRules` (namespace `Tabletop.GameServer.Rules`): `Sequence Sequence(string name)` (throws `UnknownSequenceException(string)`); `string? ActionSequence(string actionName)` (the sequence an `action` row starts; null when the action has none — an out-of-set name accepted under `warn`/`silent`, which the scheduler records without a sequence); `RollSpec RollSpec(string roll)` with `sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold)`; `string HitRoll { get; }` (the roll `ToHitPenalties` suppresses); `IRL<Reaction> ReactionsAt(GameState, PropertySet, string point, FrameView frame)` (Task 9); `IRL<SequenceStart> EndOfTurnSequences(GameState, PropertySet, TurnContext turn)`, `IRL<StateChange> OnRoundStarted(GameState, PropertySet, int round)`, `IRL<Guid>? OrderReactions(GameState, Guid? actorTeam, IRL<Guid> reactingTeams)` (Task 9; null from `GrimdarkFutureRules` and `StubRules`); `IRL<StateChange> OnTurnEnded(GameState, PropertySet, TurnContext turn)` (Task 11; `TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq)` and `Activation(Guid UnitId, string Action, bool RunsSequence, IRL<StateChange> Changes)` are the scheduler plan's Task 1 types, namespace `Tabletop.GameServer.Scheduler` — these three are the only round and turn hooks — the scheduler plan defines no `EndOfTurn`, `RoundStartChanges` or `TurnEndChanges` of its own); `sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context)`; `sealed record SequenceStart(string Sequence, SequenceContext Context)` (the scheduler plan's `HookStartSequences` carries these).
- How a frame's position and history are read: position = the frame's completed steps less its repeats (a `step_completed` naming the same step as the one before it is a repeat; a `Sequence` never lists one name twice in a row), less one more while the frame's last `step_completed` carries `StepRepeat.Key`, so a repeating step keeps its position; values = the `Values` of those steps folded in order (later wins), without `StepRepeat.Key`; history = the effective log after the frame's last `step_completed` (or its `sequence_started`): `ask_opened` with this `FrameId` → an `AskRecord`, `ask_closed` → its answers; a `sequence_started` while this frame is on top → a direct `ChildRecord`, marked by its `sequence_finished`/`sequence_abandoned`. A step re-reads all of this on every run; that is the whole of its memory.
- `Advance`: nothing on the stack → `[]`; the top frame past its last step → `sequence_finished`; an open Ask on the top frame → `[]`; position > 0 with no closed confirm in the history and a gate addressee → a `Confirm` Ask to it. The addressee is the actor's team when `WaitsBetweenSteps` is on, and **every team** for a frame with no actor and no parent whatever that setting says (the roll-off, whose dice are otherwise unreachable once it pops — ruling 8). The Ask is unsealed, so the dispatcher closes it on the first answer. A nested actorless frame is not gated. Otherwise run the step: `Complete` → `step_completed` (its `Answer` is the last closed non-confirm Ask's single answer, its `Rolls` the context's, `Changes`, `Values`) then the forced `dice_rerolled` payloads, then `sequence_finished` when it was the last step; `Finish` → the same with `sequence_finished` regardless; `OpenAsks` → `ask_opened` each; `Push` → `sequence_started` for the child. One outcome per call; the session's `AdvanceAsync` loop calls again.

Spec: action sequences → The resolution stack (four kinds; a frame is name, context, position, produced values), A step ends one of four ways, One roll primitive ("when on, every step boundary is a confirm gate — the gate is an Ask owned by the interpreter"), Asks; resolution → A pipeline in progress is state; `architecture.md` → Abilities (a sequence is a list of C# steps run over a frame stack); scheduler → The turn is an Ask (the scheduler starts a sequence through `Start`).

- [ ] **Step 1: Write the `IGameRules` additions and the scheduler seam**

Add these `using`s and records to `Rules/IGameRules.cs`, and the members to the interface body (the scheduler plan's Task 2 adds its own block beside them later; the merged file is written in full in the skeleton plan → Interfaces → Produces → `Tabletop.GameServer.Rules`). `TurnContext` is the scheduler plan's Task 1 record (`Scheduler/SchedulerTypes.cs`); land that task first, or add the record from it — it has no other dependency.

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

public sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold);
public sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context);
public sealed record SequenceStart(string Sequence, SequenceContext Context);
public sealed class UnknownSequenceException(string name) : Exception($"No sequence named '{name}'.");

public interface IGameRules
{
    // string Id is the skeleton's. The scheduler plan's Task 2 adds its block here later; until then this is the whole interface.

    // Added by the interpreter plan. Sequences and steps: the step lists are per-ruleset C# (architecture.md → Where data stops).
    Sequence Sequence(string name);
    /// <summary>The sequence an action row starts; null when the action has none (an out-of-set name accepted under warn/silent).</summary>
    string? ActionSequence(string actionName);
    // Per roll: the dice convention and the threshold derivation, a function (action sequences → constraints 2–4).
    RollSpec RollSpec(string roll);
    string HitRoll { get; }
    // Windows: who can react at a named point, and with what (action sequences → Windows).
    IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame);
    // Hooks the scheduler calls (scheduler → The line), each once per turn or round, with the scheduler's TurnContext.
    /// <summary>Sequences to run after the activation's own work, before turn_ended (OPR: end-of-activation morale). Empty: nothing to run.</summary>
    IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn);
    /// <summary>Changes riding on round_started (OPR: fatigue cleared, spell tokens granted). Round 0 — the phases before round one — gets none.</summary>
    IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round);
    /// <summary>Changes riding on turn_ended (OPR: wounds-this-activation to 0; an idle activation clears Shaken).</summary>
    IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn);
    /// <summary>The scheduler's reaction ordering, when it has one; null leaves Windows.ReactionOrder's own policy in force.</summary>
    IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams);
}
```

**The scheduler seam, declared here.** `TurnContext`, `TurnAsk`, `Activation`, `HookResult` and `FirstTeamDecision` are named by the interface above and implemented by `StubRules` below, and `GfHooks.OnTurnEnded` (Task 11) reads `ActivationOption.Idle`. This plan runs first (`docs/design/README.md` → Build order), so it declares them, in the scheduler's own namespace and files, exactly as the scheduler plan specifies, and that plan reads them (its Task 1). Write `server/game/Scheduler/SchedulerTypes.cs` and `server/game/Scheduler/ActivationOption.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

// The scheduler's side of the shared engine is Tabletop.GameServer.Engine.IScheduler (this plan's Task 10):
// IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter). The scheduler plan's SchedulerBase (its Task 5) implements it.
// These records are the seam between the two rules plans. This plan declares them because it runs first and its own IGameRules
// members and StubRules reference them; the scheduler plan reads them and adds nothing here.

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
/// <summary>Sequences for the interpreter to start, as SequenceStart(Sequence, Context) — the record beside IGameRules.</summary>
public sealed record HookStartSequences(IReadOnlyList<SequenceStart> Sequences) : HookResult;

/// <summary>
/// Who goes first: an order, or "roll off" — the scheduler starts the named ruleset sequence through the interpreter and reads
/// the descending order back from its step_completed.Values place_1..place_n (pages 7, 50; scheduler plan Task 3).
/// </summary>
public abstract record FirstTeamDecision;
public sealed record OrderedTeams(IReadOnlyList<Guid> Order) : FirstTeamDecision;
public sealed record RollOffNeeded(string Sequence) : FirstTeamDecision;
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>The activate Ask's option id names the unit and the action, so an out-of-set answer still says what it meant. Declared here
/// with the seam because GfHooks.OnTurnEnded (Task 11) reads Idle; the scheduler plan's Task 1 tests it and adds nothing.</summary>
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

`GrimdarkFutureRules` is already `public sealed partial class` in `Rules/GrimdarkFuture/GfRules.cs` (skeleton Task 6); create `Rules/GrimdarkFuture/GfRules.Sequences.cs` as a further partial with each new member's body `throw new NotImplementedException()` until its task. Do not redeclare `RulesId`, `Instance` or `Id`. `GameRulesRegistry` is unchanged.

- [ ] **Step 2: Write `Steps.cs` and `FrameReader.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>A step is ruleset C#. It reads game state and its frame and ends one of four ways. It holds nothing between runs.</summary>
public interface IStep { string Name { get; } StepOutcome Run(StepContext ctx); }

/// <summary>Starts a sequence on the stack: what the scheduler (Task 10) needs from the interpreter.</summary>
public interface ISequenceStarter { IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context); }

/// <summary>No two consecutive steps share a name: a repeated completion of one name is how a repeating step is told from the next step.</summary>
public sealed record Sequence(string Name, IReadOnlyList<IStep> Steps)
{
    public IReadOnlyList<IStep> Steps { get; } = Steps.Zip(Steps.Skip(1)).Any(p => p.First.Name == p.Second.Name)
        ? throw new ArgumentException($"{Name}: consecutive steps share a name") : Steps;
}

public abstract record StepOutcome;
/// <summary>step_completed; the frame advances. Values are the settled parameters later steps and child frames read. Repeat keeps the position: the same step runs again with a fresh history (a move step's next segment).</summary>
public sealed record Complete(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null, bool Repeat = false) : StepOutcome;
/// <summary>step_completed, then sequence_finished: the remaining steps never happen (a charge that cannot reach ends the activation).</summary>
public sealed record Finish(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null) : StepOutcome;
/// <summary>The frame suspends until every Ask is closed. Several Asks: a window offered to several teams, one each.</summary>
public sealed record OpenAsks(IReadOnlyList<Ask> Asks) : StepOutcome;
/// <summary>A nested sequence starts; this step runs again when it finishes and sees it in History.Children.</summary>
public sealed record Push(string Sequence, SequenceContext Context) : StepOutcome;

/// <summary>
/// The reserved key SequenceInterpreter writes into a repeating step's StepCompleted.Values: a step that returned
/// Complete(Repeat: true) is recoverable as a repeat from the effective log alone, because a step holds no C# locals across
/// a suspension. Prefixed with '$' so it can never collide with a ruleset's own snake_case keys, and so a client's log
/// panel can filter it out by this one name.
/// </summary>
public static class StepRepeat
{
    public const string Key = "$repeat";
}

public sealed record AskRecord(Ask Ask, IReadOnlyDictionary<Guid, AskAnswer>? Answers)
{
    public bool Closed => Answers is not null;
    public AskAnswer? Single => Answers is { Count: 1 } a ? a.Values.First() : null;
}
public sealed record ChildRecord(Guid FrameId, string Sequence, SequenceContext Context, bool Finished, bool Abandoned);

public sealed record StepHistory(IReadOnlyList<AskRecord> Asks, IReadOnlyList<ChildRecord> Children)
{
    public static readonly StepHistory Empty = new([], []);
    public bool Waiting => Asks.Any(a => !a.Closed);
    public AskRecord? LastClosed(string? question = null) => Asks.LastOrDefault(a => a.Closed && (question is null || a.Ask.Question == question));
    public int Confirmed => Asks.Count(a => a.Closed && a.Ask.Question == AskQuestions.Confirm);
    public IEnumerable<AskRecord> ClosedExceptConfirm => Asks.Where(a => a.Closed && a.Ask.Question != AskQuestions.Confirm);
}

public sealed record FrameView(Frame Frame, int Position, IReadOnlyDictionary<string, int> Values, StepHistory History, FrameView? Parent)
{
    public Guid Id => Frame.Id;
    public SequenceContext Context => Frame.Context;
    public int Value(string key, int fallback = 0) => Values.GetValueOrDefault(key, fallback);
}
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Frames, positions, values and histories, read from the projection's stack and the effective log. Nothing here is stored.</summary>
public static class FrameReader
{
    public static IReadOnlyList<FrameView> Read(GameState state)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var views = new List<FrameView>();
        foreach (var frame in state.Projection.Stack)
        {
            var started = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == frame.Id);
            var lastStep = log.FindLastIndex(e => e.Payload is StepCompleted s && s.FrameId == frame.Id);
            var values = new Dictionary<string, int>();
            foreach (var s in log.Where(e => e.Payload is StepCompleted sc && sc.FrameId == frame.Id).Select(e => (StepCompleted)e.Payload))
                foreach (var (k, v) in s.Values ?? new Dictionary<string, int>()) if (k != StepRepeat.Key) values[k] = v;   // the marker is position bookkeeping, read from the raw last step_completed below
            // A repeat completes the same step again: distinct consecutive-name runs give the index of the step after the run's
            // first completion, so a step_completed still marked $repeat holds position at the run's own step one more time.
            var runs = frame.Steps.Count - frame.Steps.Zip(frame.Steps.Skip(1)).Count(p => p.First.Step == p.Second.Step);
            var repeating = frame.Steps.Count > 0 && (frame.Steps[^1].Values?.ContainsKey(StepRepeat.Key) ?? false);
            var position = runs - (repeating ? 1 : 0);
            views.Add(new FrameView(frame, position, values, History(log, frame.Id, Math.Max(started, lastStep) + 1), views.LastOrDefault()));
        }
        return views;
    }

    /// <summary>Everything that happened to this frame since its current step began: its Asks, and the sequences it pushed (depth 1 only).</summary>
    static StepHistory History(IReadOnlyList<GameEvent> log, Guid frameId, int from)
    {
        var asks = new List<AskRecord>();
        var children = new List<ChildRecord>();
        var depth = 0;
        for (var i = from; i < log.Count; i++)
        {
            switch (log[i].Payload)
            {
                case AskOpened o when o.Ask.FrameId == frameId: asks.Add(new AskRecord(o.Ask, null)); break;
                case AskClosed c when asks.FindIndex(a => a.Ask.Id == c.AskId) is var k and >= 0:
                    asks[k] = asks[k] with { Answers = c.Answers.ToDictionary(a => a.TeamId, a => a.Answer) }; break;
                case SequenceStarted s when s.FrameId != frameId:
                    if (depth == 0) children.Add(new ChildRecord(s.FrameId, s.Sequence, s.Context, false, false));
                    depth++; break;
                case SequenceFinished f when f.FrameId != frameId: Close(children, f.FrameId, abandoned: false); depth = Math.Max(0, depth - 1); break;
                case SequenceAbandoned a when a.FrameId != frameId: Close(children, a.FrameId, abandoned: true); depth = Math.Max(0, depth - 1); break;
            }
        }
        return new StepHistory(asks, children);
    }

    static void Close(List<ChildRecord> children, Guid frameId, bool abandoned)
    {
        var k = children.FindIndex(c => c.FrameId == frameId);
        if (k >= 0) children[k] = children[k] with { Finished = !abandoned, Abandoned = abandoned };
    }
}
```

- [ ] **Step 3: Write `StepContext.cs` and `SequenceInterpreter.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

public static class Teams
{
    public static Guid? Of(GameState state, Guid unitId) =>
        state.Projection.Units.TryGetValue(unitId, out var u) ? state.Info.Participants.FirstOrDefault(p => p.Id == u.ParticipantId)?.TeamId : null;
    public static IReadOnlyList<Guid> Others(GameState state, Guid? team) => state.Info.Teams.Select(t => t.Id).Where(t => t != team).ToList();
}

/// <summary>What a step sees: state, the pinned set, its frame and history, and a roller at the log's cursor.</summary>
public sealed class StepContext(GameState state, PropertySet properties, IGameRules rules, FrameView frame, Roller roller)
{
    private readonly List<RollRecord> _rolls = new();
    private readonly List<DiceRerolled> _forced = new();

    public GameState State { get; } = state;
    public PropertySet Properties { get; } = properties;
    public IGameRules Rules { get; } = rules;
    public FrameView Frame { get; } = frame;
    public StepHistory History => Frame.History;
    public Projection Projection => State.Projection;
    public Guid? ActorTeam => Frame.Context.ActorUnitId is { } u ? Teams.Of(State, u) : null;
    public Guid? TeamOf(Guid unitId) => Teams.Of(State, unitId);
    public IReadOnlyList<RollRecord> Rolls => _rolls;
    public IReadOnlyList<DiceRerolled> ForcedRerolls => _forced;

    /// <summary>
    /// The roll spec supplies the threshold, unless the ruleset names one. A ruleset overrides it where the target number is a
    /// model's rather than the unit's: p.4's Quality test is "the model's Quality value", so `GfRolls` passes the value it read
    /// off the attacking model's slot and one such plan is built per distinct value.
    /// </summary>
    public RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null, int? threshold = null)
    {
        var spec = Rules.RollSpec(roll);
        return RollEffects.Plan(live, roller, roll, threshold ?? spec.Threshold(live, roller), baseModifiers, spec.Convention, requiredRaw, Rules.HitRoll);
    }

    public RollRecord Roll(RollPlan plan, int count)
    {
        var result = roller.Roll(Frame.Id, plan, count);
        _rolls.Add(result.Record);
        if (result.ForcedRerolls is { } f) _forced.Add(f);
        return result.Record;
    }

    public Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team], legal, allowPass, false, windowKind, Frame.Id);
}

/// <summary>Runs the top frame's current step and turns its outcome into payloads. Generic: knows no ruleset.</summary>
public sealed class SequenceInterpreter(IGameRules rules, PropertySet properties, IDiceSource dice) : ISequenceStarter
{
    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        rules.Sequence(sequence);   // throws UnknownSequenceException
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }

    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        var frames = FrameReader.Read(state);
        if (frames.Count == 0) return [];
        var top = frames[^1];
        var sequence = rules.Sequence(top.Frame.Sequence);
        if (top.Position >= sequence.Steps.Count) return [new SequenceFinished(top.Id)];
        if (top.History.Waiting) return [];
        var actorTeam = top.Context.ActorUnitId is { } u ? Teams.Of(state, u) : null;
        if (top.Position > 0 && top.History.Confirmed == 0 && GateTeams(state, top, actorTeam) is { } gate)
            return [new AskOpened(new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, $"Continue: {sequence.Steps[top.Position].Name}", gate, new Confirmation(), false, false, null, top.Id))];

        var step = sequence.Steps[top.Position];
        var ctx = new StepContext(state, properties, rules, top, new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state)));
        var events = new List<EventPayload>();
        switch (step.Run(ctx))
        {
            case Complete c: Completed(events, ctx, step, c.Changes, RepeatValues(c), finished: !c.Repeat && top.Position + 1 == sequence.Steps.Count); break;
            case Finish f: Completed(events, ctx, step, f.Changes, f.Values, finished: true); break;
            case OpenAsks { Asks.Count: 0 }: throw new InvalidOperationException($"{top.Frame.Sequence}/{step.Name} suspended with no Ask open");
            case OpenAsks a: events.AddRange(a.Asks.Select(ask => new AskOpened(ask))); break;
            case Push p: rules.Sequence(p.Sequence); events.Add(new SequenceStarted(Guid.CreateVersion7(), p.Sequence, p.Context)); break;
        }
        return events;
    }

    /// <summary>
    /// A repeat must be recoverable from the effective log alone, since a step holds no C# locals across a suspension and
    /// FrameReader rebuilds the frame from the log. Complete(Repeat: true) rides the reserved StepRepeat.Key in this
    /// step_completed's Values; FrameReader reads it back to hold the frame's position at this step for one more run.
    /// </summary>
    static IReadOnlyDictionary<string, int>? RepeatValues(Complete c)
    {
        if (!c.Repeat) return c.Values;
        var values = c.Values is null ? new Dictionary<string, int>() : new Dictionary<string, int>(c.Values);
        values[StepRepeat.Key] = 1;
        return values;
    }

    /// <summary>
    /// Who a step boundary is gated to, or null for no gate. The actor's team, and only when WaitsBetweenSteps is on. Every team,
    /// whatever that setting says, for a frame with no actor and no parent: the roll-off is the only one, and nothing else keeps its log
    /// open, so its dice would never be reachable by RerollDice, which needs the frame on the stack. The Ask is unsealed, so the
    /// dispatcher closes it on the first answer and any team releases it. A nested actorless frame - the terrain-wounds deal-wounds -
    /// is not gated, because its own assign Ask already suspends it.
    /// </summary>
    static IReadOnlyList<Guid>? GateTeams(GameState state, FrameView top, Guid? actorTeam) =>
        actorTeam is { } team ? state.Info.WaitsBetweenSteps ? [team] : null
        : top.Parent is null ? state.Info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList()
        : null;

    static void Completed(List<EventPayload> events, StepContext ctx, IStep step, IReadOnlyList<StateChange> changes, IReadOnlyDictionary<string, int>? values, bool finished)
    {
        var answer = ctx.History.ClosedExceptConfirm.LastOrDefault()?.Single;
        events.Add(new StepCompleted(ctx.Frame.Id, step.Name, answer, ctx.Rolls, changes) { Values = values is { Count: > 0 } ? values : null });
        events.AddRange(ctx.ForcedRerolls);
        if (finished) events.Add(new SequenceFinished(ctx.Frame.Id));
    }
}
```

