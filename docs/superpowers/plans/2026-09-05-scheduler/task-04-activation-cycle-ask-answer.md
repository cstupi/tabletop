# Task 4: The activation cycle — Ask, answer, activation, sequence, end-of-turn hook

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
