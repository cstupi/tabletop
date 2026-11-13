using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause by clause, 3.6.0 army rules on Shaken and failed Morale Tests (army-rules.part.json ruleText).
///
/// Steadfast, Battleborn, Honor Code (one text): "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+,
/// it stops being Shaken."
///   R1 most models (a joined Hero without it still leaves most; one of three is not most); R2 only a Shaken unit; R3 at the beginning of the round, once;
///   R4 one die; R5 on a 4+ it stops being Shaken, below it stays.
///
/// No Retreat: "When a unit where most models have this rule fails a Morale Test that causes it to be Shaken or Routed, the test counts as passed instead.
/// Then, roll as many dice as the number of wounds it would take to fully destroy it, and for each result of 1-3 the unit takes one wound, which can't be
/// ignored."
///   NR1 most models; NR2 a failure that would shake counts as passed; NR3 a failure that would rout counts as passed; NR4 as many dice as the wounds to
///   destroy it (Tough and wounds already taken counted); NR5 each 1-3 one wound, 4-6 none; NR6 can't be ignored (Regeneration rolls nothing);
///   negative: a passed test rolls nothing; a test that causes neither (the unit is already Shaken) is not one No Retreat answers.
/// </summary>
public class ClausesActivateMoraleRulesTests
{
    static readonly PropertySet Core = TestProperties.Core36;

    static void Apply(GameState s, params EventPayload[] events)
    {
        foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null));
    }

    static ConditionSet Shake(Guid unit) => new(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true);

    static Unit Some(string rule, int holders, int models)
    {
        var u = Unit("A", "4+", "4+", [], models, []);
        return u with { Models = [.. u.Models.Select((m, i) => i < holders ? m with { Properties = [P(rule)] } : m)] };
    }

    // ---- Steadfast, Battleborn, Honor Code --------------------------------------------------------------------------------------------

    static IReadOnlyList<SequenceStart> RoundStart(GameState s) =>
        GrimdarkFutureRules.V360.RoundBoundaryStep(s, Core, 1, [Samples.Team1, Samples.Team2]) is HookStartSequences h ? h.Sequences : [];

    [Theory]
    [InlineData("Steadfast", 4, false)]
    [InlineData("Battleborn", 4, false)]
    [InlineData("Honor Code", 4, false)]
    [InlineData("Steadfast", 3, true)]
    [InlineData("Battleborn", 3, true)]
    [InlineData("Honor Code", 3, true)]
    public void A_shaken_unit_rolls_one_die_at_the_round_start_and_a_4_plus_ends_its_shaken(string rule, int die, bool stillShaken)
    {
        var a = Unit("A", "4+", "4+", [], 2, [P(rule)]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, new RoundStarted(1, [Samples.Team1, Samples.Team2], []), Shake(a.Id));
        var start = Assert.Single(RoundStart(s));                                                                // R3
        Assert.Equal((GfNames.RallySeq, a.Id, rule), (start.Sequence, start.Context.ActorUnitId!.Value, start.Context.Action));
        var sim = Sim.Gf36(s, die);
        sim.Start(start.Sequence, start.Context);
        var roll = Assert.Single(sim.Emitted.OfType<StepCompleted>().SelectMany(x => x.Rolls), r => r.Roll == rule);
        Assert.Single(roll.Dice);                                                                                 // R4
        Assert.Equal(stillShaken, sim.Unit(a.Id).Conditions.Contains(GfNames.Shaken));                          // R5
        Assert.Empty(RoundStart(sim.State));                                                                      // once, at the round's beginning
    }

    [Theory]
    [InlineData("Steadfast", 2, 3, true)]
    [InlineData("Steadfast", 1, 3, false)]
    [InlineData("Battleborn", 1, 2, false)]
    [InlineData("Honor Code", 2, 3, true)]
    public void Only_a_unit_where_most_models_have_it_rolls(string rule, int holders, int models, bool rolls)
    {
        var a = Some(rule, holders, models);                                                                      // R1: the others stand for a joined Hero without it
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, new RoundStarted(1, [Samples.Team1, Samples.Team2], []), Shake(a.Id));
        Assert.Equal(rolls, RoundStart(s).Count == 1);
    }

    [Fact]
    public void A_unit_that_is_not_shaken_rolls_nothing()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Steadfast")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, new RoundStarted(1, [Samples.Team1, Samples.Team2], []));
        Assert.Empty(RoundStart(s));                                                                              // R2
    }

    // ---- No Retreat --------------------------------------------------------------------------------------------------------------------

    /// <summary>The morale test, then each wound allocation asked of team 1 answered on the first eligible models.</summary>
    static Sim Test(GameState s, Guid unit, string cause, params int[] dice)
    {
        var sim = Sim.Gf36(s, dice);
        sim.Start(GfNames.MoraleTest, new SequenceContext(unit, [], [], cause));
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { Legal: Allocation al }; i++)
            sim.Answer(Samples.Team1, new Allocated(al.Targets.Take(al.MaxTargets ?? al.Amount).Take(al.Amount).ToDictionary(t => t, _ => 1)));
        return sim;
    }

    static IReadOnlyList<RollRecord> Rolls(Sim sim, string roll) => sim.Emitted.OfType<StepCompleted>().SelectMany(x => x.Rolls).Where(r => r.Roll == roll).ToList();

    [Fact]
    public void A_failure_that_would_shake_counts_as_passed_and_each_1_to_3_is_a_wound()
    {
        var a = Unit("A", "4+", "4+", [], 3, [P("No Retreat")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        var sim = Test(s, a.Id, "wounds", 1, 3, 4, 6);                                                            // fails; No Retreat 3, 4, 6
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(a.Id).Conditions);                                       // NR2
        Assert.Equal(1, sim.Steps("consequence").Last().Values!["passed"]);
        Assert.Equal(3, Assert.Single(Rolls(sim, GfNames.NoRetreat)).Dice.Count);                                // NR4: three one-wound models
        Assert.Equal(2, sim.Alive(a.Id));                                                                         // NR5: the 3 wounds, the 4 and 6 do not
    }

    [Fact]
    public void A_failure_that_would_rout_after_melee_counts_as_passed()
    {
        var a = Unit("A", "4+", "4+", [], 4, [P("No Retreat")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, new ModelsMoved([.. a.Models.Take(2).Select(m => new ModelRelocation(m.Id, s.Projection.Models[m.Id].Location, new OnShelf()))]));   // half left
        var b = Unit("B", "4+", "4+", [], 4, []);
        var plain = State([b], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(plain, new ModelsMoved([.. b.Models.Take(2).Select(m => new ModelRelocation(m.Id, plain.Projection.Models[m.Id].Location, new OnShelf()))]));
        Assert.Equal(0, Test(plain, b.Id, "melee", 1).Alive(b.Id));                                              // without it: routed
        var sim = Test(s, a.Id, "melee", 1, 6, 6);
        Assert.Equal(2, sim.Alive(a.Id));                                                                         // NR3: not routed
        Assert.Equal(0, sim.Steps("consequence").Last().Values!["routed"]);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(a.Id).Conditions);
        Assert.Equal(2, Assert.Single(Rolls(sim, GfNames.NoRetreat)).Dice.Count);
    }

    [Fact]
    public void The_dice_are_the_wounds_left_to_destroy_the_unit_tough_and_taken_wounds_counted()
    {
        var a = Unit("A", "4+", "4+", [], 3, [P("No Retreat")]);
        a = a with { Models = [a.Models[0], a.Models[1], a.Models[2] with { Properties = [P("No Retreat"), P("Tough", 3)] }] };
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, new WoundsSet(a.Models[2].Id, 1));
        var sim = Test(s, a.Id, "wounds", 1, 6, 6, 6, 6);
        Assert.Equal(4, Assert.Single(Rolls(sim, GfNames.NoRetreat)).Dice.Count);                                // NR4: 1 + 1 + (3 - 1)
        Assert.Equal(3, sim.Alive(a.Id));
    }

    [Fact]
    public void No_retreat_wounds_roll_no_regeneration()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("No Retreat"), P("Regeneration")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        var sim = Test(s, a.Id, "wounds", 1, 1, 6, 6, 6);
        Assert.Equal(1, sim.Alive(a.Id));                                                                         // NR6
        Assert.Empty(Rolls(sim, "Regeneration"));
    }

    [Fact]
    public void A_passed_test_rolls_no_no_retreat_dice()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("No Retreat")]);
        var sim = Test(State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360), a.Id, "wounds", 5, 1, 1);
        Assert.Empty(Rolls(sim, GfNames.NoRetreat));
        Assert.Equal(2, sim.Alive(a.Id));
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(1, 3, false)]
    public void Only_a_unit_where_most_models_have_it_holds(int holders, int models, bool holds)
    {
        var a = Some("No Retreat", holders, models);                                                              // NR1
        var sim = Test(State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360), a.Id, "wounds", 1, 6, 6, 6);
        Assert.Equal(!holds, sim.Unit(a.Id).Conditions.Contains(GfNames.Shaken));
        Assert.Equal(holds, Rolls(sim, GfNames.NoRetreat).Count == 1);
    }

    [Fact]
    public void An_already_shaken_unit_failing_a_wounds_test_takes_no_no_retreat_wounds()
    {
        var a = Unit("A", "4+", "4+", [], 3, [P("No Retreat")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, Shake(a.Id));
        var sim = Test(s, a.Id, "wounds", 1, 1, 1);
        Assert.Empty(Rolls(sim, GfNames.NoRetreat));
        Assert.Equal(3, sim.Alive(a.Id));
    }

    [Fact]
    public void An_already_shaken_unit_whose_failed_melee_test_would_rout_it_still_holds()
    {
        var a = Unit("A", "4+", "4+", [], 4, [P("No Retreat")]);
        var s = State([a], [Unit("E", "4+", "4+", [], 1, [])]).Under(RuleSets.Gf360);
        Apply(s, Shake(a.Id), new ModelsMoved([.. a.Models.Take(2).Select(m => new ModelRelocation(m.Id, s.Projection.Models[m.Id].Location, new OnShelf()))]));
        var sim = Test(s, a.Id, "melee", 6, 6);                                                                   // a Shaken unit rolls no test die (p.10: it always fails)
        Assert.Equal(2, sim.Alive(a.Id));                                                                         // the rout is caused, so it counts as passed
        Assert.Single(Rolls(sim, GfNames.NoRetreat));
    }
}
