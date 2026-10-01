# Task 6: Deployment and the round boundary end to end — Scouts, Ambushers, the confirm gate, free-for-all

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/DeploymentTests.cs`, `tests/Tabletop.GameServer.Tests/RoundBoundaryTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5 (no new production code: this task proves the base, the OPR hooks and the roll-off compose as the spec says, and fixes whatever does not).
- Produces: nothing new.

Spec: scheduler → Deployment (Scouts deploy after every other unit, alternating, "starting with the player that activates next" — the roll-off winner; a second deploy Ask over the set-aside Scouts), The round boundary (hooks in order; Ambushers from round two, alternating, until every team passes; deploying an Ambusher writes nothing to the record), Windows/Enforcement unaffected; action sequences → One roll primitive (the confirm gate when waits is on). **The roll-off is gated like any other roll** (ruling 7): `GfRolls.RollOffSequence` is two steps, `roll-off` then `order`, and the boundary between them is one unsealed `Confirm` Ask to every team, opened whatever the wait setting says because the frame has no actor and no parent (its Task 8, Interfaces needed 4). So **every** roll-off in this file waits at a gate — which is why `SkipDeployment` releases one and the tests that drive `Run()` by hand call `ConfirmRollOff`. The gated test below is the one that pins the shape: the dice are editable while the frame waits, and the `order` step ranks whatever stands when a team confirms, so nothing ever rewrites a logged `step_completed` and the reader keys on `sequence_finished` alone. The `end_round` Ask (ruling 4) sits in front of all of it: it precedes `round_started`, so the fatigue and token changes, the first-team roll-off and the Ambusher loop all happen after the new round has begun, and these tests pin that — round one's fatigue is still set while the boundary Ask is open. Rulebook page 50: a free-for-all rolls off once, for deployment order, and orders every round by who finished activating first; page 13 `Ambush`; page 14 `Scout`. An Ambusher's arrival waits until none of its models is on the shelf and then logs its `set-aside` condition cleared, so a destroyed Ambusher is never offered again (W-I3, W-M1). `set_round` mid-deployment moves the game to the `rounds` phase (W-I2).

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

    [Fact]
    public void Scouts_deploy_after_every_other_unit_starting_with_the_roll_off_winner()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 2, 1, unitRules: [(1, Scout, null)])]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);                                    // B wins
        h.Run(); h.ConfirmRollOff();                                             // the roll-off's gate, released with the dice as rolled
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true)); h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), SetAside, true));   // set aside from the menu
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
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(1, h.P.Scheduler.Round);                                                             // deployment ended without a[1]
        Assert.All(h.P.Units[a[1]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, false)); h.Dispatch(TestGames.Move(h.State, a[1]), team: 0);   // the menu puts it on the table later
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

    [Fact]
    public void Ambushers_are_asked_at_the_start_of_round_two_and_may_activate_that_round()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);                                              // the round waits to be ended before anything else happens
        seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "ask_opened"], h.Kinds(seq));          // the boundary Ask sits before round_started, so every hook still lands in the new round
        Assert.Equal((2, (Guid?)null, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // a boundary Ask, not a turn
        Assert.Equal([h.Teams[0]], h.Open.Teams);                                                          // A finished first and is asked first
        h.Answer(0, new SelectedPieces([a[1]]));
        Assert.Empty(h.P.OpenAsks); Assert.Null(h.P.Scheduler.ActiveTeamId);                                // waiting for the models
        seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "condition_set", "turn_started", "ask_opened"], h.Kinds(seq));      // the arrival clears set-aside; A has no more Ambushers, B never had one: the hook is done
        Assert.DoesNotContain(SetAside, h.P.Units[a[1]].Conditions);
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
        h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), Fatigued, true));
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Contains(Fatigued, h.P.Units[b[0]].Conditions);                                    // the boundary Ask is open and no hook has run: fatigue still belongs to round one
        Assert.Equal(2, h.P.Models[caster].SpellTokens);                                          // round one's own boundary granted Caster(2); round two has granted nothing yet
        h.EndRound();
        var round = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal([typeof(ActionRecordCleared), typeof(ActionRecordCleared), typeof(ConditionSet), typeof(SpellTokensSet)], round.Changes.Select(c => c.GetType()));
        Assert.Equal(4, h.P.Models[caster].SpellTokens); Assert.Empty(h.P.Units[b[0]].Conditions);             // two from round one, two more from round two (capped at 6)
        Assert.Empty(((RoundStarted)h.Log.First(e => e.Payload is RoundStarted).Payload).Changes);   // round 0 carried none
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
            h.Answer(team, new Chosen(TestGames.Activate(((Options)h.Open.Legal).Choices[0].Pieces[0])));
        }
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));
        h.EndRound(team: 1);
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // no roll-off at a round boundary, so no confirm gate either
        Assert.Equal([h.Teams[2], h.Teams[0], h.Teams[1]], h.P.Scheduler.Order);              // C activated first and once, A once, B twice and last
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(TestGames.Activate(h.Units(2)[0]))); h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                             // C and A are finished: B goes twice running
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Setting_the_round_from_the_menu_mid_deployment_moves_to_the_rounds_phase_and_offers_an_activation()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 2, 1)]);
        h.Dice.Enqueue(1); h.Dice.Enqueue(6); h.Run(); h.ConfirmRollOff();
        var b = h.Units(1);
        h.Answer(1, new SelectedPieces([b[0]])); h.Deploy(b[0]);
        Assert.Equal((0, Deployment), (h.P.Scheduler.Round, h.P.Scheduler.Phase));
        h.Dispatch(new SetRound(1));
        Assert.Equal((1, SchedulerBase.RoundsPhase), (h.P.Scheduler.Round, h.P.Scheduler.Phase));
        Assert.Equal((AskQuestions.Activate, h.Teams[1]), (h.Open.Question, h.Open.Teams.Single()));      // b[0] is on the table: a turn opens
    }

    [Fact]
    public void An_ambusher_waits_until_every_model_is_placed_and_a_destroyed_one_is_not_offered_again()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.EndRound();
        h.Answer(0, new SelectedPieces([a[1]]));
        var all = TestGames.Relocations(h.State, a[1]);
        h.Dispatch(new MoveModels([all[0]]), team: 0);
        Assert.Empty(h.P.OpenAsks);                                                                        // one of two models placed: still arriving
        Assert.Contains(SetAside, h.P.Units[a[1]].Conditions);
        h.Dispatch(new MoveModels([all[1]]), team: 0);
        Assert.DoesNotContain(SetAside, h.P.Units[a[1]].Conditions);
        h.Dispatch(new MoveModels(all.Select(r => new ModelRelocation(r.ModelId, r.To, new OnShelf())).ToList()), team: 0);   // destroyed: every casualty back on the shelf
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);
        h.EndRound();
        Assert.Equal(3, h.P.Scheduler.Round);
        Assert.DoesNotContain(h.Log, e => e.Payload is AskOpened o && o.Ask.Question == AskQuestions.DeployAmbusher && e.Seq > h.Log.Last(x => x.Payload is RoundStarted).Seq);
    }

    [Fact]
    public void Ambushers_alternate_between_teams_starting_with_the_team_that_activates_next()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 3, 1, unitRules: [(2, Ambush, null), (3, Ambush, null)]), TestArmies.Build("B", 2, 1, unitRules: [(2, Ambush, null)])]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        foreach (var u in new[] { a[1], a[2], b[1] }) h.Dispatch(new SetCondition(TestGames.UnitSubject(u), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.EndRound();
        var asked = new List<Guid>();
        while (h.P.OpenAsks.Count > 0 && h.Open.Question == AskQuestions.DeployAmbusher)
        {
            var team = h.Teams.ToList().IndexOf(h.Open.Teams.Single());
            var unit = ((PieceSelection)h.Open.Legal).Eligible[0];
            asked.Add(h.Open.Teams.Single());
            h.Answer(team, new SelectedPieces([unit])); h.Deploy(unit);
        }
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[0]], asked);                                          // p.13: A, then B, then A again; a first-eligible pick would ask A twice running
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "Deployment|RoundBoundary"`; expect 9 passed. Fix any base-class defect these expose in `SchedulerBase.cs`, never by weakening an assertion the spec supports. Commit: `Prove deployment, Scouts, Ambushers and the free-for-all boundary end to end`.**
