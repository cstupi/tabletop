using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 Hive Bond and Hold the Line: "If most models in this unit have this rule, they get +1 to Morale Test rolls when shot/charged from over 9"/6" away."
/// The morale test's interaction carries the charge's recorded range and the charger's Charge for the unit charged, and the shooter's distance after
/// shooting. 3.5.1's "+1 to morale test rolls triggered by shooting/melee" reads neither.
/// </summary>
public class GfMoraleRange36Tests
{
    static Destinations BaseToBase(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target)
    {
        var (from, to) = (sim.Model(charger.Models[0].Id).Location, (OnTable)sim.Model(target.Models[0].Id).Location);
        return new([new ModelRelocation(charger.Models[0].Id, from, new OnTable(new Placement(to.Placement.Position with { X = to.Placement.Position.X - 1.3 }, new Yaw(0))))]);
    }

    /// <summary>A charge from x1 at a Tough(3) unit carrying the rule at 0: the charger hits (6), the defender fails its block (1) and loses; its morale die is 3.</summary>
    static Sim Charged(string rules, string rule, double x1)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P(rule), P("Tough", 3)]);
        var sim = Sim.Gf(rules, State([charger], [defender], x1: x1, x2: 0), 6, 1, 3);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Id]));
        sim.Answer(Samples.Team1, BaseToBase(sim, charger, defender));
        for (var i = 0; i < 4 && sim.AskForOrNull(Samples.Team2) is { } ask; i++)
            sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("strike"));
        return sim;
    }

    static int MoraleBonus(Sim sim, string rule) =>
        sim.Steps("test").SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Morale).SelectMany(r => r.Modifiers).Where(m => m.Source == rule).Sum(m => m.Amount);

    [Theory]
    [InlineData(RuleSets.Gf351, "Hive Bond", 1, 1)]
    [InlineData(RuleSets.Gf360, "Hive Bond", 1, 0)]
    [InlineData(RuleSets.Gf360, "Hold the Line", 1, 0)]
    public void Charged_from_over_six_inches_gets_plus_one_under_3_6(string rules, string rule, int far, int near)
    {
        Assert.Equal(far, MoraleBonus(Charged(rules, rule, -12), rule));    // 12" between centres: 10.74" edge to edge
        Assert.Equal(near, MoraleBonus(Charged(rules, rule, -6), rule));    // 4.74"
    }

    /// <summary>The charger's own test reads no "charged": a Tough(3) charger that loses is tested with no bonus under 3.6.</summary>
    [Fact]
    public void The_charging_unit_is_not_charged()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Hive Bond"), P("Tough", 3)], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P("Tough", 3)], Melee("Fist", 1));
        // The charger misses (1); the defender strikes back, hits (6), the charger fails its block (1); the charger's morale die 3.
        var sim = Sim.Gf36(State([charger], [defender], x1: -12, x2: 0), 1, 6, 1, 3);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Id]));
        sim.Answer(Samples.Team1, BaseToBase(sim, charger, defender));
        for (var i = 0; i < 4 && sim.AskForOrNull(Samples.Team2) is { } ask; i++)
            sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("strike"));
        Assert.Equal(charger.Id, sim.Emitted.OfType<SequenceStarted>().Single(s => s.Sequence == GfNames.MoraleTest).Context.ActorUnitId);
        Assert.Equal(0, MoraleBonus(sim, "Hive Bond"));
    }

    /// <summary>Shot: the general test after the shooter's activation, the shooter 12" or 6" away (centres).</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, -12, 1)]
    [InlineData(RuleSets.Gf351, -6, 1)]
    [InlineData(RuleSets.Gf360, -12, 1)]
    [InlineData(RuleSets.Gf360, -6, 0)]
    public void Shot_from_over_nine_inches_gets_plus_one_under_3_6(string rules, double x1, int bonus)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [P("Hold the Line"), P("Tough", 3)]);
        var sim = Sim.Gf(rules, State([shooter], [target], x1: x1, x2: 0), 6, 1, 3);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        sim.Start(GfNames.MoraleTest, new SequenceContext(target.Id, [], [], "wounds"));
        Assert.Equal(bonus, MoraleBonus(sim, "Hold the Line"));
    }
}
