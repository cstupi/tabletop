using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.9: "After a melee where they attacked, units hit on unmodified rolls of 6 in melee until the end of that round, and those hits
/// don’t count as rolls of 6 for special rules." 3.5.1 p.9 has no second clause, so a fatigued 6 still sets off its rules there.
/// </summary>
public class GfFatigue36Tests
{
    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Theory]
    [InlineData(RuleSets.Gf351, true)]
    [InlineData(RuleSets.Gf360, false)]
    public void A_fatigued_rending_six_gives_no_ap_under_3_6(string rules, bool rends)
    {
        var charger = Unit("Charger", "4+", "2+", [], 1, [], Melee("Sword", 1));
        var tired = Unit("Tired", "4+", "4+", [], 1, [], Melee("Saw", 1, P("Rending")));
        var sim = Sim.Gf(rules, State([charger], [tired], x1: -3, x2: 0), 1, 6, 5);                    // charger misses; strike-back 6; block 5
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, tired.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, tired));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var six = sim.Steps("roll-to-hit").Last().Rolls.Single().Dice.Single();
        Assert.True(six.Success);                                                                       // the hit still lands
        Assert.Equal(rends, six.Triggered?.Contains("Rending") == true);
        var block = sim.Steps("roll-to-block").Last().Rolls.Single();
        Assert.Equal(rends, block.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4));
        Assert.Equal(!rends, block.Dice.Single().Success);                                              // 5 against 2+, or 5 − 4 = 1
    }

    /// <summary>p.10 (both editions): Shaken units "may strike back counting as Fatigued", so the 3.6 clause reaches a Shaken strike-back.
    /// Brutal Fighter ("When in melee, unmodified results of 6 to hit deal 1 extra hit") rather than Furious, which acts only when charging.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 1)]
    [InlineData(RuleSets.Gf360, 0)]
    public void A_shaken_unit_striking_back_counts_as_fatigued_so_its_brutal_fighter_six_adds_no_hit_under_3_6(string rules, int extra)
    {
        var charger = Unit("Charger", "4+", "6+", [], 1, [], Melee("Sword", 1));
        var shaken = Unit("Shaken", "4+", "4+", [], 1, [P("Brutal Fighter")], Melee("Club", 1));
        var sim = Sim.Gf(rules, State([charger], [shaken], x1: -3, x2: 0), 1, 6, 1, 1);                // charger misses; strike-back 6; blocks 1, 1
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, shaken.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, shaken));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.True(sim.Steps("roll-to-hit").Last().Rolls.Single().Dice.Single().Success);
        Assert.Equal(extra, sim.Steps("score-hits").Last().Values!["extra_hits"]);
    }

    /// <summary>Predator Fighter: "For each unmodified roll of 6 to hit in melee, this model may roll +1 attack with that weapon."</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 2)]
    [InlineData(RuleSets.Gf360, 1)]
    public void A_fatigued_predator_fighter_six_rolls_no_extra_attack_under_3_6(string rules, int pools)
    {
        var charger = Unit("Charger", "4+", "6+", [], 1, [], Melee("Sword", 1));
        var tired = Unit("Tired", "4+", "4+", [], 1, [P("Predator Fighter")], Melee("Claw", 1));
        var sim = Sim.Gf(rules, State([charger], [tired], x1: -3, x2: 0), 1, 6, 1, 1, 1);              // charger misses; strike-back 6; then the added die or blocks
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, tired.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, tired));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Equal(pools, sim.Steps("roll-to-hit").Last().Rolls.Count);
    }

    /// <summary>3.6 Hazardous: "on unmodified results of 1 to hit this model takes one wound". A 1 is no hit, so fatigue's clause leaves it.</summary>
    [Fact]
    public void A_fatigued_hazardous_one_still_wounds_its_own_unit_under_3_6()
    {
        var charger = Unit("Charger", "4+", "6+", [], 1, [], Melee("Sword", 1));
        var tired = Unit("Tired", "4+", "4+", [], 1, [P("Tough", 3)], Melee("Drill", 1, P("Hazardous")));
        var sim = Sim.Gf36(State([charger], [tired], x1: -3, x2: 0), 1, 1, 1, 1);                      // charger misses; strike-back 1
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, tired.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, tired));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var roll = sim.Steps("roll-to-hit").Last().Rolls.Single();
        Assert.True(roll.HitsTriggerNothing);
        Assert.Equal(["Hazardous +1 wound"], roll.Dice.Single().Triggered!);
        Assert.Equal(1, sim.Steps("score-hits").Last().Values!["self_wounds"]);
    }
}
