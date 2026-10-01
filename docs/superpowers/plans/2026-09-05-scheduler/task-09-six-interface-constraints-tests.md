# Task 9: The six interface constraints, as tests

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/SchedulerConstraintTests.cs` (the interpreter plan's Task 18 owns `InterfaceConstraintTests.cs` in this project; the two files sit side by side)

**Interfaces:**
- Consumes: `FakeRules` and `SchedulerHarness` (Task 5), both shapes.
- Produces: nothing new. If a constraint cannot be met without a production change, the change goes in the file the constraint names and is small; none is expected.

Spec: scheduler → What the interface must not assume, 1–6. Each test drives the generic scheduler with a `FakeRules` whose hook answers the 40k way, without any 40k rule being built. Constraint 2's *alternation inside a phase* is not built (the spec says so); the test covers the part that is — a phase list inside a turn, each phase its own Ask loop. Constraint 5's *whole sequence inside another team's turn* is the interpreter's; the scheduler's share is not to mistake a framed Ask addressed to the non-active team for its own, tested here. The `end_round` Ask is generic engine behaviour, not a policy `GameRules` can replace (ruling 4), so the constraint tests that cross a round boundary answer it like any player would; no constraint asks for a ruleset to advance rounds by itself, and if one ever did it would be a hook, not a branch.

- [ ] **Step 1: Write the tests**

```csharp
using Tabletop.GameServer.Interpreter;
using Interpreter = Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SchedulerConstraintTests
{
    static SchedulerHarness Game(FakeRules rules, bool playerTurn = false) => new(
        [TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1)],
        scheduler: playerTurn ? p => new PlayerTurnScheduler(p) : null, rules: rules,
        shape: playerTurn ? SchedulerShape.PlayerTurn : SchedulerShape.AlternatingActivation);

    static SchedulerHarness PlacedUnrun(FakeRules rules, bool playerTurn = false)
    {
        var h = Game(rules, playerTurn);
        foreach (var u in h.P.Units.Keys.ToList()) h.Append([TestGames.Place(h.State, u)]);
        return h;
    }
    static SchedulerHarness Placed(FakeRules rules, bool playerTurn = false) { var h = PlacedUnrun(rules, playerTurn); h.Run(); return h; }

    [Fact]
    public void One_the_first_team_each_round_is_a_policy_the_same_team_every_round_needs_no_roll_off()
    {
        var h = Placed(new FakeRules());                                                                 // no phases before round one, same order every round
        Assert.Equal((1, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.DoesNotContain(h.Log, e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq);
        Assert.DoesNotContain(h.Log, e => e.Payload is PhaseStarted { Phase: "deployment" });
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        h.EndRound();                                                                                     // a player ends the round whatever the first-team policy is
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // OPR would have put B first
        Assert.Equal(h.Teams, h.P.Scheduler.Order);
    }

    [Fact]
    public void Two_a_turn_may_be_a_list_of_phases_each_with_its_own_ask_loop()
    {
        var h = PlacedUnrun(new FakeRules { PhasesInTurn = ["movement", "shooting"] }, playerTurn: true);
        var seq = h.P.LastSeq; h.Run();
        Assert.Equal(["round_started", "phase_started", "turn_started", "phase_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal("movement", h.P.Scheduler.Phase);
        seq = h.P.LastSeq; h.Answer(0, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "phase_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal(("shooting", h.Teams[0]), (h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));
        seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));
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
        var h = Placed(rules);
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
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
        var h = Placed(rules);
        var a = h.Units(0);
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
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
        var h = Placed(rules);
        var a = h.Units(0);
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
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
        Assert.Equal([g.Teams[1], g.Teams[2]], Interpreter.Windows.ReactionOrder(g.State, g.Teams[0], [g.Teams[2], g.Teams[1]]));   // the default
        var chooser = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)],
            rules: new FakeRules { OrderReactionsHook = (_, _, reacting) => reacting });                                                    // "the active player chooses": as sent
        TestGames.Apply(chooser, new RoundStarted(1, chooser.Teams, []));
        Assert.Equal([chooser.Teams[2], chooser.Teams[1]], Interpreter.Windows.ReactionOrder(chooser.State, chooser.Teams[0], [chooser.Teams[2], chooser.Teams[1]]));
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter SchedulerConstraint`; expect 6 passed. Commit: `Test the six 40k-derived constraints on the scheduler interface`.**
