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
    static Subject UnitSubject(Guid id) => TestGames.UnitSubject(id);

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
        Assert.True(idle.RunsSequence); Assert.Equal(GfNames.IdleSeq, Rules.ActionSequence(ActivationOption.Idle)); Assert.True(Rules.ActivationComplete(g.State, idle));
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
    public void Artillery_and_Immobile_units_may_only_hold()
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 1, unitRules: [(1, Artillery, null), (2, Immobile, null)])]);
        foreach (var unit in g.State.Projection.Units.Values)
            Assert.Equal(["Hold"], GrimdarkFutureRules.ActionsFor(g.State, Core, unit, Rows));           // p.13 Artillery: "May only use Hold actions"
        TestGames.Apply(g, new PhaseStarted(Rounds));
        var units = Units(g, 0);
        TestGames.Apply(g, TestGames.Place(g.State, units[0]));
        Assert.Equal([TestGames.Activate(units[0])], Assert.IsType<Options>(Rules.LegalTurn(g.State, Core, g.Teams[0])!.Legal).Choices.Select(c => c.Id));
    }

    [Fact]
    public void Artillery_and_Immobile_declared_on_every_model_are_read_through_aggregation_and_mean_hold_only()
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 2, modelRules: [(1, Artillery, null), (2, Immobile, null)])]);
        var units = Units(g, 0);
        TestGames.Apply(g, TestGames.Place(g.State, units[0]), TestGames.Place(g.State, units[1]));
        foreach (var id in units)
            Assert.Equal(["Hold"], GrimdarkFutureRules.ActionsFor(g.State, Core, g.State.Projection.Units[id], Rows));
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
        TestGames.Apply(g, new SequenceStarted(Guid.CreateVersion7(), HoldSeq, new SequenceContext(b[0], [], [], Hold)),   // the root frame GfHooks anchors "this activation" on
            new SequenceStarted(Guid.CreateVersion7(), Melee, new SequenceContext(b[0], [a[0]], [], null)));
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
        var cleared = Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order));           // arrived: set-aside is cleared before the next Ask
        Assert.Equal(new ConditionSet(UnitSubject(b[0]), SetAside, false), Assert.Single(cleared.Events));
        TestGames.Apply(g, cleared.Events.ToArray());
        var second = Assert.IsType<AskOpened>(Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order)).Events.Single()).Ask;
        Assert.Equal([g.Teams[0]], second.Teams);
        TestGames.Apply(g, new AskOpened(second), new AskClosed(second.Id, [new TeamAnswer(g.Teams[0], new Passed())]));
        Assert.IsType<HookDone>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                       // team 1 has none left, team 0 passed
        Assert.Empty(g.State.Projection.Units[b[0]].Actions);                                      // deploying wrote nothing to the record
    }
}
