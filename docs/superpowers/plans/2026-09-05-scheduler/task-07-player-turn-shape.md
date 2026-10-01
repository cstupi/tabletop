# Task 7: The player-turn shape

**Files:**
- Create: `server/game/Scheduler/PlayerTurnScheduler.cs`
- Test: `tests/Tabletop.GameServer.Tests/PlayerTurnSchedulerTests.cs`

**Interfaces:**
- Consumes: `SchedulerBase` and its hooks (Task 5), `TurnCycle.AskFor`, `AskQuestions.ActOrEndPhase`, `IGameRules.PhasesInTurn`.
- Produces: `sealed class PlayerTurnScheduler(PropertySet) : SchedulerBase`.

Spec: scheduler → The two shapes (player-turn: a team holds the turn for every activation until its legal set is empty or it passes; the turn is a list of phases from `GameRules`, each an Ask loop of *act or end phase* with the legal set read per phase; OPR has one phase and activates every unit in a row; the team's record clears at its `turn_ended`; the round ends when every team has had a turn); game flow → Two schedulers (each owns when the record clears). The first-team policy is the same hook, so OPR under this shape still rolls off before deployment and gives the winner round one's first turn. Deployment (round 0) alternates one unit a turn under this shape too, because scheduler → Deployment describes the phase once rather than per shape, and page 6 alternates units regardless. The `end_round` Ask (ruling 4) is the base's, so it needs nothing here: "the round ends when every team has had a turn" means the boundary Ask opens then, and `IsFinished`, `ShapeTurnEndChanges` and the phase loop have no part in it — this task's tests only answer it. p.10's resets happen "at the end of the activation", and a turn here holds many: when a cycle completes or passes, `ActivationResets` logs `OnTurnEnded`'s changes as bare state changes before the next Ask, and the run at `turn_ended` stays, so the hook must be idempotent (W-I1).

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
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "ask_opened"], h.Kinds(seq));   // same turn, next Ask
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal([a[1]], Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());
        seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.False(ended.Passed);
        Assert.Equal([new ActionRecordCleared(a[0]), new ActionRecordCleared(a[1])], ended.Changes.OrderBy(c => a.ToList().IndexOf(((ActionRecordCleared)c).UnitId)).ToList());
        Assert.All(a, u => Assert.Empty(h.P.Units[u].Actions));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question); Assert.Equal(h.Teams, h.Open.Teams);                   // both shapes stop at the boundary and both ask every team
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
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        var seq = h.P.LastSeq; h.Answer(0, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.True(ended.Passed); Assert.Single(ended.Changes);                                            // a[0]'s record cleared; a[1] never activated
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(a[0]));
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
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
        h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));
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
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // A holds the turn for its second unit
        h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(TestGames.Activate(h.Units(2)[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question); h.EndRound(team: 2);
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[2]], h.P.Scheduler.Order);                          // finished-first repeats the order here: under player-turn a team finishes when its units run out
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // p.50: the only roll-off was for deployment
        h.Rehydrate(); Assert.Empty(h.Run());
    }
    [Fact]
    public void A_unit_wounded_to_half_is_tested_once_not_after_every_later_activation_of_the_turn()
    {
        var h = Game(2, 2); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b0 = h.Units(1)[0];
        h.Append([new ModelsMoved([new ModelRelocation(h.P.Units[b0].Definition.Models[0].Id, h.P.Models[h.P.Units[b0].Definition.Models[0].Id].Location, new OnShelf())]), new WoundsThisActivationSet(b0, 1)]);
        int Tests() => h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.MoraleTest && s.Context.ActorUnitId == b0);
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(1, Tests());
        Assert.Equal(0, h.P.Units[b0].WoundsThisActivation);
        h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(1, Tests());                                                                          // p.10: at the end of the activation that wounded it, not again at the end of the next
    }

    [Fact]
    public void A_shaken_unit_that_idles_in_a_mid_turn_activation_stops_being_shaken_before_the_turn_ends()
    {
        var h = Game(2, 1); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0);
        h.Append([new ConditionSet(TestGames.UnitSubject(a[0]), GfNames.Shaken, true)]);
        h.Answer(0, new Chosen(TestGames.Activate(a[0], ActivationOption.Idle)));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                              // the turn is still A's
        Assert.DoesNotContain(GfNames.Shaken, h.P.Units[a[0]].Conditions);
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
        if (ActivationResets(state, turn) is { Count: > 0 } resets) return resets;
        var ask = state.Rules.LegalTurn(state, Properties, turn.TeamId);
        return ask is null ? NextPhaseOrEnd(state, log, turn, passed: false) : [new AskOpened(TurnCycle.AskFor(turn.TeamId, ShapeAsk(state, ask)))];
    }

    protected override IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn) =>
        ActivationResets(state, turn) is { Count: > 0 } resets ? resets : NextPhaseOrEnd(state, log, turn, passed: true);

    /// <summary>
    /// p.10's resets happen at the end of the activation, and a turn here holds many: the ruleset's turn-end changes are logged as bare state
    /// changes when a cycle completes or passes, before the next Ask. The hook is idempotent, so the next call finds nothing and goes on;
    /// the run at turn_ended stays.
    /// </summary>
    private IReadOnlyList<EventPayload> ActivationResets(GameState state, TurnContext turn) =>
        state.Rules.OnTurnEnded(state, Properties, turn).Cast<EventPayload>().ToList();
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
            : ClearRecords(TeamUnits.Of(state, turn.TeamId));
}
```

When a named in-turn phase starts, `PhaseStarted` moves the rotation anchor; that is harmless inside a turn (the rotation is only consulted between turns) and the base's stale-Ask cutoff uses `turn_started`, not the phase. `Projection.Scheduler.Phase` then reads the in-turn phase name until the next `round_started`'s phase; OPR has no in-turn phases so its `Phase` stays `rounds` (scheduler → State).

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PlayerTurnScheduler`; expect 7 passed; run the whole project and expect everything green. Commit: `Add the player-turn shape`.**
