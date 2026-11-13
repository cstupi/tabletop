using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause by clause, 3.6.0 Hero, Unique Hero, Tough(X), Fear(X) (pp.14–15) and Reanimation (army-rules.part.json ruleText).
///
/// Hero: "Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero, but must use the unit’s Defense until all other models
/// have been killed. The Hero’s unit, and units within 6” of friendly Heroes with Tough(9) or higher, may use the Hero’s Quality for Morale Tests as long as
/// the Hero isn’t Shaken." Note: the unit's size is increased by 1; the Hero as last model takes Morale Tests as the unit; it never leaves or joins.
/// Example: alone after the others die, a failed melee test Routs it (half or less of the starting size).
///   H2 the unit's Defense until alone, then its own; H3 its unit tests on its Quality (proven by ArmyRules36Tests, GfRollsTests);
///   H4 within 6", Tough(9)+, friendly, not Shaken (proven by GfMoraleAura36Tests); H5 counts in the size: alone, a failed melee test routs it,
///   a failed wounds test shakes it.
/// Unique Hero: "Counts as having Hero, and this model may only be taken once per army." (counts as Hero: proven by ArmyRules36Tests; here its Defense.)
/// Tough(X): "This model must take X wounds before being killed. If a model with Tough joins a unit without it, then it is removed last when the unit takes
/// wounds. You must continue to put wounds on the Tough model with most wounds in the unit until it is killed, before starting to put them on the next Tough
/// model (Heroes must be assigned wounds last, even if already wounded)." (each clause proven by GfDealWoundsTests; here all four in one 3.6 unit)
/// Fear(X): "When in melee with this model, enemies count as having taken +X wounds when checking who won." (both sides proven by GfMeleeCompare36Tests;
///   here X per model, a joined Hero's own, and no real wound.)
/// Reanimation: "When a unit where all models have this rule is activated, roll as many dice as the max. number of models/wounds it could restore. For each
/// 5+ you may restore one model/wound. Note that new models may only be restored if they can be placed in coherency with non-restored models."
///   RA1 all models (a joined Hero without it stops it); RA2 the dice: killed models plus wounds carried; RA3 each 5+ one, a 4 none; RA4 "may";
///   RA5 a restored model stands in coherency (within 1") with a model in play.
/// </summary>
public class ClausesActivateHeroToughTests
{
    static readonly PropertySet Core = TestProperties.Core36;

    static void Apply(Sim sim, params EventPayload[] events) => sim.Append(events);
    static ModelRelocation Shelve(Sim sim, Guid model) => new(model, sim.Model(model).Location, new OnShelf());
    static SequenceContext Shot(Unit a, Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    // ---- Hero, Unique Hero ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Hero")]
    [InlineData("Unique Hero")]
    public void A_joined_hero_blocks_on_the_units_defense_until_it_is_alone(string rule)
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P(rule)] }, squad.Models[1]] };
        var shooters = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var full = Sim.Gf36(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        full.Start(GfNames.AttackSequence, Shot(shooters, joined));
        Assert.Equal(5, full.Steps("roll-to-block").Single().Rolls.Single().Threshold);                         // H2: the unit's
        Assert.Equal(1, full.Alive(joined.Id));
        Assert.IsType<OnShelf>(full.Model(joined.Models[1].Id).Location);                                        // and the Hero takes the wound last

        var alone = Sim.Gf36(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        Apply(alone, new ModelsMoved([Shelve(alone, joined.Models[1].Id)]));
        alone.Start(GfNames.AttackSequence, Shot(shooters, joined));
        Assert.Equal(2, alone.Steps("roll-to-block").Single().Rolls.Single().Threshold);                        // H2: its own once alone
    }

    static Unit HeroInFive(int tough)
    {
        var squad = Unit("Squad", "5+", "5+", [], 6, []);
        return squad with { Models = [.. squad.Models.Take(5), squad.Models[5] with { Stats = new Dictionary<string, string> { ["Quality"] = "4+" }, Properties = [P("Hero"), P("Tough", tough)] }] };
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void A_hero_left_alone_in_its_unit_routs_on_a_failed_melee_test_and_is_shaken_by_a_failed_wounds_test(int tough)
    {
        var unit = HeroInFive(tough);
        foreach (var (cause, routed) in new[] { ("melee", true), ("wounds", false) })
        {
            var sim = Sim.Gf36(State([unit], [Unit("E", "4+", "4+", [], 1, [])]), 1);
            Apply(sim, new ModelsMoved([.. unit.Models.Take(5).Select(m => Shelve(sim, m.Id))]));             // H5: one of a starting six
            sim.Start(GfNames.MoraleTest, new SequenceContext(unit.Id, [], [], cause));
            Assert.Equal(4, sim.Steps("test").Single().Rolls.Single().Threshold);                               // H3: it tests as the unit, on its Quality
            Assert.Equal(routed ? 0 : 1, sim.Alive(unit.Id));
            Assert.Equal(!routed, sim.Unit(unit.Id).Conditions.Contains(GfNames.Shaken));
        }
    }

    // ---- Tough ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Wounds_go_on_plain_models_then_the_most_wounded_tough_model_and_a_hero_last_even_when_wounded()
    {
        var t = Unit("T", "4+", "4+", [], 4, []);
        t = t with { Models = [t.Models[0], t.Models[1] with { Properties = [P("Tough", 3)] }, t.Models[2] with { Properties = [P("Tough", 3)] },
            t.Models[3] with { Properties = [P("Hero"), P("Tough", 3)] }] };
        var (plain, x, y, hero) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id, t.Models[3].Id);
        var sim = Sim.Gf36(State([Unit("A", "4+", "4+", [], 1, [])], [t]));
        Apply(sim, new WoundsSet(hero, 2), new WoundsSet(y, 1));
        Assert.Equal([plain], GfAttack.EligibleForNextWound(sim.State, Core, t.Id));                            // removed last
        Apply(sim, new ModelsMoved([Shelve(sim, plain)]));
        Assert.Equal([y], GfAttack.EligibleForNextWound(sim.State, Core, t.Id));                                // the most wounded Tough model, not the hero's 2
        Apply(sim, new WoundsSet(y, 2));
        Assert.IsType<OnTable>(sim.Model(y).Location);                                                          // X wounds before being killed
        Assert.Equal([y], GfAttack.EligibleForNextWound(sim.State, Core, t.Id));                                // until it is killed
        Apply(sim, new ModelsMoved([Shelve(sim, y)]));
        Assert.Equal([x], GfAttack.EligibleForNextWound(sim.State, Core, t.Id));                                // the hero, wounded, still last
        Apply(sim, new ModelsMoved([Shelve(sim, x)]));
        Assert.Equal([hero], GfAttack.EligibleForNextWound(sim.State, Core, t.Id));
    }

    // ---- Fear --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The charger's melee, every attack missing and the target passing its test: the target's counted total and its real wounds.</summary>
    static (int Counted, int Taken) Feared(Unit charger)
    {
        var target = Unit("Target", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var sim = Sim.Gf36(State([charger], [target], x1: -3, x2: 0), [.. Enumerable.Repeat(1, charger.Models.Count), 6]);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        sim.Answer(Samples.Team2, new Chosen("pass"));
        return (sim.Steps("compare").First().Values!["defender_wounds"], sim.Unit(target.Id).WoundsThisActivation);
    }

    [Fact]
    public void Fear_counts_x_per_model_holding_it_a_joined_heros_included_and_deals_no_wound()
    {
        Assert.Equal((2, 0), Feared(Unit("Two", "4+", "4+", [], 2, [P("Fear", 1)], Melee("Claw", 1))));           // (X) rules stack, no real wound
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Melee("Claw", 1));
        var joined = squad with { Models = [.. squad.Models.Take(2), squad.Models[2] with { Properties = [P("Hero"), P("Fear", 2)] }] };
        Assert.Equal((2, 0), Feared(joined));
        Assert.Equal((0, 0), Feared(squad));
    }

    // ---- Reanimation -------------------------------------------------------------------------------------------------------------------

    static Sim Activate(Unit u, int[] dice, Action<Sim> setup)
    {
        var sim = Sim.Gf36(State([u], [Unit("E", "4+", "4+", [], 1, [])], x1: -20, x2: 20), dice);
        setup(sim);
        sim.Append([new ActionRecorded(u.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        return sim;
    }

    static RollRecord? Reanimation(Sim sim) => sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).SingleOrDefault(r => r.Roll == "Reanimation");

    static Unit Reanimators() => Unit("R", "4+", "4+", [], 3, [P("Reanimation")]) is var u
        ? u with { Models = [u.Models[0], u.Models[1], u.Models[2] with { Properties = [P("Reanimation"), P("Tough", 3)] }] } : u;

    [Fact]
    public void Reanimation_rolls_one_die_per_killed_model_and_carried_wound_and_restores_one_per_5_plus()
    {
        var u = Reanimators();
        var sim = Activate(u, [5, 4, 5], s => s.Append([new ModelsMoved([Shelve(s, u.Models[0].Id)]), new WoundsSet(u.Models[2].Id, 2)]));
        Assert.Equal("Reanimation: restore 1 model and 1 wound?", sim.AskFor(Samples.Team1).Prompt);
        sim.Answer(Samples.Team1, new Chosen("restore"));
        var roll = Reanimation(sim)!;
        Assert.Equal((3, 5), (roll.Dice.Count, roll.Threshold));                                                // RA2: one killed model + two wounds
        Assert.IsType<OnTable>(sim.Model(u.Models[0].Id).Location);                                            // RA3: the first 5 restores the model
        Assert.Equal(1, sim.Model(u.Models[2].Id).Wounds);                                                      // the second 5 a wound; the 4 nothing
        var gap = Tabletop.Geometry.Distance.EdgeToEdge(Battlefield.Placed(sim.State, u.Models[0].Id)!.Value,
            new[] { u.Models[1].Id, u.Models[2].Id }.Select(m => Battlefield.Placed(sim.State, m)!.Value).MinBy(p => Tabletop.Geometry.Distance.EdgeToEdge(Battlefield.Placed(sim.State, u.Models[0].Id)!.Value, p)));
        Assert.InRange(gap, 0.01, 1.0);                                                                          // RA5: in coherency with the models in play
    }

    [Fact]
    public void Reanimation_needs_all_models_and_rolls_nothing_with_nothing_to_restore()
    {
        var u = Reanimators();
        var joined = u with { Models = [u.Models[0], u.Models[1], u.Models[2] with { Properties = [P("Hero"), P("Tough", 3)] }] };
        Assert.Null(Reanimation(Activate(joined, [5, 5], s => s.Append([new ModelsMoved([Shelve(s, joined.Models[0].Id)])]))));   // RA1
        Assert.Null(Reanimation(Activate(u, [5], _ => { })));                                                   // RA2: zero dice
    }

    [Fact]
    public void Reanimation_asks_before_restoring()
    {
        var u = Reanimators();
        var sim = Activate(u, [5], s => s.Append([new ModelsMoved([Shelve(s, u.Models[0].Id)])]));
        Assert.IsType<OnShelf>(sim.Model(u.Models[0].Id).Location);                                             // RA4 "may": nothing yet
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfReanimation.Question, ask.Question);
        Assert.True(ask.AllowPass);
        sim.Answer(Samples.Team1, new Passed());                                                                  // a pass is not restoring
        Assert.IsType<OnShelf>(sim.Model(u.Models[0].Id).Location);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
    }
}
