# Scheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the scheduler in `server/game/`: the two shapes behind one interface selected by `game.scheduler_shape`, the turn as an Ask, deployment as the phase before round one after a logged roll-off, the round boundary — which a player ends — and its hooks, reaction ordering, and every hook `GameRules` supplies for Grimdark Future — all state rebuilt from the log, carried by `round_started`, `turn_started`, `turn_ended` and `phase_started`.

**Architecture:** `IScheduler.Advance(GameState)` is the scheduler's side of the one shared `IRulesEngine`; it reads `state.Projection` and the effective log on every call, never holds state of its own, and returns payloads the session appends. A shared `SchedulerBase` runs the phases before round one (alternating, one unit a turn), the round boundary (a player's `end_round` answer, then the first-team policy, roll-off, `round_started`, the interactive boundary hook) and one *activation cycle* (Ask → answer → activation → sequence → end-of-turn hook → `turn_ended`); `AlternatingActivationScheduler` and `PlayerTurnScheduler` differ only in what happens after a cycle completes and in when the action record clears. Everything ruleset-specific is a member added to `IGameRules` and implemented on `GrimdarkFutureRules`. **The seam with the interpreter plan:** the scheduler implements the interpreter plan's seam `Tabletop.GameServer.Engine.IScheduler { Advance(GameState, ISequenceStarter) }` and `Engine.ISchedulerFactory { Create(IGameRules, GameInfo, PropertySet) }`; the one `IRulesEngine` is the interpreter plan's `Engine.RulesEngine`, which calls the scheduler when no framed Ask is open and the resolution stack is empty. The scheduler starts sequences — the declared action's, the end-of-turn ones, the roll-off — through `ISequenceStarter.Start`, reads the pinned `PropertySet` for the action rows' names and hands it to the three `IGameRules` hooks the interpreter plan declares (`OnRoundStarted`, `EndOfTurnSequences`, `OnTurnEnded`), and reads the roll-off's result from its `step_completed.Values` — the interpreter holds that frame open behind its confirm gate, so the roll-off spans dispatches and the reader waits across them. It defines no `RulesEngine`, no interpreter interface and no roll primitive of its own.

**Tech Stack:** .NET 10 (`net10.0`, C# 12), xUnit 2.9.3, the skeleton's `InMemoryEventLog`, `EventApplier` and `CommandDispatcher`; the Docker Postgres for one end-to-end test.

**Spec:** `docs/superpowers/specs/2026-09-04-scheduler-design.md` (primary); `2026-08-28-game-flow-design.md` (two shapes, when the record clears, the shelf, and the team model — teams created only while `lobby` or `setup`, a participant's team is their slot, reconnecting returns the player to it); `2026-09-03-action-sequences-design.md` (the Ask, the frame stack, the end-of-activation hook, fatigue, the token economy); `2026-09-04-setup-phase-design.md` (the flip into deployment, the zone swap); `2026-08-30-presence-design.md` and `2026-09-04-whole-design-review.md` (a participant with no team is never Asked; the pass as a logged fact; enforcement three positions); `2026-08-29-identity-and-enumerations-design.md` (`scheduler_shape`, actions named per ruleset); `2026-09-04-client-shell-design.md` → Asks and windows (mutes are client-side). Rulebook: Grimdark Future v3.5.1 pages 6, 7, 10, 13–14, 21–23, 49, read for this plan. Earlier plans obeyed: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` (every Protocol name) and `2026-09-05-game-server-skeleton.md` (`IGameRules`, `IRulesEngine`, `GameState`, `Actor`, `Legality`, `EngineResult`, `CommandDispatcher`, `InMemoryEventLog`, the test infrastructure). `2026-09-05-interpreter-and-effects/README.md` is obeyed for the seam: `Engine.IScheduler`, `Engine.ISchedulerFactory`, `Interpreter.ISequenceStarter`, `Properties.PropertySet`, `IGameRules.Sequence/ActionSequence/EndOfTurnSequences/OnRoundStarted/OnTurnEnded`, `GfRolls.RollOffSequence`, `GfNames`, `Effects.Lookup`, and the test infrastructure `Sim`, `TestProperties`, `LambdaStep`. Build order: the interpreter plan's Tasks 1–11 before this plan's Task 3 (this plan's Tasks 1–2 depend only on the skeleton, plus `TestProperties.Core` for Task 2's tests).

**Requires landed:** the contracts and game-server skeleton plans, and **the whole interpreter plan** — `RulesEngine`, `ISchedulerFactory`, `ISequenceStarter`, `SequenceStart`, `GfNames`, the three hooks' `GfHooks` bodies, and the seam records in `server/game/Scheduler/` that its Task 8 declares.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, namespace root `Tabletop.GameServer`; `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; `global.json` pins SDK `10.0.400`. Files land in `server/game/` and `tests/Tabletop.GameServer.Tests/`; nothing is added to `libs/` and no Protocol type is renamed or reshaped.
- The scheduler is a generic engine component: it knows no ruleset, never reads a unit's action record, conditions or properties itself, and branches on nothing but `GameInfo.SchedulerShape` (scheduler → The line; `architecture.md` → Turn scheduling: "No engine code branches on ruleset identity").
- State is round, order over teams, active team, passed teams — read from `Projection.Scheduler` and the effective log on every call, never cached (scheduler → State, Nothing is an invariant). The four events carry it; replay applies them and never re-derives them. The team list itself is frozen at the flip to `playing`, so the order over teams is an ordering of the same set in every round of a game and an order rebuilt from the log never has to reconcile against a team that appeared or vanished (game flow → the team model).
- The turn is an Ask through the Protocol `Ask` type with `FrameId = null`; the legal set is server-computed; a team with an empty legal set is skipped with no Ask and no turn events; a participant with no team is never addressed, which is what a spectator is; the pass is `turn_ended` with `Passed = true` and stands for the round; hand moves (`models_moved`) never end a turn (scheduler → The turn is an Ask, Re-entering a round). A reconnecting player is never that participant: their team is the slot on their `participant` row, chosen before play and unchanged by the reconnect (game flow → the team model).
- **The round does not advance on its own. A player ends it**, to avoid an accidental auto-advance. Under both shapes the scheduler reaches the end of a round, opens one unframed Ask (question `AskQuestions.EndRound`, `Confirmation`, `AllowPass` false) addressed to every team that has a participant, and waits; the first answer to it is what emits the next round's `round_started`. The Ask sits before `round_started`, so `round_started` carries `OnRoundStarted`'s changes and `RoundBoundaryStep` runs after it; deployment's phases and its flip into round one are the round-0 branch, which never reaches the Ask (Task 5).
- Only OPR's `GameRules` is built: `GrimdarkFutureRules`, id `grimdark-future`, runs under both shapes. 40k is a constraint on the interface, tested through a fake `IGameRules`, never implemented.
- The engine never appends or broadcasts; it returns payloads, and never emits `ask_answered` or an `ask_closed` for an answer — the dispatcher does. The one exception, named in the skeleton (→ Interfaces needed 2) and reserved for the scheduler: an unframed Ask of its own that a menu edit (`set_active_team`, `set_round`) superseded is closed with an **empty** answer list, because the dispatcher only closes Asks that carry a `FrameId`.
- Enumeration, not validation: legality is the Ask's legal set; an out-of-set answer that reaches the scheduler under `warn`/`silent` is acted on as if legal (scheduler → Enforcement). **An answer the ruleset cannot read follows the same dial** (ruling 3): the dispatcher refuses it under `strict`, and under `warn`/`silent` it is logged (with a warning under `warn`) and read as a pass. Both decisions are `EnforcementPolicy.Decide` over `Legality.OutsideLegalSet` in `CommandDispatcher.Answer`, so the unreadable case and the out-of-set case are one rule and not two (Task 4).
- Identifiers are `Guid.CreateVersion7()`. One world unit = one inch. C# only.
- Tests: xUnit, headless, over `InMemoryEventLog`/`EventApplier` and the real `RulesEngine` driven through the interpreter plan's `Sim` with a queue-fed `IDiceSource` and `FakeRules` (Grimdark Future's hooks, empty action sequences, so an activation's frame finishes at once); both shapes tested for two teams and for N teams of one; one socket end-to-end test against the Docker Postgres (collection `Postgres`).
- Commit after every task, one-line imperative message, with the `Co-Authored-By` trailer naming the model.

---

## File Structure

```
server/game/
  Rules/IGameRules.cs                               modified: the scheduler's hook members, added beside the interpreter plan's
  Dispatch/CommandDispatcher.cs                     modified: Answer probes a turn answer's readability, so the dial decides an unreadable one (Task 4)
  Rules/GrimdarkFuture/GfRules.Scheduling.cs        this plan's partial of the skeleton's GrimdarkFutureRules: phases, first team, activatable units and their actions, answer reading
  Rules/GrimdarkFuture/GfRules.Deployment.cs        deploy and scout legal sets, the Ambusher round-boundary loop
  Program.cs                                        modified: SchedulerFactory registered as Engine.ISchedulerFactory in place of NoSchedulerFactory
  Scheduler/SchedulerTypes.cs                       read, not written: the interpreter plan's Task 8 declares TurnAsk, Activation, TurnContext, HookResult, FirstTeamDecision
  Scheduler/ActivationOption.cs                     read, not written: the interpreter plan's Task 8 declares the activate option id "{unitId:N}/{action}"
  Scheduler/TeamUnits.cs                            which units are a team's, through the participant; shelf and table checks
  Scheduler/SchedulerLog.cs                         effective-log slices: since round, since phase, since turn; scheduler Asks
  Scheduler/RollOff.cs                              starts the ruleset's roll-off sequence through the interpreter and reads the order back from its Values once the frame has finished
  Scheduler/TurnCycle.cs                            one activation cycle read from the log and driven forward
  Scheduler/SchedulerBase.cs                        Engine.IScheduler: phases before round one, the round boundary, next team, the shared Advance
  Scheduler/AlternatingActivationScheduler.cs
  Scheduler/PlayerTurnScheduler.cs
  Scheduler/SchedulerFactory.cs                     Engine.ISchedulerFactory: picks a shape from GameInfo.SchedulerShape
tests/Tabletop.GameServer.Tests/
  Infrastructure/ScriptedStarter.cs                 ISequenceStarter fake for the TurnCycle and RollOff unit tests: records what was started
  Infrastructure/TestArmies.cs                      army builder: N units × M models, named rules per unit
  Infrastructure/TestGames.cs                       GameInfo + Board + GameState for N teams; Apply; Place (event) and Move (command)
  Infrastructure/SchedulerHarness.cs                a Sim (the interpreter plan's) over the real RulesEngine, queue-fed dice; Answer, Deploy, Rehydrate
  Infrastructure/FakeRules.cs                       the merged IGameRules with Grimdark Future's bodies, settable hooks, and empty action sequences
  ActivationOptionTests.cs, SchedulerLogTests.cs, GrimdarkFutureRulesTests.cs, RollOffTests.cs, TurnCycleTests.cs,
  AlternatingSchedulerTests.cs, DeploymentTests.cs, RoundBoundaryTests.cs, PlayerTurnSchedulerTests.cs,
  ReactionOrderingTests.cs, SchedulerConstraintTests.cs, SchedulerFactoryTests.cs, SchedulerEndToEndTests.cs
CLAUDE.md                                           modified: one Layout child bullet naming server/game/Scheduler/ (Task 10)
```

`Scheduler/` is a new folder beside `Rules/` and `Dispatch/` because the scheduler is generic engine code, not a ruleset (scheduler → The line). `Rules/GrimdarkFuture/` holds the one ruleset: the skeleton's `GfRules.cs` declares `GrimdarkFutureRules` (`RulesId`, `Instance`, `Id`) as `sealed partial`, the interpreter plan's `GfRules.Sequences.cs` and `Gf*.cs` hold its sequences, rolls and hooks, and this plan's two partial files hold the turn machinery's hooks. No file in this plan is named `GrimdarkFutureRules.cs` or `GrimdarkFutureNames.cs` (the names class is the interpreter plan's `GfNames.cs`, which already declares every name this plan reads and gains nothing here), and no `RulesEngine`, `ISequenceInterpreter`, `IReactionOrdering` or `InterfaceConstraintTests` exists here — those names belong to the interpreter plan.

---
### Task 1: The scheduler's types, the option id and the log reader

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

### Task 2: `IGameRules` gains the scheduler's hooks; `GrimdarkFutureRules` implements them

**Files:**
- Modify: `server/game/Rules/IGameRules.cs` (add the scheduler's members beside the interpreter plan's)
- Read, do not modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` — the interpreter plan's Task 11 declares every name this plan uses; land it first
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Scheduling.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Deployment.cs`, `server/game/Scheduler/TeamUnits.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestArmies.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/TestGames.cs`, `tests/Tabletop.GameServer.Tests/GrimdarkFutureRulesTests.cs`

**Interfaces:**
- Consumes: Task 1's types, `SchedulerLog`, `ActivationOption`; `UnitInPlay`, `ModelInPlay`, `Location`, `OnShelf`, `PropertyInstance`, `Grant` (Protocol/Content); the skeleton's `GrimdarkFutureRules` (`sealed partial`, `Rules/GrimdarkFuture/GfRules.cs`); the interpreter plan's `Effects.Lookup.UnitHas(GameState, PropertySet, Guid unitId, string entry)` (Task 6 — own properties, aggregated model properties, and the grants store), `PropertySet`, `TestProperties.Core`, and its three hooks `OnRoundStarted`/`EndOfTurnSequences`/`OnTurnEnded` with their `GfHooks` bodies (Task 11) — this plan implements none of those three.
- Produces: `IGameRules` members `IReadOnlyList<string> PhasesBeforeRounds { get; }`, `IReadOnlyList<string> PhasesInTurn { get; }`, `FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams)`, `TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId)`, `Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer)`, `bool ActivationComplete(GameState state, Activation activation)`, `HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order)`, `IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams)` — and no others: what this plan first drafted as `RoundStartChanges`, `EndOfTurn` and `TurnEndChanges` is the interpreter plan's `OnRoundStarted(GameState, PropertySet, int round)`, `EndOfTurnSequences(GameState, PropertySet, TurnContext)` and `OnTurnEnded(GameState, PropertySet, TurnContext)`, one body each in `GfHooks` (the merged interface is written in full in the skeleton plan → Interfaces → Produces); `GrimdarkFutureRules` partials `GfRules.Scheduling.cs` and `GfRules.Deployment.cs` (no `RulesId`, `Instance` or `Id` — the skeleton's `GfRules.cs` has them) with `internal static IReadOnlyList<string> ActionsFor(GameState, PropertySet, UnitInPlay, IReadOnlyList<string> rows)` and `internal static IReadOnlyList<Guid> Ambushers(GameState, PropertySet, Guid teamId)` (this task adds nothing to `GfNames`: `Deployment`, `Scouts`, `Rounds`, `Deploy`, `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `SetAside` are declared there by the interpreter plan's Task 11, beside `Hold`, `Advance`, `Rush`, `Charge`, `Shaken`, `Fatigued`, `MoraleTest`, `Melee`, `RollOffSeq`, `TokenCap` — redeclaring any of them is a duplicate, not a gap); `static class TeamUnits { Guid? TeamOf(GameState, Guid participantId); IReadOnlyList<UnitInPlay> Of(GameState, Guid teamId); bool AllShelved(GameState, UnitInPlay); bool AnyOnTable(GameState, UnitInPlay); IReadOnlyList<ModelInPlay> ModelsOf(GameState, UnitInPlay); int IndexOf(IReadOnlyList<Guid> order, Guid team); }`. Test infrastructure `TestArmies.Build(string name, int units, int modelsPerUnit, (int Unit, string Rule, int? Parameter)[]? unitRules = null, (int Unit, string Rule, int? Parameter)[]? modelRules = null)`; `TestGames.Build(IReadOnlyList<Army?> armyPerTeam, SchedulerShape shape = AlternatingActivation, Enforcement enforcement = Warn, bool waits = false, int spectators = 0, IGameRules? rules = null)` returning `TestGames.Built(GameState State, GameInfo Info, IReadOnlyList<Guid> Teams, IReadOnlyList<Guid> Participants)`; `TestGames.Apply(Built, params EventPayload[])`; `TestGames.Relocations(GameState, Guid unitId)` and, over it, `TestGames.Place` (the `ModelsMoved` event, for `Apply`/`Append`) and `TestGames.Move` (the identical `MoveModels` command, for `Dispatch`) putting every model of the unit on the table.

Spec: scheduler → The line (the right-hand column is this interface), The turn is an Ask (legal set = activatable units × actions; `Immobile` means Hold only; Shaken means idle), Deployment (shelf minus set-aside; Scouts after every other unit), The round boundary (fatigue, spell tokens, Ambushers "starting with the player that activates next", nothing written to the record), What the interface must not assume 1 (first team is a policy: finished-first for two teams, roll-off every round for free-for-all — page 50 "including the first"), 6 (reaction ordering replaceable). Action sequences → End-of-activation hook (half or less of starting size or Tough; melee units compare instead), Fatigue (cleared at the round boundary), The token economy (+X, capped at 6, even off the table). Rulebook page 10: Shaken units "must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation"; page 13 `Aircraft` "May only use Advance actions", `Ambush`; page 14 `Immobile`, `Scout`. Set-aside is a condition on the shelved unit (scheduler → Deployment), written from the menu with `SetCondition` like any other property (game flow → Nothing is an invariant); no phase Asks for it.

- [ ] **Step 1: Write the test infrastructure `TestArmies.cs` and `TestGames.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestArmies
{
    /// <summary>
    /// units × modelsPerUnit, fresh ids. unitRules go on the unit; modelRules on every model of that unit (1-based unit numbers).
    /// Put a rule on the side its catalog entry is declared for, or Lookup.UnitHas silently answers false: in TestProperties.Core
    /// Aircraft, Scout and Ambush are kind unit (no aggregation, so the model path can never match), Immobile is kind model with
    /// aggregation all, and Caster and Tough are read per model by Lookup.ModelParameter — so the first four go in unitRules and the
    /// last two in modelRules, which is what every fixture below does.
    /// </summary>
    public static Army Build(string name, int units, int modelsPerUnit,
        (int Unit, string Rule, int? Parameter)[]? unitRules = null, (int Unit, string Rule, int? Parameter)[]? modelRules = null)
    {
        var list = new List<Unit>();
        for (var u = 1; u <= units; u++)
        {
            var mr = (modelRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList();
            var models = Enumerable.Range(1, modelsPerUnit).Select(m => new ModelSlot(Guid.CreateVersion7(), m, $"{name} {u}.{m}",
                new Dictionary<string, string>(), mr, Base.Circle(1.26), 1.5, null, [])).ToList();
            list.Add(new Unit(Guid.CreateVersion7(), u, $"{name} unit {u}", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" },
                (unitRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList(), models));
        }
        return new Army(Guid.CreateVersion7(), name, "grimdark-future", "test", list);
    }
}
```

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestGames
{
    public sealed record Built(GameState State, GameInfo Info, IReadOnlyList<Guid> Teams, IReadOnlyList<Guid> Participants);

    /// <summary>One team per entry, one participant each; a null army is a team that brought nothing. Spectators are participants with no team.</summary>
    public static Built Build(IReadOnlyList<Army?> armyPerTeam, SchedulerShape shape = SchedulerShape.AlternatingActivation,
        Enforcement enforcement = Enforcement.Warn, bool waits = false, int spectators = 0, IGameRules? rules = null, long rngSeed = 7)
    {
        var teams = armyPerTeam.Select((_, i) => new Team(Guid.CreateVersion7(), i + 1)).ToList();
        var participants = teams.Select((t, i) => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Player {i + 1}", t.Id, armyPerTeam[i]?.Id)).ToList();
        participants.AddRange(Enumerable.Range(0, spectators).Select(i => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Watcher {i}", null, null)));
        var info = new GameInfo(Guid.CreateVersion7(), "Test", GameStatus.Playing, participants[0].AccountId, GrimdarkFutureRules.RulesId, "sha256:test",
            shape, enforcement, waits, rngSeed, teams, participants);                        // RngSeed is positional (contracts plan): a build that forgot it would not compile
        var armies = participants.Zip(armyPerTeam, (p, a) => (p, a)).Where(x => x.a is not null)
            .Select(x => new ArmyInPlay(x.p.Id, ArmySnapshot.Of(x.a!, Guid.CreateVersion7()))).ToList();
        var board = new Board(TableStateTests.Sample(), armies);
        var state = new GameState(info, board, new EventApplier(board, info), rules ?? GrimdarkFutureRules.Instance);
        return new Built(state, info, teams.Select(t => t.Id).ToList(), participants.Select(p => p.Id).ToList());
    }

    public static void Apply(Built game, params EventPayload[] payloads)
    {
        foreach (var p in payloads) game.State.Applier.Apply(new GameEvent(game.State.Projection.LastSeq + 1, null, p, null));
    }

    /// <summary>Every model of the unit onto the table, in a row. Place is the event (for Apply/Append); Move is the identical command (for Dispatch).</summary>
    public static IReadOnlyList<ModelRelocation> Relocations(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(6 + i * 2, 6, 0), new Yaw(0))))).ToList();

    public static ModelsMoved Place(GameState state, Guid unitId) => new(Relocations(state, unitId));
    public static MoveModels Move(GameState state, Guid unitId) => new(Relocations(state, unitId));
}
```

- [ ] **Step 2: Write the failing tests `GrimdarkFutureRulesTests.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class GrimdarkFutureRulesTests
{
    static readonly GrimdarkFutureRules Rules = GrimdarkFutureRules.Instance;
    static readonly PropertySet Core = TestProperties.Core;                                    // its action rows: Hold, Advance, Rush, Charge
    static readonly string[] Rows = ["Hold", "Advance", "Rush", "Charge"];
    static Subject UnitSubject(Guid id) => new(SubjectKind.Unit, id);

    /// <summary>Team 1: three units of three (unit 2 is a Scout, unit 3 an Ambusher); team 2: two units, the second Immobile with one Tough(6) Caster(2) model.</summary>
    static TestGames.Built Game(SchedulerShape shape = SchedulerShape.AlternatingActivation) => TestGames.Build([
        TestArmies.Build("A", 3, 3, unitRules: [(2, Scout, null), (3, Ambush, null)]),
        TestArmies.Build("B", 2, 1, unitRules: [(2, Immobile, null)], modelRules: [(2, Tough, 6), (2, Caster, 2)])], shape);
    static IReadOnlyList<Guid> Units(TestGames.Built g, int team) => TeamUnits.Of(g.State, g.Teams[team]).Select(u => u.Id).ToList();

    [Fact]
    public void Deployment_offers_the_shelf_minus_set_aside_and_scouts_offers_the_set_aside_scouts()
    {
        var g = Game(); var a = Units(g, 0);
        TestGames.Apply(g, new PhaseStarted(Deployment), new ConditionSet(UnitSubject(a[1]), SetAside, true), new ConditionSet(UnitSubject(a[2]), SetAside, true));
        var ask = Rules.LegalTurn(g.State, Core, g.Teams[0])!;
        Assert.Equal((AskQuestions.Deploy, false), (ask.Question, ask.AllowPass));
        Assert.Equal([a[0]], Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        TestGames.Apply(g, TestGames.Place(g.State, a[0]));
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[0]));                               // nothing left but set-aside units
        TestGames.Apply(g, new PhaseStarted(Scouts));
        var scouts = Rules.LegalTurn(g.State, Core, g.Teams[0])!;
        Assert.Equal(AskQuestions.DeployScouts, scouts.Question);
        Assert.Equal([a[1]], Assert.IsType<PieceSelection>(scouts.Legal).Eligible);            // the Ambusher waits for the round boundary
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[1]));                               // team 2 has no scouts
    }

    [Fact]
    public void Deploy_answers_read_a_unit_and_complete_when_every_model_is_placed()
    {
        var g = Game(); var a = Units(g, 0);
        TestGames.Apply(g, new PhaseStarted(Deployment));
        var act = Rules.ReadAnswer(g.State, g.Teams[0], new SelectedPieces([a[0]]))!;
        Assert.Equal((a[0], Deploy, false, 0), (act.UnitId, act.Action, act.RunsSequence, act.Changes.Count));
        Assert.False(Rules.ActivationComplete(g.State, act));
        var models = g.State.Projection.Units[a[0]].Definition.Models;
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(models[0].Id, new OnShelf(), new OnTable(new(new(1, 1, 0), new(0))))]));
        Assert.False(Rules.ActivationComplete(g.State, act));
        TestGames.Apply(g, TestGames.Place(g.State, a[0]));
        Assert.True(Rules.ActivationComplete(g.State, act));
        Assert.Null(Rules.ReadAnswer(g.State, g.Teams[0], new SelectedPieces([Guid.CreateVersion7()])));
    }

    [Fact]
    public void Activation_offers_units_on_the_table_with_no_record_and_the_actions_their_rules_allow()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        TestGames.Apply(g, new PhaseStarted(Rounds), TestGames.Place(g.State, a[0]), TestGames.Place(g.State, a[1]), TestGames.Place(g.State, b[0]), TestGames.Place(g.State, b[1]),
            new ActionRecorded(a[1], "Advance"), new ConditionSet(UnitSubject(b[0]), Shaken, true));
        var teamA = Assert.IsType<Options>(Rules.LegalTurn(g.State, Core, g.Teams[0])!.Legal).Choices;
        Assert.Equal(Rows.Select(r => ActivationOption.Id(a[0], r)), teamA.Select(c => c.Id));                 // a[1] activated, a[2] shelved
        var teamB = Assert.IsType<Options>(Rules.LegalTurn(g.State, Core, g.Teams[1])!.Legal).Choices;
        Assert.Equal([ActivationOption.Id(b[0], ActivationOption.Idle), ActivationOption.Id(b[1], "Hold")], teamB.Select(c => c.Id));  // Shaken idles; Immobile holds
        TestGames.Apply(g, new ActionRecorded(a[0], "Hold"));
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[0]));
        var act = Rules.ReadAnswer(g.State, g.Teams[1], new Chosen(ActivationOption.Id(b[1], "Hold")))!;
        Assert.Equal((b[1], "Hold", true), (act.UnitId, act.Action, act.RunsSequence));
        Assert.Equal(new ActionRecorded(b[1], "Hold"), act.Changes.Single());
        var idle = Rules.ReadAnswer(g.State, g.Teams[1], new Chosen(ActivationOption.Id(b[0], ActivationOption.Idle)))!;
        Assert.False(idle.RunsSequence); Assert.True(Rules.ActivationComplete(g.State, idle));
        Assert.Null(Rules.ReadAnswer(g.State, g.Teams[1], new Chosen("garbage")));
    }

    [Fact]
    public void Aircraft_may_only_advance_even_when_shaken()
    {
        var g = TestGames.Build([TestArmies.Build("A", 1, 1, unitRules: [(1, Aircraft, null)])]);
        var unit = g.State.Projection.Units.Values.Single();
        Assert.Equal(["Advance"], GrimdarkFutureRules.ActionsFor(g.State, Core, unit, Rows));
        TestGames.Apply(g, new ConditionSet(UnitSubject(unit.Id), Shaken, true));
        Assert.Equal(["Advance"], GrimdarkFutureRules.ActionsFor(g.State, Core, unit, Rows));      // p.13: "even if Shaken"
    }

    [Fact]
    public void First_team_is_a_roll_off_before_round_one_then_the_winner_then_whoever_finished_first()
    {
        var g = Game(); var teams = g.Teams;
        Assert.Equal(RollOffSeq, Assert.IsType<RollOffNeeded>(Rules.FirstTeam(g.State, 0, teams)).Sequence);
        TestGames.Apply(g, new RoundStarted(0, [teams[1], teams[0]], []));
        Assert.Equal([teams[1], teams[0]], Assert.IsType<OrderedTeams>(Rules.FirstTeam(g.State, 1, teams)).Order);
        TestGames.Apply(g, new RoundStarted(1, [teams[1], teams[0]], []),
            new TurnStarted(teams[1]), new TurnEnded(teams[1], false, []), new TurnStarted(teams[0]), new TurnEnded(teams[0], false, []),
            new TurnStarted(teams[1]), new TurnEnded(teams[1], false, []));                       // team 1 activated last, so team 0 finished first
        Assert.Equal([teams[0], teams[1]], Assert.IsType<OrderedTeams>(Rules.FirstTeam(g.State, 2, teams)).Order);
        var three = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
        TestGames.Apply(three, new RoundStarted(0, three.Teams, []));
        Assert.IsType<RollOffNeeded>(Rules.FirstTeam(three.State, 0, three.Teams));                // p.6 and p.50: deployment order is a roll-off
        Assert.IsType<OrderedTeams>(Rules.FirstTeam(three.State, 1, three.Teams));                 // p.50: three players order like two, no round roll-off
        Assert.IsType<OrderedTeams>(Rules.FirstTeam(three.State, 2, three.Teams));
    }

    [Fact]
    public void Round_start_clears_fatigue_and_grants_capped_spell_tokens()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        var caster = g.State.Projection.Units[b[1]].Definition.Models[0].Id;
        TestGames.Apply(g, new ConditionSet(UnitSubject(a[0]), Fatigued, true), new SpellTokensSet(caster, 5));
        Assert.Empty(Rules.OnRoundStarted(g.State, Core, 0));                                           // the interpreter plan's hook, called by SchedulerBase.BeginRound
        var changes = Rules.OnRoundStarted(g.State, Core, 2);
        Assert.Contains(new ConditionSet(UnitSubject(a[0]), Fatigued, false), changes);
        Assert.Contains(new SpellTokensSet(caster, 6), changes);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void End_of_turn_tests_morale_at_half_strength_except_after_melee_and_turn_end_resets_the_counter()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        var models = g.State.Projection.Units[a[0]].Definition.Models;
        TestGames.Apply(g, new TurnStarted(g.Teams[1]), TestGames.Place(g.State, a[0]), new WoundsThisActivationSet(a[0], 1),
            new ModelsMoved([new ModelRelocation(models[0].Id, new OnTable(new(new(1, 1, 0), new(0))), new OnShelf())]));
        var turn = new TurnContext(g.Teams[1], new Activation(b[0], "Hold", true, []), 1, 1);
        Assert.Empty(Rules.EndOfTurnSequences(g.State, Core, turn));                                    // 2 of 3 left: over half (the interpreter plan's hook)
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(models[1].Id, new OnTable(new(new(3, 1, 0), new(0))), new OnShelf())]));
        var started = Rules.EndOfTurnSequences(g.State, Core, turn);
        Assert.Equal((MoraleTest, a[0]), (started.Single().Sequence, started.Single().Context.ActorUnitId));
        TestGames.Apply(g, new SequenceStarted(Guid.CreateVersion7(), Melee, new SequenceContext(b[0], [a[0]], [], null)));
        Assert.Empty(Rules.EndOfTurnSequences(g.State, Core, turn));                                    // fought this melee: the melee compares wounds instead
        var tough = g.State.Projection.Units[b[1]].Definition.Models[0].Id;
        TestGames.Apply(g, new TurnStarted(g.Teams[0]), TestGames.Place(g.State, b[1]), new WoundsThisActivationSet(b[1], 3), new WoundsSet(tough, 3));
        Assert.Single(Rules.EndOfTurnSequences(g.State, Core, new TurnContext(g.Teams[0], null, 1, 1)));   // Tough(6) at 3 wounds: half
        var reset = Rules.OnTurnEnded(g.State, Core, new TurnContext(g.Teams[0], null, 1, 1));
        Assert.Equal([new WoundsThisActivationSet(a[0], 0), new WoundsThisActivationSet(b[1], 0)], reset.OrderBy(c => ((WoundsThisActivationSet)c).UnitId == b[1]).ToList());
        TestGames.Apply(g, new ConditionSet(UnitSubject(b[0]), Shaken, true));
        var idle = new TurnContext(g.Teams[1], new Activation(b[0], ActivationOption.Idle, false, []), 1, 1);
        Assert.Contains(new ConditionSet(UnitSubject(b[0]), Shaken, false), Rules.OnTurnEnded(g.State, Core, idle));
    }

    [Fact]
    public void Ambushers_are_asked_from_round_two_starting_with_the_first_team_alternating_until_all_pass()
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 1, unitRules: [(1, Ambush, null), (2, Ambush, null)]), TestArmies.Build("B", 1, 1, unitRules: [(1, Ambush, null)])]);
        var a = Units(g, 0); var b = Units(g, 1); var order = new[] { g.Teams[1], g.Teams[0] };
        TestGames.Apply(g, new ConditionSet(UnitSubject(a[0]), SetAside, true), new ConditionSet(UnitSubject(a[1]), SetAside, true), new ConditionSet(UnitSubject(b[0]), SetAside, true));
        Assert.IsType<HookDone>(Rules.RoundBoundaryStep(g.State, Core, 1, order));
        TestGames.Apply(g, new RoundStarted(2, order, []));
        var first = Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order));
        var ask = Assert.IsType<AskOpened>(first.Events.Single()).Ask;
        Assert.Equal((g.Teams[1], AskQuestions.DeployAmbusher, true), (ask.Teams.Single(), ask.Question, ask.AllowPass));
        Assert.Equal([b[0]], Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        TestGames.Apply(g, first.Events.Single());
        Assert.IsType<HookWaiting>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                    // open
        TestGames.Apply(g, new AskClosed(ask.Id, [new TeamAnswer(g.Teams[1], new SelectedPieces([b[0]]))]));
        Assert.IsType<HookWaiting>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                    // models still on the shelf
        TestGames.Apply(g, TestGames.Place(g.State, b[0]));
        var second = Assert.IsType<AskOpened>(Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order)).Events.Single()).Ask;
        Assert.Equal([g.Teams[0]], second.Teams);
        TestGames.Apply(g, new AskOpened(second), new AskClosed(second.Id, [new TeamAnswer(g.Teams[0], new Passed())]));
        Assert.IsType<HookDone>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                       // team 1 has none left, team 0 passed
        Assert.Empty(g.State.Projection.Units[b[0]].Actions);                                      // deploying wrote nothing to the record
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GrimdarkFutureRules`; expect compile errors — `IGameRules` has none of these members. Extend `IGameRules.cs`** — add `using Tabletop.GameServer.Scheduler;` and these members to the interface body (the interpreter plan's Task 8 adds its block the same way; the skeleton's `Id`, `UnknownGameRulesException` and `GameRulesRegistry` stay as they are):

```csharp
    // Added by the scheduler plan. Every hook reads state.Projection and the log and returns; none holds state.

    /// <summary>Phases before round one, in order; each alternates one unit a turn. OPR: deployment, scouts. Empty: rounds start at once.</summary>
    IReadOnlyList<string> PhasesBeforeRounds { get; }
    /// <summary>Phases inside one team's turn under the player-turn shape, in order. Empty means one unnamed phase (OPR).</summary>
    IReadOnlyList<string> PhasesInTurn { get; }
    /// <summary>Who goes first in <paramref name="round"/> (0 is the phase before round one). Teams are in ordinal order. RollOffNeeded names the ruleset sequence that decides.</summary>
    FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams);
    /// <summary>The team's turn Ask now, read from the phase in state.Projection.Scheduler.Phase; the action rows are properties.Actions; null when its legal set is empty.</summary>
    TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId);
    /// <summary>What a turn answer means; null when it names nothing (treated as a pass).</summary>
    Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer);
    /// <summary>For an activation with no sequence: is its work done (deploy: every model placed).</summary>
    bool ActivationComplete(GameState state, Activation activation);
    /// <summary>The interactive round-boundary step, called each Advance after round_started until HookDone (OPR: Ambushers).</summary>
    HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order);
    /// <summary>Order for simultaneous reactions, or null for Windows.ReactionOrder's default: turn order starting after the actor, passed teams dropped.</summary>
    IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams);
```

What rides on `round_started` and `turn_ended`, and what runs at the end of an activation, are the interpreter plan's `OnRoundStarted(state, properties, round)`, `OnTurnEnded(state, properties, turn)` and `EndOfTurnSequences(state, properties, turn)` — the scheduler calls those three and adds nothing of its own beside them.

- [ ] **Step 4: Check `GfNames.cs` already carries the scheduler's names; write `TeamUnits.cs`**

Nothing is added to `GfNames`. The interpreter plan's Task 11 declares the whole block this plan reads — confirm these lines are in `server/game/Rules/GrimdarkFuture/GfNames.cs` before writing anything that uses them, and if that plan's Task 11 has not landed, land it first rather than declaring a second names class:

```csharp
    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts", Rounds = Scheduler.SchedulerBase.RoundsPhase;   // phases
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough";
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Which units are a team's: through the participant that brought them. A participant with no team owns units no team may use.</summary>
public static class TeamUnits
{
    public static Guid? TeamOf(GameState state, Guid participantId) => state.Info.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
    public static IReadOnlyList<UnitInPlay> Of(GameState state, Guid teamId) =>
        state.Projection.Units.Values.Where(u => TeamOf(state, u.ParticipantId) == teamId).OrderBy(u => u.Definition.Ordinal).ToList();
    public static IReadOnlyList<ModelInPlay> ModelsOf(GameState state, UnitInPlay unit) => unit.Definition.Models.Select(m => state.Projection.Models[m.Id]).ToList();
    public static bool AllShelved(GameState state, UnitInPlay unit) => ModelsOf(state, unit).All(m => m.Location is OnShelf);
    public static bool AnyOnTable(GameState state, UnitInPlay unit) => ModelsOf(state, unit).Any(m => m.Location is not OnShelf);
    public static int IndexOf(IReadOnlyList<Guid> order, Guid team) { for (var i = 0; i < order.Count; i++) if (order[i] == team) return i; return -1; }
}
```

`GfNames.Rounds` is `SchedulerBase.RoundsPhase` so the two spellings cannot drift, which makes `GfNames.cs` depend on this plan's Task 5: land Task 5's `SchedulerBase.cs` constant first (it has no other dependency), or leave `GfNames.Rounds` as the literal `"rounds"` until it does. `GrimdarkFutureRules` itself is the skeleton's `sealed partial class` in `GfRules.cs`; the skeleton's tests already import `Tabletop.GameServer.Rules.GrimdarkFuture`.

- [ ] **Step 5: Write `GfRules.Scheduling.cs`**

Name checks go through the interpreter plan's `Lookup.UnitHas(state, properties, unitId, entry)` (own properties, aggregated model properties, the grants store — whole-design review → name checks read the grants store) rather than a second reading of the unit here, so `LegalTurn` and `RoundBoundaryStep` take the pinned `PropertySet` as the interpreter plan's hooks do; the action rows are `properties.Actions` (`ActionDefinition.Name`, catalog order: `Hold`, `Advance`, `Rush`, `Charge`).

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The scheduler plan's partial: phases, first team, the turn's legal set and how an answer is read. Every member is a pure reading of GameState.</summary>
public sealed partial class GrimdarkFutureRules
{
    public IReadOnlyList<string> PhasesBeforeRounds { get; } = [Deployment, Scouts];
    public IReadOnlyList<string> PhasesInTurn { get; } = [];
    /// <summary>No OPR rule orders simultaneous reactions; Windows.ReactionOrder's default stands (scheduler → Windows).</summary>
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams) => null;

    /// <summary>
    /// Page 6: deployment order is a roll-off, and page 50 keeps one for a free-for-all's deploy order. Turn order is not a roll-off
    /// at any team count: page 7 gives round one to the team that deployed first and every later round to whoever finished activating
    /// first, and page 50 says the same for 3 and 4 players. One rule for two teams and for many, so nothing branches on the team count.
    /// </summary>
    public FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams)
    {
        if (round == 0) return new RollOffNeeded(RollOffSeq);
        var previous = state.Projection.Scheduler.Order.Count > 0 ? state.Projection.Scheduler.Order : teams;
        if (round == 1) return new OrderedTeams(previous);
        var log = SchedulerLog.Of(state);
        var ended = log.LastTurnEndedByTeam(log.SinceRound);           // the round just finished: its round_started is still the last
        return new OrderedTeams(previous.OrderBy(t => ended.GetValueOrDefault(t, 0L)).ThenBy(t => TeamUnits.IndexOf(previous, t)).ToList());
    }

    public TurnAsk? LegalTurn(GameState state, PropertySet props, Guid teamId)
    {
        var phase = state.Projection.Scheduler.Phase;
        if (phase is Deployment or Scouts) return DeployAsk(state, props, teamId, phase);
        var actionRows = props.Actions.Select(a => a.Name).ToList();
        var options = TeamUnits.Of(state, teamId)
            .Where(u => u.Actions.Count == 0 && TeamUnits.AnyOnTable(state, u))
            .SelectMany(u => ActionsFor(state, props, u, actionRows).Select(a => ActivationOption.For(u, a))).ToList();
        return options.Count == 0 ? null : new TurnAsk(AskQuestions.Activate, "Activate a unit", new Options(options), AllowPass: true);
    }

    /// <summary>
    /// Page 13: Aircraft "may only use Advance actions ... (even if Shaken)", and "their mandatory move doesn't break the staying idle
    /// requirement to stop being Shaken" — so Aircraft is read before Shaken, and a Shaken Aircraft is offered Advance, not Idle.
    /// Page 10: every other Shaken unit "must spend their activation being idle". Page 14: Immobile means Hold only.
    /// </summary>
    internal static IReadOnlyList<string> ActionsFor(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<string> actionRows)
    {
        if (Lookup.UnitHas(state, props, unit.Id, Aircraft)) return actionRows.Where(a => a == Advance).ToList();
        if (unit.Conditions.Contains(Shaken)) return [ActivationOption.Idle];
        if (Lookup.UnitHas(state, props, unit.Id, Immobile)) return actionRows.Where(a => a == Hold).ToList();
        return actionRows;
    }

    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => answer switch
    {
        SelectedPieces { PieceIds: [var unit] } when state.Projection.Units.ContainsKey(unit) => new Activation(unit, Deploy, false, []),
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit)
            => new Activation(unit, action, action != ActivationOption.Idle, [new ActionRecorded(unit, action)]),
        _ => null,
    };

    /// <summary>Deploying ends when the last model of the unit has a placement (scheduler → Deployment). Idle is complete at once.</summary>
    public bool ActivationComplete(GameState state, Activation activation) =>
        activation.Action != Deploy || !TeamUnits.ModelsOf(state, state.Projection.Units[activation.UnitId]).Any(m => m.Location is OnShelf);
}
```

Fatigue clearing and spell tokens at `round_started`, the wounds-this-activation reset and Idle-clears-Shaken at `turn_ended`, and the end-of-activation morale tests are the interpreter plan's `GfHooks.OnRoundStarted`, `GfHooks.OnTurnEnded(state, turn)` and `GfHooks.EndOfTurnSequences` (its Task 11), reached through `IGameRules.OnRoundStarted`, `OnTurnEnded` and `EndOfTurnSequences`; this plan writes no body for them.

- [ ] **Step 6: Write `GfRules.Deployment.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>Deployment: the shelf minus set-aside units. Scouts: the set-aside Scouts, after every other unit (page 14). One unit per Ask; no pass.</summary>
    private static TurnAsk? DeployAsk(GameState state, PropertySet props, Guid teamId, string phase)
    {
        var units = TeamUnits.Of(state, teamId).Where(u => TeamUnits.AllShelved(state, u));
        units = phase == Deployment
            ? units.Where(u => !u.Conditions.Contains(SetAside))
            : units.Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Scout));
        var ids = units.Select(u => u.Id).ToList();
        if (ids.Count == 0) return null;
        return phase == Deployment
            ? new TurnAsk(AskQuestions.Deploy, "Deploy one unit", new PieceSelection(ids, 1, 1), AllowPass: false)
            : new TurnAsk(AskQuestions.DeployScouts, "Deploy a Scout", new PieceSelection(ids, 1, 1), AllowPass: false);
    }

    internal static IReadOnlyList<Guid> Ambushers(GameState state, PropertySet props, Guid teamId) =>
        TeamUnits.Of(state, teamId).Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Ambush) && TeamUnits.AllShelved(state, u)).Select(u => u.Id).ToList();

    /// <summary>
    /// Page 13: from round two, Ambushers deploy "starting with the player that activates next", alternating, until every team has passed
    /// or has none left. Read from the deploy_ambusher Asks since round_started; a pass stands for this boundary; nothing is written to the record.
    /// </summary>
    public HookResult RoundBoundaryStep(GameState state, PropertySet props, int round, IReadOnlyList<Guid> order)
    {
        if (round < 2) return new HookDone();
        var log = SchedulerLog.Of(state);
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployAmbusher).ToList();
        var passed = new HashSet<Guid>();
        foreach (var a in asks)
        {
            if (state.Projection.OpenAsks.ContainsKey(a.Ask.Id)) return new HookWaiting();
            var answer = since.OfType<AskClosed>().First(c => c.AskId == a.Ask.Id).Answers.FirstOrDefault()?.Answer;
            if (answer is SelectedPieces { PieceIds: [var unit] } && state.Projection.Units.TryGetValue(unit, out var u) && TeamUnits.AllShelved(state, u))
                return new HookWaiting();                                            // models still coming off the shelf
            if (answer is not SelectedPieces) passed.Add(a.Ask.Teams[0]);
        }
        var eligible = order.Where(t => !passed.Contains(t) && Ambushers(state, props, t).Count > 0).ToList();
        if (eligible.Count == 0) return new HookDone();
        var next = eligible[0];
        if (asks.Count > 0)
        {
            var lastIndex = TeamUnits.IndexOf(order, asks[^1].Ask.Teams[0]);
            next = eligible.FirstOrDefault(t => TeamUnits.IndexOf(order, t) > lastIndex, eligible[0]);
        }
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [next],
            new PieceSelection(Ambushers(state, props, next), 1, 1), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);
        return new HookEmit([new AskOpened(ask)]);
    }
}
```

- [ ] **Step 7: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect every earlier test still green and 8 new passed. Commit: `Give GameRules the scheduler's hooks and implement them for Grimdark Future`.**

### Task 3: The roll-off — started through the interpreter, read back from its values

**Files:**
- Create: `server/game/Scheduler/RollOff.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/ScriptedStarter.cs`, `tests/Tabletop.GameServer.Tests/RollOffTests.cs`

**Interfaces:**
- Consumes: `ISequenceStarter.Start` (interpreter plan Task 8); `SchedulerLog` (Task 1); `SequenceStarted`, `StepCompleted` (with `Values`), `SequenceFinished`, `SequenceAbandoned`, `SequenceContext` (Protocol); `GameInfo.Teams` (`Team.Ordinal`). The roll-off sequence itself is the interpreter plan's `GfRolls.RollOffSequence` (its Task 11): two steps, `GfNames.RollOffRollStep` (`"roll-off"`) — one die per team in ordinal order, ties rerolled among the tied until every team is ordered — and `GfNames.RollOffOrderStep` (`"order"`), which ranks them and carries `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]`, team ordinals best first. The confirm gate sits between the two. This plan rolls no dice and names neither step: it takes the frame's last completed step carrying `place_1`.
- Produces: `static class RollOff` with `abstract record Progress` and `Emit(IReadOnlyList<EventPayload> Events)`, `Waiting`, `Resolved(IReadOnlyList<Guid> Order)`; `static Progress Advance(GameState state, SchedulerLog log, string sequence, ISequenceStarter starter)`; `static IReadOnlyList<Guid> Order(GameState state, StepCompleted? step)`. Every branch is read from the effective log — `Waiting` is *no `sequence_finished` for the started frame*, not *the frame is on `Projection.Stack`* — so the reader answers the same way whoever calls it and however the engine's ladder routes a gated frame. Test infrastructure `ScriptedStarter : ISequenceStarter` with `List<(string Sequence, SequenceContext Context)> Started` (records and emits one `sequence_started`, validating nothing — for the `TurnCycle` and `RollOff` unit tests; the harness in Task 5 uses the real interpreter).

Spec: scheduler → Deployment ("The roll-off is the roll primitive with no threshold — one die per team, raw results compared, ties rerolled among the tied"; "an ordinary sequence on the roll primitive, logged"), Events (replay applies `round_started.Order`; the roll-off is never re-derived on replay). Page 6: "the players roll-off and the winner must start deploying"; page 50: "starting with the player that got the highest score, and then alternating in descending order". The scheduler starts the sequence `FirstTeamDecision.RollOffNeeded` names with no actor and reads its `place_k` values into the order on a later call. **The roll-off is gated like any other roll (ruling 7).** `GfRolls.RollOffSequence` is two steps, `roll-off` then `order`; the boundary between them is an unsealed `Confirm` Ask to every team, opened whatever the wait setting says, because the frame has no actor and no parent (interpreter plan Task 8, `GateTeams`; its Interfaces needed 4). So the roll-off spans three dispatches rather than one batch: the one that starts the sequence and rolls, the confirm — with any number of `SetDice` edits before it, which is the point of the gate — and the `Advance` after `sequence_finished` that writes `round_started`. The `order` step ranks the dice that stand at the confirm, so the edit decides the order without anything rewriting a logged `step_completed`. The reader carries nothing between the three and re-derives its answer from the log every time: `Advance` keys `Resolved` on `sequence_finished`, never on the presence of a `step_completed`, and hands `Order` the frame's last completed step **that carries `place_1`**, which is the `order` step and never the `roll-off` step. A roll-off abandoned from the menu is started again.

- [ ] **Step 1: Write `ScriptedStarter.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>An ISequenceStarter that records what was asked for and emits the sequence_started, checking nothing — the TurnCycle and RollOff unit tests drive the frame's life by hand.</summary>
public sealed class ScriptedStarter : ISequenceStarter
{
    public List<(string Sequence, SequenceContext Context)> Started { get; } = new();

    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        Started.Add((sequence, context));
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }
}
```

- [ ] **Step 2: Write the failing tests `RollOffTests.cs`**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class RollOffTests
{
    static TestGames.Built Three() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
    /// <summary>The ranking step, which runs after the gate and carries the values (GfNames.RollOffOrderStep).</summary>
    static StepCompleted Places(Guid frame, params int[] ordinals) =>
        new(frame, GfNames.RollOffOrderStep, null, [], []) { Values = ordinals.Select((o, i) => ($"place_{i + 1}", o)).Append(("winner_ordinal", ordinals[0])).ToDictionary(x => x.Item1, x => x.Item2) };
    /// <summary>The rolling step, which carries dice and no values: the reader must not mistake it for the ranking (GfNames.RollOffRollStep).</summary>
    static StepCompleted Rolled(Guid frame) => new(frame, GfNames.RollOffRollStep, null, [], []);
    static SchedulerLog Log(TestGames.Built g) => SchedulerLog.Of(g.State);

    [Fact]
    public void Order_reads_place_values_as_team_ordinals_best_first_and_fills_in_teams_the_values_miss()
    {
        var g = Three(); var t = g.Teams; var frame = Guid.CreateVersion7();
        Assert.Equal([t[1], t[2], t[0]], RollOff.Order(g.State, Places(frame, 2, 3, 1)));
        Assert.Equal([t[2], t[0], t[1]], RollOff.Order(g.State, Places(frame, 3)));                      // a hand-edited log: the rest follow in ordinal order
        Assert.Equal([t[0], t[1], t[2]], RollOff.Order(g.State, null));                                  // no step at all: ordinal order
        Assert.Equal([t[1], t[0], t[2]], RollOff.Order(g.State, Places(frame, 2, 2, 9)));                // duplicates and unknown ordinals are skipped
    }

    [Fact]
    public void With_no_roll_off_since_the_round_anchor_the_named_sequence_is_started_with_no_actor()
    {
        var g = Three(); var starter = new ScriptedStarter();
        var emit = Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));
        var started = Assert.IsType<SequenceStarted>(Assert.Single(emit.Events));
        Assert.Equal((GfNames.RollOffSeq, (Guid?)null, (string?)null), (started.Sequence, started.Context.ActorUnitId, started.Context.Action));
        Assert.Equal([(GfNames.RollOffSeq, started.Context)], starter.Started);
    }

    [Fact]
    public void A_live_frame_waits_a_finished_one_resolves_and_an_abandoned_one_is_started_again()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));   // started and unfinished: the whole interval a confirm gate holds
        TestGames.Apply(g, Places(frame, 3, 1, 2), new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);
        TestGames.Apply(g, new RoundStarted(0, [t[2], t[0], t[1]], []));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // a new round anchor: the old roll-off does not count
        var again = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(again, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), new SequenceAbandoned(again));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // abandoned from the menu: start over
        Assert.Equal(3, starter.Started.Count);
    }

    [Fact]
    public void A_gated_roll_off_waits_behind_its_confirm_and_resolves_from_the_ranking_step_when_the_frame_finishes()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Confirm", g.Teams, new Confirmation(), false, false, null, frame);
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), Rolled(frame), new AskOpened(gate));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // rolled, and the gate holds the frame with the dice editable
        TestGames.Apply(g, new AskClosed(gate.Id, [new TeamAnswer(t[0], new Confirmed())]), Places(frame, 3, 1, 2));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // confirmed and ranked, but the frame has not finished: still nothing to read
        TestGames.Apply(g, new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);   // the ranking step, not the rolling one
        Assert.Empty(starter.Started);                                                                    // waiting starts nothing, so no second roll-off is ever begun
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `RollOff.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// The roll-off is a ruleset sequence (FirstTeamDecision.RollOffNeeded names it; OPR's is GfRolls.RollOffSequence) run by the
/// interpreter like any other. The scheduler starts it with no actor and, once its frame has finished, reads the descending order
/// from its step_completed.Values place_1..place_n — team ordinals, best first (scheduler → Deployment; p.50). Replay applies
/// round_started.Order and never reads the values again.
/// It is confirm-gated like any other roll: the sequence is two steps, and the boundary before the second is an
/// every-team Confirm the interpreter opens whatever the wait setting says, so the frame stays open with its dice editable and the
/// order is read only once sequence_finished stands. Waiting is that whole interval. The values live on the step that carries
/// place_1 — the ranking step, which runs after the gate — so an edited die decides the order and no logged step is ever rewritten.
/// </summary>
public static class RollOff
{
    /// <summary>The one key convention between a ruleset's roll-off and this reader: place_1..place_n, team ordinals best first (interfaces needed 5). It also identifies the ranking step among the frame's completed steps.</summary>
    static string Place(int k) => $"place_{k}";

    public abstract record Progress;
    public sealed record Emit(IReadOnlyList<EventPayload> Events) : Progress;
    public sealed record Waiting : Progress;
    public sealed record Resolved(IReadOnlyList<Guid> Order) : Progress;

    /// <summary>Since the last round_started (the whole log before the first): no roll-off → start one; abandoned → start again; started and unfinished → wait, which is where a confirm gate holds it; finished → its order.</summary>
    public static Progress Advance(GameState state, SchedulerLog log, string sequence, ISequenceStarter starter)
    {
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var started = since.OfType<SequenceStarted>().LastOrDefault(s => s.Sequence == sequence);
        if (started is null || since.Any(p => p is SequenceAbandoned a && a.FrameId == started.FrameId))
            return new Emit(starter.Start(state, sequence, new SequenceContext(null, [], [], null)));
        if (!since.Any(p => p is SequenceFinished f && f.FrameId == started.FrameId)) return new Waiting();
        return new Resolved(Order(state, since.OfType<StepCompleted>().LastOrDefault(s => s.FrameId == started.FrameId && s.Values?.ContainsKey(Place(1)) is true)));
    }

    /// <summary>place_1..place_n as team ids; ordinals the values do not name (a hand-edited log; the team list itself cannot change after the flip to playing) follow in ordinal order.
    /// The step handed in is the frame's last completed step carrying place_1 — the ranking step, which the interpreter runs after the confirm gate, over whatever dice stand then.</summary>
    public static IReadOnlyList<Guid> Order(GameState state, StepCompleted? step)
    {
        var teams = state.Info.Teams.OrderBy(t => t.Ordinal).ToList();
        var placed = new List<Guid>();
        for (var k = 1; step?.Values is { } values && values.TryGetValue(Place(k), out var ordinal); k++)
            if (teams.FirstOrDefault(t => t.Ordinal == ordinal) is { } team && !placed.Contains(team.Id)) placed.Add(team.Id);
        return placed.Concat(teams.Select(t => t.Id).Where(t => !placed.Contains(t))).ToList();
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter RollOff`; expect 4 passed. Commit: `Start the ruleset's roll-off through the interpreter and read the order back`.**

### Task 4: The activation cycle — Ask, answer, activation, sequence, end-of-turn hook

**Files:**
- Create: `server/game/Scheduler/TurnCycle.cs`
- Modify: `server/game/Dispatch/CommandDispatcher.cs` (the skeleton's, Task 6 there) — `Answer` probes a turn answer's readability, so the enforcement dial decides an unreadable one (ruling 3)
- Test: `tests/Tabletop.GameServer.Tests/TurnCycleTests.cs`; modify `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs` (its `activate` fixture's option id, Step 3)

**Interfaces:**
- Consumes: `SchedulerLog`, `Activation`, `TurnAsk`, `TurnContext` (Task 1); `IGameRules.LegalTurn/ReadAnswer/ActivationComplete` (Task 2); the interpreter plan's `IGameRules.ActionSequence` (null: no sequence) and `EndOfTurnSequences(state, properties, turn)`, `ISequenceStarter.Start`, `PropertySet`; the skeleton's `LegalSetMembership.Contains`, `Legality`, `Verdict`, `EnforcementPolicy.Decide` and `GameInfo.Enforcement`; test infrastructure `ScriptedStarter` (Task 3), `TestProperties.Core`.
- Produces: `abstract record CycleState` with `NoAsk`, `AskOpen(Ask Ask)`, `PassAnswered(long AnsweredAt)`, `NeedsStart(Activation Activation, long AnsweredAt)`, `Running(Activation Activation, long AnsweredAt, Guid FrameId)`, `WaitingCompletion(Activation Activation, long AnsweredAt)`, `ActivationEnded(Activation Activation, long AnsweredAt, long EndedAt)`; `abstract record CycleStep` with `CycleEmit(IReadOnlyList<EventPayload> Events)`, `CycleWaiting`, `CycleEmpty`, `CyclePassed(TurnContext Turn)`, `CycleComplete(TurnContext Turn)`; `sealed class TurnCycle(PropertySet properties)` with `CycleState Read(GameState state, Guid teamId, SchedulerLog log)` and `CycleStep Step(GameState state, Guid teamId, SchedulerLog log, ISequenceStarter starter, Func<TurnAsk, TurnAsk>? shape = null)`; `static Ask TurnCycle.AskFor(Guid teamId, TurnAsk ask)`. One modified member: `CommandDispatcher.Answer` computes `Legality` from set membership **and** readability, so an unreadable turn answer follows the dial.

Spec: scheduler → The turn is an Ask (the answer names a unit and an action; the scheduler starts that sequence; the activation is written to the record when the sequence starts, so an abandoned sequence still counts; when the sequence finishes the end-of-turn hook runs, then the turn ends; hand moves are not an answer; passing is a real answer; out-of-set answers proceed as if legal), Deployment (the turn ends when the last model has a placement). Action sequences → End-of-activation hook (a morale sequence the scheduler waits for). Everything is read from the effective log since the last `turn_started` (or a `phase_started` inside the turn, under player-turn phases): the last scheduler Ask, its close, what was written after it, whether the root frame is still on the stack, whether a hook sequence started after the activation ended. **An answer that names nothing follows the enforcement dial** (ruling 3), exactly as an out-of-set answer does. The decision is one line in `CommandDispatcher.Answer` rather than a branch in `TurnCycle`: the dispatcher already holds `state.Info.Enforcement` and is the only place a refusal can still leave the Ask open, so under `strict` an unreadable answer is refused before `ask_answered` is written, and under `warn`/`silent` it is written (with `OutOfSet = true` under `warn`, which is the warning) and the scheduler reads it as a pass. `TurnCycle.Read` therefore maps `ReadAnswer` returning null to `PassAnswered` whatever the dial says, which is what keeps a rebuilt state independent of a setting a player may since have changed. Under the player-turn shape the same reader serves each activation in the turn, because the *last* scheduler Ask since `turn_started` is always the current cycle's. The round boundary's `end_round` Ask (Task 5) is a scheduler Ask too, but it can never fall in this slice: it is opened only when no turn is open, and the one thing that opens a turn over it — `set_active_team` — logs a `turn_started` that moves `CycleStartAt` past it (and closes it as stale in the same `Advance`). So `TurnCycle` needs no case for it.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class TurnCycleTests
{
    readonly ScriptedStarter _starter = new();
    static TurnCycle Cycle => new(TestProperties.Core);
    static Subject U(Guid id) => new(SubjectKind.Unit, id);

    /// <summary>Two teams of two three-model units, everything on the table, round one in progress, team 0's turn started.</summary>
    static TestGames.Built InRounds(out Guid team, out IReadOnlyList<Guid> units, Enforcement enforcement = Enforcement.Warn)
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 3), TestArmies.Build("B", 2, 3)], enforcement: enforcement);
        foreach (var u in g.State.Projection.Units.Keys.ToList()) TestGames.Apply(g, TestGames.Place(g.State, u));
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []), new PhaseStarted(GfNames.Rounds), new TurnStarted(g.Teams[0]));
        team = g.Teams[0]; units = TeamUnits.Of(g.State, team).Select(u => u.Id).ToList();
        return g;
    }
    CycleStep Step(TestGames.Built g, Guid team, TurnCycle cycle) => cycle.Step(g.State, team, SchedulerLog.Of(g.State), _starter);
    static Ask Opened(CycleStep step) => ((AskOpened)Assert.IsType<CycleEmit>(step).Events.Single()).Ask;
    static void Answer(TestGames.Built g, Ask ask, Guid team, AskAnswer answer) => TestGames.Apply(g, new AskAnswered(ask.Id, team, answer, false), new AskClosed(ask.Id, [new TeamAnswer(team, answer)]));

    [Fact]
    public void A_turn_opens_an_activate_ask_then_waits_starts_the_sequence_waits_and_completes_when_it_finishes()
    {
        var g = InRounds(out var team, out var units);
        var ask = Opened(Step(g, team, Cycle));
        Assert.Equal((AskQuestions.Activate, true, null), (ask.Question, ask.AllowPass, ask.FrameId)); Assert.Equal([team], ask.Teams);
        Assert.Equal(8, Assert.IsType<Options>(ask.Legal).Choices.Count);                              // 2 units × 4 actions
        TestGames.Apply(g, new AskOpened(ask));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, TestGames.Place(g.State, units[0]));                                         // a hand move changes nothing
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], "Advance")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[0], "Advance"), start[0]);
        var frame = Assert.IsType<SequenceStarted>(start[1]);
        Assert.Equal((units[0], "Advance"), (frame.Context.ActorUnitId, frame.Context.Action));
        TestGames.Apply(g, start.ToArray());
        Assert.IsType<Running>(Cycle.Read(g.State, team, SchedulerLog.Of(g.State)));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceStarted(Guid.CreateVersion7(), "shooting", new SequenceContext(units[0], [], [], null)));   // a nested frame
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceFinished(frame.FrameId));                                        // pops both
        var done = Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Equal((team, units[0], "Advance"), (done.Turn.TeamId, done.Turn.Activation!.UnitId, done.Turn.Activation.Action));
        Assert.Equal(g.State.Projection.LastSeq, done.Turn.ActivationEndedAtSeq);
    }

    [Fact]
    public void Passing_is_a_real_answer_and_so_is_an_answer_that_names_nothing()
    {
        var g = InRounds(out var team, out _);
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Passed());
        Assert.IsType<CyclePassed>(Step(g, team, Cycle));
        TestGames.Apply(g, new TurnStarted(team));
        var again = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(again));
        Answer(g, again, team, new Chosen("not-an-option"));
        Assert.IsType<CyclePassed>(Step(g, team, Cycle));                                                 // under warn and silent: logged, then read as a pass
        var strict = InRounds(out var st, out _, Enforcement.Strict);                                      // the same logged answer, in a game since switched to strict
        var third = Opened(Step(strict, st, Cycle)); TestGames.Apply(strict, new AskOpened(third));
        Answer(strict, third, st, new Chosen("not-an-option"));
        Assert.IsType<CyclePassed>(Step(strict, st, Cycle));                                               // the reader does not consult the dial: a rebuilt state cannot depend on a setting a player may have changed since
    }

    [Fact]
    public void Idle_and_an_unknown_action_write_the_record_and_complete_without_a_sequence()
    {
        var g = InRounds(out var team, out var units);
        TestGames.Apply(g, new ConditionSet(U(units[0]), GfNames.Shaken, true));
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], ActivationOption.Idle)));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[0], ActivationOption.Idle), Assert.Single(start));
        TestGames.Apply(g, start.ToArray());
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Empty(_starter.Started);
        TestGames.Apply(g, new TurnStarted(team));
        var second = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(second));
        Answer(g, second, team, new Chosen(ActivationOption.Id(units[1], "Teleport")));                 // out of set, accepted under warn
        var recorded = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[1], "Teleport"), Assert.Single(recorded));                 // the interpreter had no sequence for it
        TestGames.Apply(g, recorded.ToArray());
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
    }

    [Fact]
    public void The_end_of_turn_hook_runs_once_and_the_turn_completes_when_its_sequence_finishes()
    {
        var g = InRounds(out var team, out var units);
        var enemy = TeamUnits.Of(g.State, g.Teams[1])[0];
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], "Hold")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events; TestGames.Apply(g, start.ToArray());
        var frame = ((SequenceStarted)start[1]).FrameId;
        var models = enemy.Definition.Models;
        TestGames.Apply(g, new WoundsThisActivationSet(enemy.Id, 2),
            new ModelsMoved([new ModelRelocation(models[0].Id, g.State.Projection.Models[models[0].Id].Location, new OnShelf()), new ModelRelocation(models[1].Id, g.State.Projection.Models[models[1].Id].Location, new OnShelf())]),
            new SequenceFinished(frame));
        var hook = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        var morale = Assert.IsType<SequenceStarted>(Assert.Single(hook));
        Assert.Equal((GfNames.MoraleTest, enemy.Id), (morale.Sequence, morale.Context.ActorUnitId));
        TestGames.Apply(g, hook.ToArray());
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceFinished(morale.FrameId));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Equal(2, _starter.Started.Count);                                                          // the action, then the morale test — asked for once each
    }

    [Fact]
    public void An_abandoned_activation_still_counts_and_ends_the_turn()
    {
        var g = InRounds(out var team, out var units);
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[1], "Rush")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events; TestGames.Apply(g, start.ToArray());
        TestGames.Apply(g, new SequenceAbandoned(((SequenceStarted)start[1]).FrameId));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Contains("Rush", g.State.Projection.Units[units[1]].Actions);
    }

    [Fact]
    public void An_empty_legal_set_is_reported_and_deployment_waits_for_every_model()
    {
        var g = TestGames.Build([TestArmies.Build("A", 1, 2), TestArmies.Build("B", 1, 1)]);
        var team = g.Teams[0]; var unit = TeamUnits.Of(g.State, team)[0];
        TestGames.Apply(g, new RoundStarted(0, g.Teams, []), new PhaseStarted(GfNames.Deployment), new TurnStarted(team));
        var ask = Opened(Step(g, team, Cycle));
        Assert.Equal((AskQuestions.Deploy, false), (ask.Question, ask.AllowPass));
        TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new SelectedPieces([unit.Id]));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        var m = unit.Definition.Models;
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(m[0].Id, new OnShelf(), new OnTable(new(new(2, 2, 0), new(0))))]));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(m[1].Id, new OnShelf(), new OnTable(new(new(4, 2, 0), new(0))))]));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        TestGames.Apply(g, new TurnEnded(team, false, []), new TurnStarted(team));
        Assert.IsType<CycleEmpty>(Step(g, team, Cycle));                                                // nothing left to deploy
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter TurnCycle`; expect compile errors. Write `TurnCycle.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Where one activation stands, read from the log since turn_started.</summary>
public abstract record CycleState;
public sealed record NoAsk : CycleState;
public sealed record AskOpen(Ask Ask) : CycleState;
public sealed record PassAnswered(long AnsweredAt) : CycleState;
public sealed record NeedsStart(Activation Activation, long AnsweredAt) : CycleState;
public sealed record Running(Activation Activation, long AnsweredAt, Guid FrameId) : CycleState;
public sealed record WaitingCompletion(Activation Activation, long AnsweredAt) : CycleState;
public sealed record ActivationEnded(Activation Activation, long AnsweredAt, long EndedAt) : CycleState;

/// <summary>What the scheduler does about it.</summary>
public abstract record CycleStep;
public sealed record CycleEmit(IReadOnlyList<EventPayload> Events) : CycleStep;
public sealed record CycleWaiting : CycleStep;
/// <summary>No Ask yet and the legal set is empty: the team is skipped.</summary>
public sealed record CycleEmpty : CycleStep;
public sealed record CyclePassed(TurnContext Turn) : CycleStep;
/// <summary>The activation's work and the end-of-turn hook are both done.</summary>
public sealed record CycleComplete(TurnContext Turn) : CycleStep;

/// <summary>
/// One activation: Ask → answer → the activation's changes and its sequence → the end-of-turn sequences → complete. Nothing is held
/// between calls: the last scheduler Ask since turn_started is the cycle, what follows it in the log says how far it got. Sequences are
/// started through the interpreter's ISequenceStarter; the pinned set is what LegalTurn and EndOfTurnSequences read.
/// </summary>
public sealed class TurnCycle(PropertySet properties)
{
    public static Ask AskFor(Guid teamId, TurnAsk ask) => new(Guid.CreateVersion7(), ask.Question, ask.Prompt, [teamId], ask.Legal, ask.AllowPass, Sealed: false, WindowKind: null, FrameId: null);

    public CycleState Read(GameState state, Guid teamId, SchedulerLog log)
    {
        var slice = log.SinceCycle;                                                                     // the turn, or the phase started inside it
        var found = log.LastSchedulerAsk(slice);
        if (found is null) return new NoAsk();
        var (opened, _, closed, closedAt) = found.Value;
        if (closed is null) return new AskOpen(opened.Ask);
        var answer = (closed.Answers.FirstOrDefault(a => a.TeamId == teamId) ?? closed.Answers.FirstOrDefault())?.Answer;
        if (answer is null or Passed) return new PassAnswered(closedAt);
        var act = state.Rules.ReadAnswer(state, teamId, answer);
        if (act is null) return new PassAnswered(closedAt);                                                // names nothing: a pass, and under strict the dispatcher refused it, so nothing like this is ever in the log of a strict game

        var after = slice.Where(e => e.Seq > closedAt).ToList();
        var started = act.Changes.Count == 0 || act.Changes.Any(c => after.Any(e => e.Payload.Equals(c)));
        if (!act.RunsSequence)
        {
            if (!started) return new NeedsStart(act, closedAt);
            return state.Rules.ActivationComplete(state, act) ? new ActivationEnded(act, closedAt, closedAt) : new WaitingCompletion(act, closedAt);
        }
        var start = after.FirstOrDefault(e => e.Payload is SequenceStarted s && s.Context.ActorUnitId == act.UnitId && s.Context.Action == act.Action);
        if (start is null) return started ? new ActivationEnded(act, closedAt, closedAt) : new NeedsStart(act, closedAt);
        var frameId = ((SequenceStarted)start.Payload).FrameId;
        if (state.Projection.Stack.Any(f => f.Id == frameId)) return new Running(act, closedAt, frameId);
        var end = after.FirstOrDefault(e => (e.Payload is SequenceFinished f && f.FrameId == frameId) || (e.Payload is SequenceAbandoned a && a.FrameId == frameId));
        return new ActivationEnded(act, closedAt, end?.Seq ?? start.Seq);
    }

    /// <param name="starter">The interpreter: starts the declared action's sequence and the end-of-turn ones.</param>
    /// <param name="shape">The shape's last word on the Ask (player-turn asks act-or-end-phase); identity when null.</param>
    public CycleStep Step(GameState state, Guid teamId, SchedulerLog log, ISequenceStarter starter, Func<TurnAsk, TurnAsk>? shape = null)
    {
        switch (Read(state, teamId, log))
        {
            case NoAsk:
                var ask = state.Rules.LegalTurn(state, properties, teamId);
                return ask is null ? new CycleEmpty() : new CycleEmit([new AskOpened(AskFor(teamId, shape is null ? ask : shape(ask)))]);
            case AskOpen or Running or WaitingCompletion:
                return new CycleWaiting();
            case PassAnswered p:
                return new CyclePassed(new TurnContext(teamId, null, p.AnsweredAt, p.AnsweredAt));
            case NeedsStart n:
            {
                var events = new List<EventPayload>(n.Activation.Changes);
                if (n.Activation.RunsSequence && state.Rules.ActionSequence(n.Activation.Action) is { } sequence)   // null: an out-of-set action, recorded and nothing more
                    events.AddRange(starter.Start(state, sequence, new SequenceContext(n.Activation.UnitId, [], [], n.Activation.Action)));
                return new CycleEmit(events);
            }
            case ActivationEnded e:
            {
                var turn = new TurnContext(teamId, e.Activation, e.AnsweredAt, e.EndedAt);
                var hookRan = log.SinceCycle.Any(ev => ev.Seq > e.EndedAt && ev.Payload is SequenceStarted);
                if (hookRan) return state.Projection.Stack.Count == 0 ? new CycleComplete(turn) : new CycleWaiting();
                var starts = state.Rules.EndOfTurnSequences(state, properties, turn).SelectMany(s => starter.Start(state, s.Sequence, s.Context)).ToList();
                return starts.Count == 0 ? new CycleComplete(turn) : new CycleEmit(starts);
            }
            default: throw new NotSupportedException();
        }
    }
}
```

"The end-of-turn sequences ran" is read from the log as a `sequence_started` after the activation ended, so `EndOfTurnSequences` is called once per turn; an empty answer completes the turn at once. The `Running`/`WaitingCompletion` states are reached only from these unit tests — `RulesEngine.Advance` runs the interpreter while a frame exists and never calls the scheduler then.

- [ ] **Step 3: Put the enforcement dial in front of an unreadable turn answer.** In the skeleton's `Dispatch/CommandDispatcher.cs`, `Answer` computes one `Legality` from two questions (ruling 3):

```csharp
        var inSet = LegalSetMembership.Contains(ask, command.Answer);
        // An answer to a turn Ask that the ruleset cannot read — an option id that does not parse, a unit the log no longer has —
        // is outside the legal set as much as an unknown option is, so it takes the same three positions under the enforcement dial.
        // Passing is a real answer and is never probed; a framed Ask, an Ambusher Ask and the boundary confirmation are not turn Asks.
        var readable = command.Answer is Passed || !SchedulerLog.IsTurnAsk(ask) || state.Rules.ReadAnswer(state, team, command.Answer) is not null;
        var detail = inSet ? $"answer to '{ask.Question}' names nothing this ruleset can read" : $"answer to '{ask.Question}' is outside the legal set";
        var verdict = EnforcementPolicy.Decide(state.Info.Enforcement, inSet && readable ? Legality.Legal : Legality.OutsideLegalSet, detail);
        if (verdict is Refuse r) return new Refused(r.Reason, r.Detail);
        var events = new List<EventPayload> { new AskAnswered(ask.Id, team, command.Answer, !inSet || !readable) };
```

The file gains `using Tabletop.GameServer.Scheduler;` for `SchedulerLog`. Nothing else in `Answer` changes, and no signature does: the enforcement setting is already there as `state.Info.Enforcement`, and `ReadAnswer` needs `GameState` and the answering team, which are the arguments the method already has — so there is nothing to thread. `ask_answered.OutsideLegalSet` carries the `warn` warning for both halves, which is what the client already renders. Under `strict` nothing is appended and the Ask stays open, so a client that keeps sending an unreadable answer keeps being refused and the same turn stands; nothing waits on a turn ending, because the round boundary is a player's own Ask (Task 5) and the engine rests while an unframed Ask of the scheduler's is live.

One fixture in the skeleton's `CommandDispatcherTests` moves with it, and must: its `Activate` sample offers the option id `"a"`, which is in its legal set and unreadable, so under `strict` it would be refused where that file expects `Applied`. Give the sample a real activation option id — `new Options([new AskOption(Samples.Unit.ToString("N") + "/Advance", "Advance", [Samples.Unit])])` — and let the two `new Chosen("a")` answers in `An_in_set_answer_answers_and_closes_an_unsealed_ask` follow it. `Chosen("zzz")` stays as it is: out of set under either reading. No test is added or removed there, and the fixture is truer for it, since an `activate` Ask's option ids are the scheduler's.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter TurnCycle`; expect 6 passed. Then run `--filter CommandDispatcher`; expect all green with the fixture above. Commit: `Drive one activation from Ask to end-of-turn hook by reading the log`.**

### Task 5: `SchedulerBase` and the alternating-activation shape

**Files:**
- Create: `server/game/Scheduler/SchedulerBase.cs`, `server/game/Scheduler/AlternatingActivationScheduler.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/SchedulerHarness.cs`, `tests/Tabletop.GameServer.Tests/AlternatingSchedulerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; `CommandDispatcher`, `Applied`, `Refused`, `Actor` (skeleton); `RoundStarted`, `TurnStarted`, `TurnEnded`, `PhaseStarted`, `ActionRecordCleared` (Protocol); the interpreter plan's `Engine.IScheduler`, `Engine.RulesEngine`, `Interpreter.ISequenceStarter`, `Properties.PropertySet`, `Dice.IDiceSource`, `IGameRules.OnRoundStarted/OnTurnEnded`, and its test infrastructure `Sim`, `TestProperties.Core`, `LambdaStep`, `StubRules` (the shape `FakeRules` follows).
- Produces: `abstract class SchedulerBase(PropertySet properties) : Engine.IScheduler` with `const string RoundsPhase = "rounds"`, `IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter)`, protected `PropertySet Properties`, `TurnCycle Cycle`, `Guid? NextTeam(GameState, SchedulerLog, IReadOnlyList<Guid> order)`, `IReadOnlyList<EventPayload> EndTurn(GameState, TurnContext turn, bool passed)`, virtual `bool IsFinished(GameState, SchedulerLog, Guid teamId)` (passed this round, or empty legal set), virtual `IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState, int round)` and `ShapeTurnEndChanges(GameState, TurnContext)` (empty), virtual `IReadOnlyList<EventPayload> BeforeFirstAsk(GameState, SchedulerLog, Guid teamId)` (empty), `TurnAsk ShapeAsk(GameState, TurnAsk)` (identity), `IReadOnlyList<EventPayload> OnEmpty(GameState, SchedulerLog, TurnContext)` (ends the turn), abstract `IReadOnlyList<EventPayload> OnPassed(GameState, SchedulerLog, TurnContext)` and `OnComplete(GameState, SchedulerLog, TurnContext)`, and `static Ask EndRoundAsk(IReadOnlyList<Guid> teams)` with the private `bool RoundEnded(GameState, SchedulerLog, IReadOnlyList<Guid> teams, out IReadOnlyList<EventPayload> gate)` that opens it and reads its answer; `sealed class AlternatingActivationScheduler(PropertySet) : SchedulerBase`. Test infrastructure `FakeRules : IGameRules` — the whole merged interface: Grimdark Future's bodies by default, except `PhasesBeforeRounds`/`PhasesInTurn` (settable, default empty), the settable hooks `FirstTeamHook` (default: the teams as given), `LegalTurnHook`, `RoundBoundaryHook` (default `HookDone`), `EndOfTurnHook`, `OrderReactionsHook`, and `SequenceHook` (default: the roll-off is Grimdark Future's real sequence; every other name is an **empty** `Sequence`, which the interpreter finishes in one `Advance` with no `step_completed`, so the harness's kind lists hold), plus `static FakeRules Gf()` — the same with Grimdark Future's `PhasesBeforeRounds`, `FirstTeam` and `RoundBoundaryStep`, the harness's default; `QueueDice : IDiceSource` (`Queue<int> Dice`; dequeues per draw, then a seeded `Random`); and `SchedulerHarness(IReadOnlyList<Army?> armies, Func<PropertySet, IScheduler>? scheduler = null, Enforcement enforcement = Warn, bool waits = false, int spectators = 0, IGameRules? rules = null, SchedulerShape shape = AlternatingActivation)` — a `Sim` over the real `RulesEngine(rules, TestProperties.Core, QueueDice, scheduler)` — with `GameState State`, `Queue<int> Dice`, `IReadOnlyList<Guid> Teams`, `IReadOnlyList<Guid> Units(int team)`, `IReadOnlyList<EventPayload> Run()`, `IReadOnlyList<EventPayload> Dispatch(Command command, int? team = null)`, `IReadOnlyList<EventPayload> Answer(int team, AskAnswer answer)`, `IReadOnlyList<EventPayload> Deploy(Guid unitId)`, `Ask? OpenAsk(int team)`, `Ask Open`, `IReadOnlyList<EventPayload> EndRound(int team = 0)` (answers the boundary Ask with `Confirmed`), `IReadOnlyList<EventPayload> ConfirmRollOff(int team = 0)` (releases the roll-off's confirm gate, which every roll-off waits at), `void SkipDeployment()` (which releases any gate it meets), `void Rehydrate()`, `IReadOnlyList<string> Kinds(long afterSeq)`, `IReadOnlyList<GameEvent> Log`, `Projection P`. Its default rules are `FakeRules.Gf()` — Grimdark Future's scheduling hooks with every action sequence empty, so an activation's frame finishes in the same `Advance` it starts and the kind lists below hold.

Spec: scheduler → The two shapes (alternating: one activation per turn; the order from the first-team policy at `round_started`; turns rotate skipping finished teams; a team finishes when its legal set is empty or it passes; the round ends when every team has finished; the record clears at `round_started`), Deployment (one unit a turn, alternating; a team with nothing to deploy is skipped; the phase ends when every team is skipped — the spec describes the phase once, not per shape, so the alternation is the same under both), The round boundary (hooks after the order is set and before the first turn), Re-entering a round, Nothing is an invariant (`SetActiveTeam`'s `turn_started` opens an Ask; `SetRound`'s `round_started` runs the boundary), There is no round limit, Enforcement (`strict` refuses an out-of-set answer at the dispatcher, so the scheduler never sees it and the turn stands; `warn`/`silent` let it through and the scheduler acts as if it were legal — Task 4 covers that half, and an unreadable answer follows the same dial by the same route (ruling 3), proved here as a theory over the three positions); a scheduler Ask left open by a menu edit that started another turn or round closes with no answers (the one `ask_closed` the engine emits, and never for an answer). **The round does not advance on its own — a player ends it** (ruling 4): when no team can take another turn the scheduler opens the `end_round` Ask to every team that has a participant and waits, and the first answer is what makes `BeginRound(round + 1)` run. That is one rule for both the ordinary boundary and the degenerate one — a round nobody could act in waits at the same Ask, so rounds never count up on their own and the engine still rests. A menu edit while that Ask is open does not withdraw it (an Ask stands as logged); `set_active_team` supersedes it with a turn and `set_round` with a round, closing it with no answers, and if it was closed unanswered the next boundary opens a fresh one. Rotation restarts from `Order[0]` at a round or phase start (page 14: Scouts "starting with the player that activates next"), otherwise continues after the last `turn_ended`.

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
    static string Activate(Guid unit, string action = "Hold") => ActivationOption.Id(unit, action);

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
        seq = h.P.LastSeq; h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
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
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(Activate(a[0], "Advance")));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((a[0], "Advance", "advance"), h.Log.Select(e => e.Payload).OfType<SequenceStarted>().Select(s => (s.Context.ActorUnitId, s.Context.Action, s.Sequence)).Single(s => s.Action is not null));   // the roll-off is the other frame
        Assert.Contains("Advance", h.P.Units[a[0]].Actions);
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(Activate(b[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Single(Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());   // only a[1] remains
        seq = h.P.LastSeq; h.Answer(0, new Chosen(Activate(a[1])));
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
        h.Answer(0, new Chosen(Activate(a[0]))); h.Answer(1, new Chosen(Activate(b[0]))); h.Answer(0, new Chosen(Activate(a[1])));
        var seq = h.P.LastSeq; h.Answer(1, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.True(((TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload).Passed);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(b[0]));                                                          // a menu edit while B has passed
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId); Assert.Single(h.P.OpenAsks);
        h.Answer(0, new Chosen(Activate(a[2])));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                 // B was not re-asked: the pass stood, and the round waits
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);                                      // B finished (passed) before A's last turn
        h.Answer(1, new Chosen(Activate(b[0]))); h.Answer(0, new Chosen(Activate(a[0])));
        h.Answer(1, new Chosen(Activate(b[1]))); h.Answer(0, new Chosen(Activate(a[1])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // B's legal set is empty: A goes again
        h.Dispatch(new ClearActionRecord(b[0]));
        h.Answer(0, new Chosen(Activate(a[2])));
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
        Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen(Activate(a[0], "Teleport"))));   // OutsideLegalSet under strict: refused by the dispatcher
        Assert.Equal(seq, h.P.LastSeq);                                                                        // nothing logged, so the scheduler never saw it
        Assert.Equal((ask.Id, h.Teams[0]), (h.Open.Id, h.P.Scheduler.ActiveTeamId!.Value));                     // the same Ask stands
        h.Answer(0, new Chosen(Activate(a[0])));
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
            h.Answer(0, new Chosen(Activate(h.Units(0)[0])));                                                   // a readable answer moves the game on
            Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
            return;
        }
        h.Answer(0, new Chosen("not-an-option"));
        Assert.True(((AskAnswered)h.Log.First(e => e.Seq > seq && e.Payload is AskAnswered).Payload).OutsideLegalSet);   // the warn warning rides on this; the string itself is the dispatcher's, pinned by CommandDispatcherTests
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.Equal((h.Teams[0], true), (ended.TeamId, ended.Passed));                                        // read as a pass, which stands for the round
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(Activate(h.Units(1)[0])));
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
        h.Answer(0, new Chosen(Activate(a0)));
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
        Assert.All(asked.Where(a => a.Question != AskQuestions.EndRound), a => Assert.Equal([h.Teams[0]], a.Teams));   // only A is ever asked to deploy or activate
        Assert.All(asked.Where(a => a.Question == AskQuestions.EndRound), a => Assert.Equal(h.Teams, a.Teams));        // both teams may end a round; the spectator has no team and is in no Ask
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
            case OrderedTeams o: order = o.Order; break;
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
        round == 0 ? [] : state.Projection.Units.Values.Where(u => u.Actions.Count > 0).Select(u => (StateChange)new ActionRecordCleared(u.Id)).ToList();
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter AlternatingScheduler`; expect 10 passed (7 facts + the dial's 3 theory cases). Commit: `Add the scheduler base and the alternating-activation shape`.**

### Task 6: Deployment and the round boundary end to end — Scouts, Ambushers, the confirm gate, free-for-all

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/DeploymentTests.cs`, `tests/Tabletop.GameServer.Tests/RoundBoundaryTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5 (no new production code: this task proves the base, the OPR hooks and the roll-off compose as the spec says, and fixes whatever does not).
- Produces: nothing new.

Spec: scheduler → Deployment (Scouts deploy after every other unit, alternating, "starting with the player that activates next" — the roll-off winner; a second deploy Ask over the set-aside Scouts), The round boundary (hooks in order; Ambushers from round two, alternating, until every team passes; deploying an Ambusher writes nothing to the record), Windows/Enforcement unaffected; action sequences → One roll primitive (the confirm gate when waits is on). **The roll-off is gated like any other roll** (ruling 7): `GfRolls.RollOffSequence` is two steps, `roll-off` then `order`, and the boundary between them is one unsealed `Confirm` Ask to every team, opened whatever the wait setting says because the frame has no actor and no parent (its Task 8, Interfaces needed 4). So **every** roll-off in this file waits at a gate — which is why `SkipDeployment` releases one and the tests that drive `Run()` by hand call `ConfirmRollOff`. The gated test below is the one that pins the shape: the dice are editable while the frame waits, and the `order` step ranks whatever stands when a team confirms, so nothing ever rewrites a logged `step_completed` and the reader keys on `sequence_finished` alone. The `end_round` Ask (ruling 4) sits in front of all of it: it precedes `round_started`, so the fatigue and token changes, the first-team roll-off and the Ambusher loop all happen after the new round has begun, and these tests pin that — round one's fatigue is still set while the boundary Ask is open. Rulebook page 50: a free-for-all rolls off once, for deployment order, and orders every round by who finished activating first; page 13 `Ambush`; page 14 `Scout`.

- [ ] **Step 1: Write `DeploymentTests.cs`**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class DeploymentTests
{
    static Subject U(Guid id) => new(SubjectKind.Unit, id);

    [Fact]
    public void Scouts_deploy_after_every_other_unit_starting_with_the_roll_off_winner()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 2, 1, unitRules: [(1, Scout, null)])]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);                                    // B wins
        h.Run(); h.ConfirmRollOff();                                             // the roll-off's gate, released with the dice as rolled
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(U(a[1]), SetAside, true)); h.Dispatch(new SetCondition(U(b[0]), SetAside, true));   // set aside from the menu
        Assert.Equal([b[0], b[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);                 // B's Ask opened before the set-aside and stands as logged; B picks b[1]
        h.Answer(1, new SelectedPieces([b[1]])); h.Deploy(b[1]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        Assert.Equal([a[0]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);                       // A's Ask opened after: the set-aside Scout is not offered
        h.Answer(0, new SelectedPieces([a[0]])); var seq = h.P.LastSeq; h.Deploy(a[0]);
        Assert.Equal(["models_moved", "turn_ended", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((Scouts, h.Teams[1], AskQuestions.DeployScouts), (h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // the winner, not the next in rotation
        Assert.Equal([b[0]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Answer(1, new SelectedPieces([b[0]])); h.Deploy(b[0]);
        Assert.Equal((h.Teams[0], AskQuestions.DeployScouts), (h.P.Scheduler.ActiveTeamId, h.Open.Question));
        h.Answer(0, new SelectedPieces([a[1]])); seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_ended", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((1, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.All(h.P.Units.Values, u => Assert.Empty(u.Actions));
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void The_roll_off_waits_at_a_confirm_gate_to_every_team_and_a_die_edited_there_decides_the_order()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);
        h.Run();
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], h.Kinds(0));                    // rolled, then held: no order and no round_started yet
        Assert.Equal((AskQuestions.Confirm, 0, (Guid?)null), (h.Open.Question, h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.Equal(h.Teams, h.Open.Teams); Assert.False(h.Open.Sealed);                                  // every team, unsealed: no acting unit has a better claim to release it
        Assert.NotNull(h.Open.FrameId);                                                                    // the interpreter's Ask, so StaleAsks never touches it
        Assert.Single(h.P.Stack);
        var rolled = h.Log.Select(e => e.Payload).OfType<StepCompleted>().Single();
        Assert.Equal(GfNames.RollOffRollStep, rolled.Step); Assert.Null(rolled.Values);                    // the rolling step carries dice and no ranking
        var seq = h.P.LastSeq;
        h.Dispatch(new SetDice(rolled.FrameId, rolled.Rolls[0].Id, [new DieValue(0, 6)]), team: 0);        // A's 2 edited to a 6 while the frame is live and the roll is in a completed step
        Assert.Equal(["dice_edited"], h.Kinds(seq));
        Assert.Equal(0, h.P.Scheduler.Round);                                                              // the edit changed no order, because no order has been derived
        seq = h.P.LastSeq;
        h.ConfirmRollOff(team: 1);                                                                         // B releases A's edit: any team may
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var order = h.Log.Select(e => e.Payload).OfType<StepCompleted>().Last();
        Assert.Equal((GfNames.RollOffOrderStep, 1, 1, 2), (order.Step, order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));
        Assert.Equal([h.Teams[0], h.Teams[1]], h.P.Scheduler.Order);                                       // the edited die decided it: A first, where the raw dice had B
        Assert.Empty(h.P.Stack);
        h.Rehydrate(); Assert.Empty(h.Run());                                                              // and the same order comes back out of the log, with no die re-rolled
    }

    [Fact]
    public void A_set_aside_unit_that_is_neither_scout_nor_ambusher_is_simply_never_offered()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        var a = h.Units(0);
        h.Dispatch(new SetCondition(U(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(1, h.P.Scheduler.Round);                                                             // deployment ended without a[1]
        Assert.All(h.P.Units[a[1]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        h.Dispatch(new SetCondition(U(a[1]), SetAside, false)); h.Dispatch(TestGames.Move(h.State, a[1]), team: 0);   // the menu puts it on the table later
        Assert.DoesNotContain(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);     // A's open Ask predates the move and stands as logged
        h.Answer(0, new Passed()); h.Answer(1, new Passed()); h.EndRound();                               // both passed, then a player ended the round
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // A passed first, so A goes first
        Assert.Contains(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);           // the fresh set reads the table as it is
    }
}
```

An Ask's legal set is computed when it opens and stands as logged; a menu edit after that is read by the *next* Ask (scheduler → Nothing is an invariant: the scheduler reads state on every step, and a step is an Ask).

- [ ] **Step 2: Write `RoundBoundaryTests.cs`**

```csharp
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class RoundBoundaryTests
{
    static Subject U(Guid id) => new(SubjectKind.Unit, id);
    static string Activate(Guid unit) => ActivationOption.Id(unit, "Hold");

    [Fact]
    public void Ambushers_are_asked_at_the_start_of_round_two_and_may_activate_that_round()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(U(a[1]), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);                                              // the round waits to be ended before anything else happens
        seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "ask_opened"], h.Kinds(seq));          // the boundary Ask sits before round_started, so every hook still lands in the new round
        Assert.Equal((2, (Guid?)null, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // a boundary Ask, not a turn
        Assert.Equal([h.Teams[0]], h.Open.Teams);                                                          // A finished first and is asked first
        h.Answer(0, new SelectedPieces([a[1]]));
        Assert.Empty(h.P.OpenAsks); Assert.Null(h.P.Scheduler.ActiveTeamId);                                // waiting for the models
        seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_started", "ask_opened"], h.Kinds(seq));                       // A has no more Ambushers, B never had one: the hook is done
        Assert.Contains(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);           // deploying wrote nothing to the record
        Assert.Empty(h.P.Units[a[1]].Actions);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Round_start_changes_ride_on_round_started_in_order_record_clears_then_fatigue_then_tokens()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1, modelRules: [(1, Caster, 2)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1); var caster = h.P.Units[a[0]].Definition.Models[0].Id;
        h.Dispatch(new SetCondition(U(b[0]), Fatigued, true));
        h.Answer(0, new Chosen(Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(Activate(b[0])));
        Assert.Contains(Fatigued, h.P.Units[b[0]].Conditions);                                    // the boundary Ask is open and no hook has run: fatigue still belongs to round one
        Assert.Equal(2, h.P.Models[caster].SpellTokens);                                          // round one's own boundary granted Caster(2); round two has granted nothing yet
        h.EndRound();
        var round = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal([typeof(ActionRecordCleared), typeof(ActionRecordCleared), typeof(ConditionSet), typeof(SpellTokensSet)], round.Changes.Select(c => c.GetType()));
        Assert.Equal(4, h.P.Models[caster].SpellTokens); Assert.Empty(h.P.Units[b[0]].Conditions);             // two from round one, two more from round two (capped at 6)
        Assert.Equal(0, ((RoundStarted)h.Log.First(e => e.Payload is RoundStarted).Payload).Changes.Count);   // round 0 carried none
    }

    [Fact]
    public void Free_for_all_deploys_by_roll_off_then_orders_rounds_by_who_finished_first()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 2, 1), TestArmies.Build("C", 1, 1)]);
        foreach (var d in new[] { 1, 2, 3 }) h.Dice.Enqueue(d);                               // deployment: C, B, A
        h.SkipDeployment();
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // p.50: one roll-off, and it settles deployment order
        Assert.Equal([h.Teams[2], h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);              // round one follows the deployment order
        for (var i = 0; i < 4; i++)                                                                        // four activations end round one
        {
            var team = h.Teams.ToList().IndexOf(h.Open.Teams[0]);
            h.Answer(team, new Chosen(Activate(((Options)h.Open.Legal).Choices[0].Pieces[0])));
        }
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));
        h.EndRound(team: 1);
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // no roll-off at a round boundary, so no confirm gate either
        Assert.Equal([h.Teams[2], h.Teams[0], h.Teams[1]], h.P.Scheduler.Order);              // C activated first and once, A once, B twice and last
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(Activate(h.Units(2)[0]))); h.Answer(0, new Chosen(Activate(h.Units(0)[0])));
        h.Answer(1, new Chosen(Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                             // C and A are finished: B goes twice running
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "Deployment|RoundBoundary"`; expect 6 passed. Fix any base-class defect these expose in `SchedulerBase.cs`, never by weakening an assertion the spec supports. Commit: `Prove deployment, Scouts, Ambushers and the free-for-all boundary end to end`.**

### Task 7: The player-turn shape

**Files:**
- Create: `server/game/Scheduler/PlayerTurnScheduler.cs`
- Test: `tests/Tabletop.GameServer.Tests/PlayerTurnSchedulerTests.cs`

**Interfaces:**
- Consumes: `SchedulerBase` and its hooks (Task 5), `TurnCycle.AskFor`, `AskQuestions.ActOrEndPhase`, `IGameRules.PhasesInTurn`.
- Produces: `sealed class PlayerTurnScheduler(PropertySet) : SchedulerBase`.

Spec: scheduler → The two shapes (player-turn: a team holds the turn for every activation until its legal set is empty or it passes; the turn is a list of phases from `GameRules`, each an Ask loop of *act or end phase* with the legal set read per phase; OPR has one phase and activates every unit in a row; the team's record clears at its `turn_ended`; the round ends when every team has had a turn); game flow → Two schedulers (each owns when the record clears). The first-team policy is the same hook, so OPR under this shape still rolls off before deployment and gives the winner round one's first turn. Deployment (round 0) alternates one unit a turn under this shape too, because scheduler → Deployment describes the phase once rather than per shape, and page 6 alternates units regardless. The `end_round` Ask (ruling 4) is the base's, so it needs nothing here: "the round ends when every team has had a turn" means the boundary Ask opens then, and `IsFinished`, `ShapeTurnEndChanges` and the phase loop have no part in it — this task's tests only answer it.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class PlayerTurnSchedulerTests
{
    static SchedulerHarness Game(int unitsA = 2, int unitsB = 1) =>
        new([TestArmies.Build("A", unitsA, 2), TestArmies.Build("B", unitsB, 2)], scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);
    static string Activate(Guid unit) => ActivationOption.Id(unit, "Hold");

    [Fact]
    public void Deployment_still_alternates_one_unit_a_turn_and_the_winner_opens_round_one()
    {
        var h = Game(); h.Dice.Enqueue(1); h.Dice.Enqueue(6); h.Run(); h.ConfirmRollOff();
        Assert.Equal(AskQuestions.Deploy, h.Open.Question); Assert.Equal(h.Teams[1], h.Open.Teams.Single());
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                  // alternation, not "B deploys everything"
        h.SkipDeployment();
        Assert.Equal((1, h.Teams[1], AskQuestions.ActOrEndPhase, true), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question, h.Open.AllowPass));
    }

    [Fact]
    public void A_team_activates_every_unit_in_a_row_then_its_record_clears_at_turn_ended_and_the_other_team_goes()
    {
        var h = Game(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(Activate(a[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "ask_opened"], h.Kinds(seq));   // same turn, next Ask
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal([a[1]], Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());
        seq = h.P.LastSeq; h.Answer(0, new Chosen(Activate(a[1])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.False(ended.Passed);
        Assert.Equal([new ActionRecordCleared(a[0]), new ActionRecordCleared(a[1])], ended.Changes.OrderBy(c => a.ToList().IndexOf(((ActionRecordCleared)c).UnitId)).ToList());
        Assert.All(a, u => Assert.Empty(h.P.Units[u].Actions));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        seq = h.P.LastSeq; h.Answer(1, new Chosen(Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal((AskQuestions.EndRound, h.Teams), (h.Open.Question, h.Open.Teams));                   // both shapes stop at the boundary and both ask every team
        seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var round2 = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal((2, 0), (round2.Round, round2.Changes.Count));                                        // nothing to clear at the round: the turns cleared
        Assert.Equal([h.Teams[0], h.Teams[1]], round2.Order);                                              // A's turn ended first
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Passing_ends_the_turn_with_units_left_and_the_team_is_not_asked_again_this_round()
    {
        var h = Game(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0);
        h.Answer(0, new Chosen(Activate(a[0])));
        var seq = h.P.LastSeq; h.Answer(0, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.True(ended.Passed); Assert.Single(ended.Changes);                                            // a[0]'s record cleared; a[1] never activated
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(a[0]));
        h.Answer(1, new Chosen(Activate(h.Units(1)[0])));
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));                 // A was not re-asked in round one; the round waits for a player
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
    }

    [Fact]
    public void A_team_with_nothing_to_do_is_skipped_and_a_round_with_no_turns_waits_to_be_ended()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), null], scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);
        h.Dice.Enqueue(1); h.Dice.Enqueue(6); h.SkipDeployment();
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // B (the winner) has nothing: skipped
        h.Answer(0, new Chosen(Activate(h.Units(0)[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);
        h.EndRound(team: 1);                                                                              // the team that never had a turn may still end the round
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        var a0 = h.Units(0)[0];
        h.Dispatch(new MoveModels(h.P.Units[a0].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);
        h.Answer(0, new Passed());
        h.EndRound();
        Assert.Equal((3, (Guid?)null, AskQuestions.EndRound), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // nobody can act: it waits here, it does not count up
        Assert.Empty(h.Run());
    }

    [Fact]
    public void Three_teams_of_one_each_take_one_turn_a_round_in_the_deployment_order()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)],
            scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);
        foreach (var d in new[] { 3, 2, 1 }) h.Dice.Enqueue(d);                                // deployment A, B, C; p.50 gives round one that same order
        h.SkipDeployment();
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[2]], h.P.Scheduler.Order);
        var a = h.Units(0);
        h.Answer(0, new Chosen(Activate(a[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // A holds the turn for its second unit
        h.Answer(0, new Chosen(Activate(a[1])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(Activate(h.Units(2)[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question); h.EndRound(team: 2);
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[2]], h.P.Scheduler.Order);                          // finished-first repeats the order here: under player-turn a team finishes when its units run out
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // p.50: the only roll-off was for deployment
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PlayerTurnScheduler`; expect compile errors. Write `PlayerTurnScheduler.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// A team holds the turn for every activation until its legal set is empty or it passes; the turn is GameRules' list of phases, each an
/// act-or-end-phase Ask loop; the team's record clears at its turn_ended; the round ends when every team has had a turn
/// (scheduler → The two shapes). Before round one the base alternates one unit a turn as under the other shape.
/// </summary>
public sealed class PlayerTurnScheduler(PropertySet properties) : SchedulerBase(properties)
{
    protected override bool IsFinished(GameState state, SchedulerLog log, Guid teamId) =>
        state.Projection.Scheduler.Round == 0
            ? base.IsFinished(state, log, teamId)
            : log.LastTurnEndedByTeam(log.SinceRound).ContainsKey(teamId) || state.Rules.LegalTurn(state, Properties, teamId) is null;

    protected override TurnAsk ShapeAsk(GameState state, TurnAsk ask) => ask with { Question = AskQuestions.ActOrEndPhase, AllowPass = true };

    protected override IReadOnlyList<EventPayload> BeforeFirstAsk(GameState state, SchedulerLog log, Guid teamId)
    {
        var phases = state.Rules.PhasesInTurn;
        return phases.Count > 0 && !log.SinceTurn.Any(e => e.Payload is PhaseStarted) ? [new PhaseStarted(phases[0])] : [];
    }

    protected override IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn)
    {
        var ask = state.Rules.LegalTurn(state, Properties, turn.TeamId);
        return ask is null ? NextPhaseOrEnd(state, log, turn, passed: false) : [new AskOpened(TurnCycle.AskFor(turn.TeamId, ShapeAsk(state, ask)))];
    }

    protected override IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn) => NextPhaseOrEnd(state, log, turn, passed: true);
    protected override IReadOnlyList<EventPayload> OnEmpty(GameState state, SchedulerLog log, TurnContext turn) => NextPhaseOrEnd(state, log, turn, passed: false);

    /// <summary>The next of GameRules' phases inside the turn, or turn_ended after the last (OPR has none: the turn ends).</summary>
    private IReadOnlyList<EventPayload> NextPhaseOrEnd(GameState state, SchedulerLog log, TurnContext turn, bool passed)
    {
        var phases = state.Rules.PhasesInTurn;
        var current = log.SinceTurn.Select(e => e.Payload).OfType<PhaseStarted>().LastOrDefault()?.Phase;
        var index = current is null ? -1 : phases.ToList().IndexOf(current);
        return index + 1 < phases.Count ? [new PhaseStarted(phases[index + 1])] : EndTurn(state, turn, passed);
    }

    /// <summary>The team's records clear when its turn ends (game flow → Two schedulers).</summary>
    protected override IReadOnlyList<StateChange> ShapeTurnEndChanges(GameState state, TurnContext turn) =>
        state.Projection.Scheduler.Round == 0 ? []
            : TeamUnits.Of(state, turn.TeamId).Where(u => u.Actions.Count > 0).Select(u => (StateChange)new ActionRecordCleared(u.Id)).ToList();
}
```

When a named in-turn phase starts, `PhaseStarted` moves the rotation anchor; that is harmless inside a turn (the rotation is only consulted between turns) and the base's stale-Ask cutoff uses `turn_started`, not the phase. `Projection.Scheduler.Phase` then reads the in-turn phase name until the next `round_started`'s phase; OPR has no in-turn phases so its `Phase` stays `rounds` (scheduler → State).

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PlayerTurnScheduler`; expect 5 passed; run the whole project and expect everything green. Commit: `Add the player-turn shape`.**

### Task 8: Reaction ordering

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/ReactionOrderingTests.cs`

**Interfaces:**
- Consumes: `FakeRules` and `TestGames` (Tasks 5 and 2); `Projection.Scheduler.Order/Passed`; the interpreter plan's `Windows.ReactionOrder(GameState, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)` (its Task 9) — **the one ordering policy**: `IGameRules.OrderReactions` when non-null, else turn order from the team after the actor, the actor's own team last, passed teams dropped; `Sequence`, `LambdaStep`, `Complete` (interpreter plan Task 8).
- Produces: no production code — the ordering lives in `Windows.ReactionOrder`, which `Windows.Run` calls, and this plan's `IGameRules.OrderReactions` (Task 2) is the hook it consults; these tests pin the policy from the scheduler's side. No test infrastructure either: `FakeRules` is Task 5's, and this task only sets its `OrderReactionsHook`.

Spec: scheduler → Windows ("Eligibility is the sequence's business ... Ordering is the scheduler's. When more than one team answers react, their reactions resolve in the current turn order, starting with the team after the actor. Teams that passed drop out. `GameRules` may replace the ordering with a hook"); What the interface must not assume 5 (a window may belong to a non-active team: the actor is whoever's step opened the window, not the active team) and 6 (ordering is a policy with a default). The actor's own team is not excluded — OPR's casting window is offered to it (interpreter plan Task 11, `GfHooks.ReactionsAt`) — it simply comes last. OPR's one reaction outside casting, the optional **Overwatch** action (page 33), is not built by this plan or any other: it is one of the optional extra actions the handoff defers, and the scheduler's whole share of it is constraint 5 — a window belonging to a non-active team — which Task 9 tests through a fake ruleset.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class ReactionOrderingTests
{
    static TestGames.Built Four() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1), TestArmies.Build("D", 1, 1)]);

    [Fact]
    public void Reactions_resolve_in_turn_order_starting_after_the_actor_and_passed_teams_drop_out()
    {
        var g = Four(); var t = g.Teams;
        TestGames.Apply(g, new RoundStarted(1, [t[2], t[0], t[3], t[1]], []));
        Assert.Equal([t[3], t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2]]));                 // only those who reacted
        Assert.Equal([t[3], t[1], t[2], t[0]], Windows.ReactionOrder(g.State, t[0], t));                // the actor's own team may react (OPR's casting window): last
        TestGames.Apply(g, new TurnStarted(t[3]), new TurnEnded(t[3], true, []));
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));           // t3 passed
        Assert.Equal([t[0], t[1]], Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));           // after t2 comes t0, then t3 (passed, dropped), then t1
    }

    [Fact]
    public void A_non_active_actor_is_still_the_anchor_and_no_order_yet_means_team_order()
    {
        var g = Four(); var t = g.Teams;
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[2], t[1]]));                 // round 0, no order yet: Info.Teams order after the actor
        TestGames.Apply(g, new RoundStarted(1, [t[0], t[1], t[2], t[3]], []), new TurnStarted(t[0]));
        Assert.Equal([t[3], t[0], t[1]], Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));     // t2 reacted inside t0's turn; the order still starts after t2
    }

    [Fact]
    public void Game_rules_may_replace_the_ordering()
    {
        var rules = new FakeRules { OrderReactionsHook = (_, actor, reacting) => reacting.Reverse().ToList() };
        var g = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)], rules: rules);
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []));
        Assert.Equal([g.Teams[2], g.Teams[1]], Windows.ReactionOrder(g.State, g.Teams[0], [g.Teams[1], g.Teams[2]]));
    }
}
```

- [ ] **Step 2: There is no ordering class to write.** `Windows.ReactionOrder` (interpreter plan, `Interpreter/Windows.cs`) is the one policy, and it already consults `state.Rules.OrderReactions`. If the interpreter plan's Task 9 has not landed, land it first; nothing here can substitute for it.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter ReactionOrdering`; expect 3 passed. Commit: `Pin reaction ordering from the scheduler's side`.**

### Task 9: The six interface constraints, as tests

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/SchedulerConstraintTests.cs` (the interpreter plan's Task 18 owns `InterfaceConstraintTests.cs` in this project; the two files sit side by side)

**Interfaces:**
- Consumes: `FakeRules` and `SchedulerHarness` (Task 5), both shapes.
- Produces: nothing new. If a constraint cannot be met without a production change, the change goes in the file the constraint names and is small; none is expected.

Spec: scheduler → What the interface must not assume, 1–6. Each test drives the generic scheduler with a `FakeRules` whose hook answers the 40k way, without any 40k rule being built. Constraint 2's *alternation inside a phase* is not built (the spec says so); the test covers the part that is — a phase list inside a turn, each phase its own Ask loop. Constraint 5's *whole sequence inside another team's turn* is the interpreter's; the scheduler's share is not to mistake a framed Ask addressed to the non-active team for its own, tested here. The `end_round` Ask is generic engine behaviour, not a policy `GameRules` can replace (ruling 4), so the constraint tests that cross a round boundary answer it like any player would; no constraint asks for a ruleset to advance rounds by itself, and if one ever did it would be a hook, not a branch.

- [ ] **Step 1: Write the tests**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SchedulerConstraintTests
{
    static string Activate(Guid unit) => ActivationOption.Id(unit, "Hold");
    static SchedulerHarness Game(FakeRules rules, bool playerTurn = false) => new(
        [TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1)],
        scheduler: playerTurn ? p => new PlayerTurnScheduler(p) : null, rules: rules,
        shape: playerTurn ? SchedulerShape.PlayerTurn : SchedulerShape.AlternatingActivation);

    [Fact]
    public void One_the_first_team_each_round_is_a_policy_the_same_team_every_round_needs_no_roll_off()
    {
        var h = Game(new FakeRules());                                                                   // no phases before round one, same order every round
        h.Run();
        foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]);
        h.Run();
        Assert.Equal((1, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.DoesNotContain(h.Log, e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq);
        Assert.DoesNotContain(h.Log, e => e.Payload is PhaseStarted { Phase: "deployment" });
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(Activate(a[0]))); h.Answer(1, new Chosen(Activate(b[0]))); h.Answer(0, new Chosen(Activate(a[1])));
        h.EndRound();                                                                                     // a player ends the round whatever the first-team policy is
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // OPR would have put B first
        Assert.Equal(h.Teams, h.P.Scheduler.Order);
    }

    [Fact]
    public void Two_a_turn_may_be_a_list_of_phases_each_with_its_own_ask_loop()
    {
        var h = Game(new FakeRules { PhasesInTurn = ["movement", "shooting"] }, playerTurn: true);
        h.Run(); foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]);
        var seq = h.P.LastSeq; h.Run();
        Assert.Equal(["turn_started", "phase_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal("movement", h.P.Scheduler.Phase);
        seq = h.P.LastSeq; h.Answer(0, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "phase_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal(("shooting", h.Teams[0]), (h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));
        seq = h.P.LastSeq; h.Answer(0, new Chosen(Activate(h.Units(0)[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // the loop continues inside the phase
        h.Answer(0, new Passed());
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                             // the last phase ended: turn_ended, next team
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Three_reinforcements_arrive_on_the_rulesets_schedule_through_the_boundary_hook()
    {
        var reserves = "reserves";
        var rules = new FakeRules
        {
            RoundBoundaryHook = (state, _, round, order) =>
            {
                if (round != 2) return new HookDone();
                var log = SchedulerLog.Of(state);
                var asked = log.SinceRound.Select(e => e.Payload).OfType<AskOpened>().FirstOrDefault(o => o.Ask.Question == reserves);
                if (asked is null) return new HookEmit([new AskOpened(new Ask(Guid.CreateVersion7(), reserves, "Bring on reserves?", [order[0]], new Confirmation(), true, false, null, null))]);
                return state.Projection.OpenAsks.ContainsKey(asked.Ask.Id) ? new HookWaiting() : new HookDone();
            },
        };
        var h = Game(rules); h.Run(); foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(Activate(a[0]))); h.Answer(1, new Chosen(Activate(b[0]))); h.Answer(0, new Chosen(Activate(a[1])));
        h.EndRound();                                                                                     // the boundary Ask first, then the ruleset's own boundary hook
        Assert.Equal((2, (Guid?)null, reserves), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // before the first turn
        h.Dispatch(new AnswerAsk(h.Open.Id, new Confirmed()), team: 0);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
    }

    [Fact]
    public void Four_the_scheduler_never_reads_the_action_record_itself()
    {
        var rules = new FakeRules
        {
            LegalTurnHook = (state, _, team) =>
            {
                var units = TeamUnits.Of(state, team).Where(u => TeamUnits.AnyOnTable(state, u)).ToList();   // the record is ignored on purpose
                return units.Count == 0 ? null : new TurnAsk(AskQuestions.Activate, "Activate", new Options(units.Select(u => ActivationOption.For(u, "Hold")).ToList()), true);
            },
        };
        var h = Game(rules); h.Run(); foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]); h.Run();
        var a = h.Units(0);
        h.Answer(0, new Chosen(Activate(a[0]))); h.Answer(1, new Chosen(Activate(h.Units(1)[0])));
        Assert.Contains(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[0]);           // offered again despite its record
        Assert.Contains("Hold", h.P.Units[a[0]].Actions);
        h.Answer(0, new Passed()); h.Answer(1, new Passed());
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));                 // only passes finish this round, and only a player ends it
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
    }

    [Fact]
    public void Five_a_framed_ask_to_the_non_active_team_is_not_the_schedulers_and_does_not_end_the_turn()
    {
        // The action's sequence is one step that opens a React window to the other team and completes once it has answered: a 40k-shaped overwatch.
        var rules = new FakeRules
        {
            SequenceHook = name => new Sequence(name, [new LambdaStep("overwatch", ctx => ctx.History.LastClosed() is not null ? new Complete([])
                : new OpenAsks([ctx.Ask(AskQuestions.React, "Overwatch?", ctx.State.Info.Teams.Select(t => t.Id).Single(t => t != ctx.ActorTeam), new Options([new AskOption("fire", "Fire", [])]), allowPass: true, windowKind: "overwatch")]))]),
        };
        var h = Game(rules);
        h.Run(); foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]); h.Run();
        var a = h.Units(0);
        h.Answer(0, new Chosen(Activate(a[0])));
        var window = h.Open;
        Assert.Equal((AskQuestions.React, h.Teams[1], h.P.Stack.Single().Id), (window.Question, window.Teams.Single(), window.FrameId!.Value));
        Assert.False(SchedulerLog.IsSchedulerAsk(window));
        Assert.Empty(h.Run()); Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                     // a framed Ask blocks the engine; the turn stands
        var seq = h.P.LastSeq; h.Dispatch(new AnswerAsk(window.Id, new Chosen("fire")), team: 1);
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));   // the window's answer is the interpreter's business; only then does the turn end
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
    }

    [Fact]
    public void Six_ordering_simultaneous_reactions_is_a_policy_with_a_default()
    {
        var g = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []));
        Assert.Equal([g.Teams[1], g.Teams[2]], Windows.ReactionOrder(g.State, g.Teams[0], [g.Teams[2], g.Teams[1]]));   // the default
        var chooser = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)],
            rules: new FakeRules { OrderReactionsHook = (_, _, reacting) => reacting });                                                    // "the active player chooses": as sent
        TestGames.Apply(chooser, new RoundStarted(1, chooser.Teams, []));
        Assert.Equal([chooser.Teams[2], chooser.Teams[1]], Windows.ReactionOrder(chooser.State, chooser.Teams[0], [chooser.Teams[2], chooser.Teams[1]]));
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter SchedulerConstraint`; expect 6 passed. Commit: `Test the six 40k-derived constraints on the scheduler interface`.**

### Task 10: The scheduler's factory, its registration, and the socket end to end

**Files:**
- Create: `server/game/Scheduler/SchedulerFactory.cs`
- Modify: `server/game/Program.cs` (replace the `NoSchedulerFactory` registration the interpreter plan's Task 10 left), `CLAUDE.md` (one Layout line)
- Test: `tests/Tabletop.GameServer.Tests/SchedulerFactoryTests.cs`, `tests/Tabletop.GameServer.Tests/SchedulerEndToEndTests.cs`

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

- [ ] **Step 3: Register.** In `Program.cs` replace `builder.Services.AddSingleton<ISchedulerFactory, NoSchedulerFactory>();` (interpreter plan Task 10, Step 5) with `builder.Services.AddSingleton<ISchedulerFactory, SchedulerFactory>();`. The `IDiceSource`, `IPropertiesStore` and `IRulesEngineFactory` registrations are the interpreter plan's and do not change. `NoScheduler`/`NoSchedulerFactory` stay in the tree for that plan's tests.

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

The second team in the first test has an army but the second-process test gives it none; both are legal games and the second is skipped through deployment as a team with an empty legal set.

- [ ] **Step 5: `CLAUDE.md`** — under Layout, append one child bullet under the `server/game/` bullet, after the interpreter plan's children, editing nothing else: ``  - `Scheduler/` — the generic turn machinery behind `Engine.IScheduler` (two shapes, the turn as an Ask, the roll-off read back from the ruleset's sequence); its Grimdark Future hooks are the `GfRules.Scheduling.cs`/`GfRules.Deployment.cs` partials in `Rules/GrimdarkFuture/`, beside the interpreter's sequences.`` The ownership rule (contracts plan Task 16): a plan edits only the lines it owns.

- [ ] **Step 6: Run `dotnet test`; expect every project green (the two end-to-end tests need the Docker Postgres up: `docker compose up -d`). Commit: `Register the scheduler behind the rules engine's seam`.**

---

## Interfaces → Produces, per namespace

Later plans copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`.

### `Tabletop.GameServer.Rules`

| Type | Members |
| --- | --- |
| `interface IGameRules` (this plan's block; the merged interface is in the skeleton plan → Interfaces → Produces) | `IRL<string> PhasesBeforeRounds`; `IRL<string> PhasesInTurn`; `FirstTeamDecision FirstTeam(GameState, int round, IRL<Guid> teams)`; `TurnAsk? LegalTurn(GameState, PropertySet, Guid teamId)`; `Activation? ReadAnswer(GameState, Guid teamId, AskAnswer)`; `bool ActivationComplete(GameState, Activation)`; `HookResult RoundBoundaryStep(GameState, PropertySet, int round, IRL<Guid> order)`; `IRL<Guid>? OrderReactions(GameState, Guid? actorTeamId, IRL<Guid> reactingTeams)`. The interpreter plan's `OnRoundStarted(GameState, PropertySet, int)`, `EndOfTurnSequences(GameState, PropertySet, TurnContext)`, `OnTurnEnded(GameState, PropertySet, TurnContext)` are what this plan calls at the round boundary, the end of an activation and turn end |
| `sealed partial class GrimdarkFutureRules` (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`; this plan's partials `GfRules.Scheduling.cs`, `GfRules.Deployment.cs`) | every member above; `internal static IRL<string> ActionsFor(GameState, PropertySet, UnitInPlay, IRL<string> rows)`, `IRL<Guid> Ambushers(GameState, PropertySet, Guid teamId)`. `RulesId`, `Instance`, `Id` are the skeleton's (`GfRules.cs`) |
| `static class GfNames` (the interpreter plan's; this plan only reads it) | `Deployment`, `Scouts`, `Rounds` (= `SchedulerBase.RoundsPhase`), `Deploy`, `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `SetAside` — all declared there, none added here |

### `Tabletop.GameServer.Scheduler`

`Engine.IScheduler { IRL<EventPayload> Advance(GameState, ISequenceStarter) }` and `Engine.ISchedulerFactory { IScheduler Create(IGameRules, GameInfo, PropertySet) }` are the interpreter plan's; this plan implements them.

| Type | Members |
| --- | --- |
| `sealed record TurnAsk(string Question, string Prompt, LegalSet Legal, bool AllowPass)` | declared by the interpreter plan's Task 8 (the seam), as are the next four rows and `ActivationOption`; this plan reads them |
| `sealed record Activation(Guid UnitId, string Action, bool RunsSequence, IRL<StateChange> Changes)` | |
| `sealed record TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq)` | |
| `abstract record HookResult` | `HookDone`, `HookWaiting`, `HookEmit(IRL<EventPayload>)`, `HookStartSequences(IRL<SequenceStart>)` (`SequenceStart` is the interpreter plan's record beside `IGameRules`) |
| `abstract record FirstTeamDecision` | `OrderedTeams(IRL<Guid> Order)`, `RollOffNeeded(string Sequence)` |
| `static class ActivationOption` | `const string Idle = "Idle"`; `string Id(Guid unitId, string action)`; `bool TryParse(string, out Guid, out string)`; `AskOption For(UnitInPlay, string action)` |
| `sealed class SchedulerLog` | `static Of(GameState)`; `Effective`, `LastSeq`, `Last(Func<EventPayload,bool>)`, `After(long)`, `RoundStartedAt`, `PhaseStartedAt`, `TurnStartedAt`, `RotationStartAt`, `CycleStartAt`, `SinceRound`, `SinceRotation`, `SinceTurn`, `SinceCycle`; `static bool IsSchedulerAsk(Ask)`; `static bool IsTurnAsk(Ask)`; `LastSchedulerAsk(slice)`; `LastTurnEndedByTeam(slice)`; `LastTurnEndedTeam(slice)` |
| `static class TeamUnits` | `Guid? TeamOf(GameState, Guid participantId)`; `IRL<UnitInPlay> Of(GameState, Guid teamId)`; `IRL<ModelInPlay> ModelsOf(GameState, UnitInPlay)`; `bool AllShelved(GameState, UnitInPlay)`; `bool AnyOnTable(GameState, UnitInPlay)`; `int IndexOf(IRL<Guid>, Guid)` |
| `static class RollOff` | `Progress` = `Emit(events)` / `Waiting` / `Resolved(order)`; `Progress Advance(GameState, SchedulerLog, string sequence, ISequenceStarter)`; `IRL<Guid> Order(GameState, StepCompleted?)` — reads `Values["place_k"]` from the ruleset's roll-off sequence (interpreter plan `GfRolls.RollOffSequence`), off the completed step carrying `place_1` and only once `sequence_finished` stands, because the frame waits at a confirm gate with its dice editable |
| `abstract record CycleState` | `NoAsk`, `AskOpen(Ask)`, `PassAnswered(long)`, `NeedsStart(Activation, long)`, `Running(Activation, long, Guid FrameId)`, `WaitingCompletion(Activation, long)`, `ActivationEnded(Activation, long, long)` |
| `abstract record CycleStep` | `CycleEmit(IRL<EventPayload>)`, `CycleWaiting`, `CycleEmpty`, `CyclePassed(TurnContext)`, `CycleComplete(TurnContext)` |
| `sealed class TurnCycle(PropertySet)` | `static Ask AskFor(Guid teamId, TurnAsk)`; `CycleState Read(GameState, Guid teamId, SchedulerLog)`; `CycleStep Step(GameState, Guid teamId, SchedulerLog, ISequenceStarter, Func<TurnAsk,TurnAsk>? shape = null)` |
| `abstract class SchedulerBase(PropertySet) : Engine.IScheduler` | `const string RoundsPhase = "rounds"`; `Advance(GameState, ISequenceStarter)`; `static Ask EndRoundAsk(IRL<Guid> teams)`; protected `Properties`, `Cycle`, `NextTeam`, `EndTurn`, virtual `IsFinished`, `ShapeRoundStartChanges`, `ShapeTurnEndChanges`, `BeforeFirstAsk`, `ShapeAsk`, `OnEmpty`, abstract `OnPassed`, `OnComplete` |
| `sealed class AlternatingActivationScheduler(PropertySet)`, `sealed class PlayerTurnScheduler(PropertySet)` | the two shapes |
| `sealed class SchedulerFactory : Engine.ISchedulerFactory` | `IScheduler Create(IGameRules, GameInfo, PropertySet)`; registered in `Program.cs` in place of `NoSchedulerFactory` |

Reaction ordering has no type here: `Interpreter.Windows.ReactionOrder(GameState, Guid? actorTeam, IRL<Guid> reactingTeams)` is the one policy and consults `IGameRules.OrderReactions` (Task 2), which Task 8's tests pin.

### Test infrastructure (`tests/Tabletop.GameServer.Tests/Infrastructure`)

`TestArmies.Build`, `TestGames` (`Build`, `Built`, `Apply`, `Relocations`, `Place`, `Move`), `ScriptedStarter`, `QueueDice`, `SchedulerHarness` (a `Sim` over the real `RulesEngine`; `EndRound(int team = 0)` answers the boundary Ask), `FakeRules` (the merged `IGameRules`: Grimdark Future's bodies, settable hooks, empty action sequences; `FakeRules.Gf()`). Beside the interpreter plan's `Armies`/`Sim`/`StubRules`/`TestProperties`/`LambdaStep`: `Armies`/`Sim` build a two-team playing game around a sequence, `TestArmies`/`TestGames`/`SchedulerHarness` build N-team games around turns; `FakeRules` is the double to use when a sequence test needs turns around it.

---

## Interfaces needed from other modules

From the **interpreter plan**, whose names are used as they stand:

1. `Engine.IScheduler { IReadOnlyList<EventPayload> Advance(GameState, ISequenceStarter) }` and `Engine.ISchedulerFactory { IScheduler Create(IGameRules, GameInfo, PropertySet) }` (its Task 10) — implemented by `SchedulerBase` and `SchedulerFactory`. `RulesEngine.Advance` calls the scheduler when no framed Ask is open and the stack is empty; the scheduler returns `[]` while an unframed Ask of its own is live and closes one a menu edit superseded (the engine's one `ask_closed`).
2. `Interpreter.ISequenceStarter.Start(GameState, string sequence, SequenceContext)` → `[sequence_started]` (its Task 8) — how the declared action's sequence, the end-of-turn sequences and the roll-off are started. Steps run in the interpreter's `Advance`.
3. `IGameRules.ActionSequence(string) → string?` — the action row's sequence, null for a name with none (an out-of-set answer under `warn`/`silent`), which the scheduler records and treats as an activation that ran nothing. `PropertySet.Actions` (`ActionDefinition.Name`) are the action rows the turn Ask offers.
4. `IGameRules.OnRoundStarted(GameState, PropertySet, int round)`, `EndOfTurnSequences(GameState, PropertySet, TurnContext)`, `OnTurnEnded(GameState, PropertySet, TurnContext)` with their `GfHooks` bodies (its Task 11) — fatigue and tokens at the round boundary (round 0 → none), the end-of-activation morale tests (units named as actor or target of a `melee` frame this activation excepted), the wounds-this-activation reset and Idle-clears-Shaken at turn end. **One thing for that plan to settle, noted here rather than changed there:** `GfHooks.OnTurnEnded` clears `shaken` only when `turn.Activation.Action == ActivationOption.Idle`, but page 13 says an Aircraft's mandatory Advance "doesn't break the staying idle requirement to stop being Shaken", so a Shaken Aircraft — which `ActionsFor` offers `Advance` (Task 2) — stops being Shaken too. The condition belongs in `GfHooks`, not here; `ActivationOption.Idle` stays a `const string` either way, as that plan's pattern match requires.
5. `GfRolls.RollOffSequence()` (its Task 11): **two** steps, `GfNames.RollOffRollStep` (`"roll-off"`) — one die per team in ordinal order, ties rerolled among the tied until every team is ordered — and `GfNames.RollOffOrderStep` (`"order"`), which ranks the dice that stand and carries `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]` (team ordinals, best first). `FirstTeamDecision.RollOffNeeded(GfNames.RollOffSeq)` names the sequence; `RollOff.Order` reads the values off the completed step that carries `place_1`, so neither step name appears in this plan's code.
6. **The roll-off's confirm gate** (that plan's Task 8). The scheduler consumes exactly four facts about it:
   - The boundary between the two steps is an unsealed `Confirm` Ask **to every team**, opened whatever `waits_between_steps` says, because the frame has no actor and no parent. So every roll-off waits, and any one team releases it.
   - The frame **stays open until that answer arrives**. `RollOff.Advance` returns `Waiting` for the whole interval and starts nothing more.
   - `sequence_finished` for the frame is the signal that the order stands. The scheduler reads `place_k` only on the first `Advance` after it, so no `round_started.Order` is ever written from dice a player may still edit.
   - The gate's Ask is **framed** (`Ask.FrameId` non-null), so `SchedulerLog.IsSchedulerAsk` never claims it and `SchedulerBase.StaleAsks` never closes it.
   The one obligation running the other way is recorded in that plan's Interfaces needed 4: the ranking must be derived **after** the gate, which is why the sequence is two steps. `RollOff.Order` reads values rather than dice and cannot rank a roll-off itself, so a ranking recorded before the gate would leave the scheduler writing the pre-edit order — `dice_edited` reaches into a `RollRecord` and never rewrites a logged `step_completed`.
   Two consequences of `RulesEngine.Advance`'s ladder are worth stating, since they are that plan's to keep: while the gate's framed Ask is open the scheduler is not called at all (branch 1), and while the frame is on the stack the interpreter runs instead of the scheduler (branch 2). So the engine's own wait during a gated roll-off shows up as the engine resting, not as `RollOff.Waiting` — the reader is what answers correctly whenever it *is* asked, which is the property that matters for undo, rehydration and any later change to that ladder.
7. `Interpreter.Windows.ReactionOrder(GameState, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)` (its Task 9) — the one reaction-ordering policy, consulting this plan's `IGameRules.OrderReactions` first; this plan defines no ordering type.
8. `Effects.Lookup.UnitHas(GameState, PropertySet, Guid unitId, string entry)` (its Task 6) — the name check `LegalTurn`, `DeployAsk` and `Ambushers` use for `Immobile`, `Aircraft`, `Scout`, `Ambush`, so the grants store counts.
9. `GfNames` (its Task 11) — the one names class, and it already declares every name this plan reads: `Deployment`, `Scouts`, `Rounds`, `Deploy`, `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `SetAside`. This plan adds none of them; `GfNames.Rounds = Scheduler.SchedulerBase.RoundsPhase` is that file's one dependency on this one (Task 5). Conditions the interpreter writes and the scheduler reads: `shaken` and `fatigued` on the **unit** subject; `set-aside` is the scheduler's (menu-written).
10. `GrimdarkFutureRules` is the skeleton's `sealed partial class` in `Rules/GrimdarkFuture/GfRules.cs`; this plan adds `GfRules.Scheduling.cs` and `GfRules.Deployment.cs`, the interpreter plan `GfRules.Sequences.cs`; nobody redeclares `RulesId`, `Instance` or `Id`.
11. Test infrastructure `Sim`, `TestProperties.Core`, `LambdaStep`, `ScriptedDice`, `InMemoryPropertiesStore` (its Tasks 3, 8, 10) — `SchedulerHarness` wraps a `Sim`; `FakeRules` implements the whole merged `IGameRules`.

From the **contracts plan**: `AskQuestions` declares the engine-owned question `EndRound = "end_round"`, beside `Activate`, `ActOrEndPhase`, `Deploy`, `DeployScouts`, `DeployAmbusher`, `Confirm`, `React`, `TokenSpend`. It is one string constant and one row of that plan's `AskQuestions` table; nothing else in the Protocol is involved, because question names are free strings (identity and enumerations → question names) and the Ask itself is the existing `Ask` with a `Confirmation` legal set, `FrameId` null. Both the server (`SchedulerLog.Questions`, `SchedulerBase.EndRoundAsk`) and the client (the client-shell plan's confirm-button label and log line) read it from there, which is why it is not a constant of this plan's.

From the **skeleton plan**: `ConnectionHandshake`, `PostgresFixture` and `Program.cs` are modified by the interpreter plan (its Tasks 4 and 10), not here; this plan's one `Program.cs` change replaces the `NoSchedulerFactory` registration. Two more of its files are modified here, both in Task 4 and both because an unreadable turn answer follows the enforcement dial (ruling 3): `Dispatch/CommandDispatcher.cs`, whose `Answer` computes `Legality` from set membership **and** readability so an unreadable turn answer follows the dial (no signature changes: `state.Info.Enforcement` and `GameState` are already its arguments), and `CommandDispatcherTests`, whose `activate` fixture offers the option id `"a"` — in that Ask's legal set and unreadable, so under `strict` it would be refused where the file expects `Applied`. It becomes `Samples.Unit.ToString("N") + "/Advance"`, with the two `Chosen("a")` answers following; `Chosen("zzz")` stays out of set, no test is added or removed, and the fixture is truer for it.

From the **client shell plan**, nothing the server depends on. The boundary Ask needs no new client mechanism — it is an unframed `Confirmation` addressed to several teams, which the Ask panel already renders — but that plan does two things about it, in its own Tasks 8 and 14: the confirm button reads "End the round" instead of "Continue", and the answer's log line reads "confirmed for Team 1" instead of "answered for Team 1". Window mutes stay client-side (client shell → Asks and windows; action sequences → Windows), so the server filters nothing per player. The activate option id is `"{unitId:N}/{action}"`; the client renders `AskOption.Label` and `Pieces` and never parses it.

## Rulings

1. **Deployment alternates one unit a turn under both shapes.** Scheduler → Deployment describes the phase once and shape-independently — one unit per Ask, the turn ending when that unit's last model has a placement, a team with nothing left skipped — and page 6 is *"the players continue alternating in placing one unit each, until all units have been deployed"*. The shape sections govern rounds and turns only.
2. **The players set a unit aside as a Scout or Ambusher themselves, with `set_condition`, whenever they like; the deploy Ask's legal set excludes a set-aside unit.** Scheduler → Deployment makes set-aside a condition on the shelved unit; game flow → Nothing is an invariant makes every property editable from the menu as an ordinary command, through the same path automation writes. There is no *set aside or deploy* Ask, because no spec lists that phase.
3. **An unreadable turn answer follows the enforcement dial** — refused under `strict`, and under `warn` and `silent` treated as a pass, with a warning logged under `warn` — so the unreadable case and the out-of-set case are one rule. Both are one clause in `CommandDispatcher.Answer` (Task 4, Step 3), which adds a readability probe beside `LegalSetMembership.Contains` and hands the same `Legality.OutsideLegalSet` to `EnforcementPolicy.Decide`. The dispatcher is where it has to be: it already holds `state.Info.Enforcement` and `GameState`, so nothing is threaded, and it is the only point at which a refusal still leaves the Ask open — the dispatcher closes an unsealed Ask on the first answer, so a refusal after the answer was logged would leave the scheduler facing a closed turn Ask it refuses to read with no open Ask to answer. Under `strict` the turn Ask stands and the same team may answer as often as it likes, each unreadable answer refused; nothing waits on the turn ending, because the round boundary is a player's own `end_round` Ask and the engine rests while an unframed Ask of the scheduler's is live. `TurnCycle.Read` maps `ReadAnswer` → null to `PassAnswered` with no dial in it, which is what makes a state rebuilt from the log independent of a setting the players may have changed since. For every answer a real game can produce the two conditions coincide, because a legal set only ever offers well-formed ids for units the log has, so the probe changes behaviour only for an in-set answer the ruleset cannot read — a ruleset that offered a model id where a unit id belongs.
4. **The round does not advance on its own; a player ends it**, to avoid an accidental auto-advance. The scheduler reaches the end of a round and stops, visibly waiting, with one unframed Ask of its own — question `AskQuestions.EndRound`, a `Confirmation`, `AllowPass` false, addressed to every team that has a participant — and the first answer to it is what emits the next round's `round_started`. Any player on any team may answer; the Ask is unsealed, so the dispatcher closes it on that first answer. It is the same rule for a wiped or idle table, which is why the degenerate case needs no round cap: nothing counts up while nobody has said to. The Ask sits before `round_started`, so `OnRoundStarted`'s changes still ride on the new round's event and `RoundBoundaryStep` still runs after it — fatigue and spell tokens land in the round they belong to. `SetRound` and `SetActiveTeam` are the menu overrides that supersede a live scheduler Ask; deployment's phases and its flip into round one are the round-0 branch, which never reaches the gate. Built in Task 5 (`SchedulerBase.EndRoundAsk`, `RoundEnded`); scheduler → There is no round limit and game flow → Flow state the same rule.
5. **An abandoned action ends the turn.** Scheduler → The turn is an Ask: *"the activation is written to the unit's action record when the sequence starts, not when it ends, so an abandoned sequence still counts as the unit having gone"* — and under the alternating shape a turn is one activation, so a unit that has gone ends it. Action sequences → abandonment agrees: pop the frames, applied effects stay.
6. **Under the player-turn shape the first team from round two is page 7's finished-first rule, as under the alternating shape.** The first-team policy is a `GameRules` hook, and scheduler → The two shapes ends *"Only OPR's `GameRules` is built, and it runs under both shapes"* — the hook is handed the round and the teams, never the shape, and page 7's finished-first rule is the only rule OPR has. A `FirstTeam` that branched on `SchedulerShape` would be `GameRules` knowing about the scheduler, which the whole seam exists to prevent.
7. **The roll-off's dice are editable: the roll-off is gated like any other roll.** The interpreter owns the gate; the scheduler's side is the reader. `RollOff.Advance` keys `Resolved` on `sequence_finished`, not on the frame leaving the stack, returns `Waiting` for the whole interval between the two, and hands `Order` the frame's last completed step carrying `place_1` — the ranking step, which the interpreter runs after the gate — so a die edited behind the gate decides the order and the order is never read from dice a player may still change (Task 3, Task 6's gated test). The roll-off spans three dispatches — the start, the confirm, and the `Advance` after the frame finishes — and nothing is carried between them, because every branch is re-derived from the log. Deployment's shape is the same either way: `round_started(0, order, [])` and `phase_started("deployment")` are one batch, emitted by the `Advance` that reads the resolved order.
