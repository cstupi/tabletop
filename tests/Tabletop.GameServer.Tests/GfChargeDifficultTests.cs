using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Charge is offered only where a charge move can reach base contact under the difficult cap. 3.5.1 p.11, 3.6.0 p.12: "If any model in a unit moves in
/// or through Difficult terrain at any point of its move, then all models in the unit may not move more than 6” for that movement." p.9: a target is
/// valid when a model "is within charge distance of one model from the target unit, and has a clear path to reach it". p.14 (3.6.0 p.15) Strider
/// "May ignore the effects of Difficult terrain"; Flying "ignores the effects of Difficult and Dangerous terrain".
/// </summary>
public class GfChargeDifficultTests
{
    /// <summary>A wood 8" square about (14, 24).</summary>
    static TerrainPlacement Wood() => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("forest"), new Point3(14, 24, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(8, 8), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Difficult, null))]);

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    /// <summary>A one-model charger standing in the wood at (14, 24) and a one-model target on open ground at `targetX`.</summary>
    static (Sim Sim, Unit Charger, Unit Target) Scene(string rules, double targetX, params string[] charger)
    {
        var c = Unit("C", "4+", "4+", [], 1, charger.Select(n => P(n)).ToArray(), Melee("Fist", 1));
        var t = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([c], [t], enforcement: Enforcement.Strict, terrain: [Wood()]), 1);
        Place(sim, c.Models[0].Id, 14, 24); Place(sim, t.Models[0].Id, targetX, 24);
        return (sim, c, t);
    }

    static Properties.PropertySet Props(string rules) => rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;

    static (IReadOnlyList<string> Actions, string? Note) Choices(Sim sim, string rules, Unit unit) =>
        GrimdarkFutureRules.ActivationChoices(sim.State, Props(rules), sim.State.Projection.Units[unit.Id], ["Hold", "Advance", "Rush", "Charge"]);

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_unit_in_difficult_terrain_is_not_offered_Charge_at_an_enemy_past_the_cap(string rules)
    {
        var (sim, charger, target) = Scene(rules, 25);   // 9.7" to contact: within 12", over 6"
        Assert.DoesNotContain(target.Id, GfMelee.ChargeTargets(sim.State, Props(rules), charger.Id));
        var (actions, note) = Choices(sim, rules, charger);
        Assert.DoesNotContain("Charge", actions);
        Assert.Equal(GrimdarkFutureRules.DifficultNoChargeNote, note);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_unit_with_one_model_in_difficult_terrain_is_held_to_the_cap_though_another_stands_clear(string rules)
    {
        // One model in the wood at (14, 24), the other on open ground at (19.5, 24), 7.2" from contact with the target at (28, 24).
        var c = Unit("C", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var t = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([c], [t], enforcement: Enforcement.Strict, terrain: [Wood()]), 1);
        Place(sim, c.Models[0].Id, 14, 24); Place(sim, c.Models[1].Id, 19.5, 24); Place(sim, t.Models[0].Id, 28, 24);
        Assert.DoesNotContain(t.Id, GfMelee.ChargeTargets(sim.State, Props(rules), c.Id));
        Assert.Equal(GrimdarkFutureRules.DifficultNoChargeNote, Choices(sim, rules, c).Note);
        Place(sim, c.Models[0].Id, 18.8, 22.5);   // out of the wood, beside its fellow
        Assert.Contains(t.Id, GfMelee.ChargeTargets(sim.State, Props(rules), c.Id));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_fast_unit_in_difficult_terrain_is_held_to_the_cap_too(string rules)
    {
        var (sim, charger, target) = Scene(rules, 25, "Fast");
        Assert.DoesNotContain(target.Id, GfMelee.ChargeTargets(sim.State, Props(rules), charger.Id));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_unit_in_difficult_terrain_is_offered_Charge_at_an_enemy_within_the_cap(string rules)
    {
        var (sim, charger, target) = Scene(rules, 20);
        Assert.Contains(target.Id, GfMelee.ChargeTargets(sim.State, Props(rules), charger.Id));
        Assert.Contains("Charge", Choices(sim, rules, charger).Actions);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Strider")]
    [InlineData(RuleSets.Gf360, "Strider")]
    [InlineData(RuleSets.Gf351, "Flying")]
    [InlineData(RuleSets.Gf360, "Flying")]
    public void A_unit_that_ignores_difficult_terrain_is_offered_Charge_past_the_cap(string rules, string rule)
    {
        var (sim, charger, target) = Scene(rules, 25, rule);
        Assert.Contains(target.Id, GfMelee.ChargeTargets(sim.State, Props(rules), charger.Id));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void An_enemy_out_of_charge_distance_keeps_the_plain_note(string rules)
    {
        var (sim, charger, _) = Scene(rules, 40);
        Assert.Equal(GrimdarkFutureRules.NoChargeNote, Choices(sim, rules, charger).Note);
    }
}
