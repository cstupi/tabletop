using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Who can strike, 3.5.1 p.9: "within 2” horizontally and 4” vertically"; 3.6.0 p.9: "All models that are within 3” of an enemy model
/// from the target unit (horizontally or vertically), may attack it."
/// </summary>
public class GfMelee36Tests
{
    static ModelRelocation To(Sim sim, Guid model, double x, double y, double z = 0) => new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, z), new Yaw(0))));

    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    /// <summary>A two-model charger strikes one defender model: the first charger model in base contact, the second where the test puts it.</summary>
    static int EligibleStrikers(string rules, double x, double y, double z)
    {
        var chargers = Unit("Chargers", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var defender = Unit("Defender", "4+", "6+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([chargers], [defender], x1: -3, x2: 0), 1, 1, 1, 1);
        sim.Append([new ModelsMoved([To(sim, defender.Models[0].Id, 0, -10), To(sim, chargers.Models[0].Id, -1.3, -10), To(sim, chargers.Models[1].Id, x, -10 + y, z)])]);
        sim.Start(GfNames.Melee, MeleeOf(chargers, defender));
        return sim.Steps("determine-attacks").First().Values!["eligible_models"];
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 1)]
    [InlineData(RuleSets.Gf360, 2)]
    public void A_model_two_and_a_half_inches_back_strikes_under_3_6_not_3_5_1(string rules, int strikers) =>
        Assert.Equal(strikers, EligibleStrikers(rules, -1.26 - 2.5, 0, 0));                            // the second rank, 2.5" edge to edge on the table

    [Theory]
    [InlineData(RuleSets.Gf351, 2)]
    [InlineData(RuleSets.Gf360, 1)]
    public void A_model_three_and_a_half_inches_above_strikes_under_3_5_1_not_3_6(string rules, int strikers) =>
        Assert.Equal(strikers, EligibleStrikers(rules, -1.3, 1.3, 3.5));                               // beside the first, within 1" across, its base 3.5" up

    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void A_charger_stopping_two_and_a_half_inches_short_fights_under_3_6_not_3_5_1(string rules, bool fights)
    {
        // Two chargers side by side stop together: drawing either into contact would leave the other out of coherency, so neither is (warn applies it).
        var charger = Unit("C", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([charger], [target], x1: -8, x2: 0), 1);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        var to = ((OnTable)sim.Model(target.Models[0].Id).Location).Placement.Position;
        var at = to with { X = to.X - 1.26 - 2.5 };                                                    // 2.5" short, edge to edge
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(charger.Models[0].Id, sim.Model(charger.Models[0].Id).Location, new OnTable(new Placement(at, new Yaw(0)))),
            new ModelRelocation(charger.Models[1].Id, sim.Model(charger.Models[1].Id).Location, new OnTable(new Placement(at with { Y = at.Y + 1.3 }, new Yaw(0))))]));
        sim.Answer(Samples.Team2, new Passed());
        Assert.Equal(fights, sim.Emitted.OfType<SequenceStarted>().Any(s => s.Sequence == GfNames.Melee));
    }
    /// <summary>3.5.1 p.13 Counter "strikes first", and Impact loses one die per Counter model; 3.6.0 p.14 Impact(X): "When charging, before either unit
    /// attacks, roll X dice". Every die is a 6, so the order is the only difference: two Impact(2) riders against one Counter pike.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 3, false)]
    [InlineData(RuleSets.Gf360, 4, true)]
    public void Impact_rolls_before_counter_and_loses_no_dice_to_counter_under_3_6(string rules, int impactDice, bool impactFirst)
    {
        var riders = Unit("Riders", "4+", "4+", [], 2, [P("Impact", 2), P("Tough", 2)], Melee("Thrust", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(rules, State([riders], [pikes], x1: -3, x2: 0), 6);
        sim.Start(GfNames.Melee, MeleeOf(riders, pikes));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "counter" }) sim.Answer(Samples.Team2, new Chosen("strike"));   // 3.6.0 p.13: "May strike first"
        Assert.Equal(impactDice, sim.Steps("impact").First().Rolls.Single().Dice.Count);
        var started = sim.Emitted.OfType<SequenceStarted>().ToList();
        var impactHits = started.FindIndex(s => s.Sequence == GfNames.ResolveHits && s.Context.ActorUnitId == riders.Id);
        var counterStrike = started.FindIndex(s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == pikes.Id);
        Assert.True(impactHits >= 0 && counterStrike >= 0);
        Assert.Equal(impactFirst, impactHits < counterStrike);
    }

    /// <summary>3.6.0 p.13 Counter: "May strike first with this weapon when charged, unless this unit is Fatigued." The defender's team chooses; 3.5.1 p.13
    /// "Strikes first" asks nothing. Every die misses, so the Asks and the strike order are the only difference.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void Counter_asks_the_defender_under_3_6_not_3_5_1(string rules, bool asks)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(rules, State([charger], [pikes], x1: -3, x2: 0), 1, 1);
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        Assert.Equal(asks, sim.AskForOrNull(Samples.Team2)?.Question == "counter");
    }

    [Fact]
    public void Counter_asks_the_defender_and_a_pass_strikes_with_the_rest_under_3_6()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")), Melee("Dagger", 1));
        var sim = Sim.Gf36(State([charger], [pikes], x1: -3, x2: 0), 1, 1, 1);                       // charger misses; pike and dagger miss
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal("counter", ask.Question);
        Assert.Equal(["strike", "pass"], Assert.IsType<Options>(ask.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team2, new Chosen("pass"));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal([charger.Id, pikes.Id, pikes.Id], attacks.Select(a => a.Context.ActorUnitId!.Value));   // the charger first; the pike and the dagger in the strike back
        Assert.Equal(pikes.Models[0].Weapons.Select(w => w.Id).Order(), attacks.Skip(1).SelectMany(a => a.Context.WeaponIds).Order());
        Assert.Contains(GfNames.Fatigued, sim.Unit(pikes.Id).Conditions);
    }

    [Theory]
    [InlineData(GfNames.Fatigued)]
    [InlineData(GfNames.Shaken)]                                                                       // p.10: Shaken "may strike back counting as Fatigued"
    public void A_fatigued_defender_has_no_counter_under_3_6(string condition)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf36(State([charger], [pikes], x1: -3, x2: 0), 1, 1);                          // charger misses; the pike misses in the strike back
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, pikes.Id), condition, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);                             // no counter Ask: the pike strikes with the rest
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == pikes.Id);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var back = sim.Emitted.OfType<SequenceStarted>().Single(s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == pikes.Id);
        Assert.Equal(pikes.Models[0].Weapons.Select(w => w.Id), back.Context.WeaponIds);
    }

    [Fact]
    public void A_fatigued_defender_still_strikes_first_with_counter_under_3_5_1()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([charger], [pikes], x1: -3, x2: 0), 1, 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, pikes.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, pikes));
        Assert.Equal(pikes.Id, sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence).Context.ActorUnitId);
    }
}
