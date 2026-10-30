using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.10: "must compare the number of wounds each unit took during this activation instead. The unit with the highest total loses". 3.5.1 p.10
/// compares the wounds each unit caused. 3.6.0 p.14 Fear(X): "When in melee with this model, enemies count as having taken +X wounds when checking who won."
/// </summary>
public class GfMeleeCompare36Tests
{
    /// <summary>A Dangerous strip centred on (x, y), 4" wide and 40" long: no charge path rounds it.</summary>
    static TerrainPlacement Patch(double x, double y) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(4, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Dangerous, null))]);

    static Destinations BaseToBase(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target)
    {
        var (from, to) = (sim.Model(charger.Models[0].Id).Location, (OnTable)sim.Model(target.Models[0].Id).Location);
        return new([new ModelRelocation(charger.Models[0].Id, from, new OnTable(new Placement(to.Placement.Position with { X = to.Placement.Position.X - 1.3 }, new Yaw(0))))]);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 1, 1, 0)]
    [InlineData(RuleSets.Gf360, 2, 1, 1)]
    public void Wounds_from_the_charge_moves_dangerous_test_count_against_the_charger_under_3_6(string rules, int chargerValue, int defenderValue, int loser)
    {
        // One Tough(3) model each: the dangerous test rolls 3 dice under both editions (3.5.1 the model's Tough, 3.6 the unit's wounds left).
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 3)], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P("Tough", 3)], Melee("Fist", 1));
        // Dangerous 1,4,4 (one wound); charger hits 4, defender blocks 1; strike back hits 4, charger blocks 1; a morale test 6 (passed).
        var sim = Sim.Gf(rules, State([charger], [defender], x1: -8, x2: 0, terrain: [Patch(-4, -20)]), 1, 4, 4, 4, 1, 4, 1, 6);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Id]));
        sim.Answer(Samples.Team1, BaseToBase(sim, charger, defender));
        Assert.Equal(1, sim.Model(charger.Models[0].Id).Wounds);                                     // the dangerous test's wound, before the melee
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var compare = sim.Steps("compare").First();
        // 3.5.1: 1 caused each way, a tie. 3.6: the charger took 2 (1 dangerous, 1 struck back), the defender 1.
        Assert.Equal((chargerValue, defenderValue, loser), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 1, 0)]
    [InlineData(RuleSets.Gf360, 0, 1)]
    public void Fear_adds_to_the_enemys_taken_total_under_3_6(string rules, int chargerValue, int defenderValue)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Fear", 1)], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([charger], [target], x1: -3, x2: 0), 1, 6);                   // the charger misses; the target passes its morale test
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        sim.Answer(Samples.Team2, new Chosen("pass"));
        var compare = sim.Steps("compare").First();
        // 3.5.1: the charger caused 0 + Fear(1). 3.6: the target took 0 + the charger's Fear(1). The target loses either way.
        Assert.Equal((chargerValue, defenderValue, 2), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Contains(sim.Steps("compare").First().Applied ?? [], a => a == "Fear");
    }
    /// <summary>3.6.0 p.14 Fear(X): "When in melee with this model, enemies count as having taken +X wounds when checking who won": no condition that the
    /// Fear model attacked. A defender with Fear(1) and no Counter strikes back; every attack misses. 3.5.1 p.13 adds Fear to the wounds the defender caused.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 0, 1)]
    [InlineData(RuleSets.Gf360, 1, 0)]
    public void A_striking_back_defenders_fear_adds_to_the_chargers_taken_total_3_6_0_p14(string rules, int chargerValue, int defenderValue)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var target = Unit("Target", "4+", "4+", [], 1, [P("Fear", 1)], Melee("Fist", 1));
        var sim = Sim.Gf(rules, State([charger], [target], x1: -3, x2: 0), 1, 1, 6, 6);               // both attacks miss; the charger passes its morale test
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == target.Id);
        var compare = sim.Steps("compare").First();
        Assert.Equal((chargerValue, defenderValue, 1), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Contains(compare.Applied ?? [], a => a == "Fear");
    }
}
