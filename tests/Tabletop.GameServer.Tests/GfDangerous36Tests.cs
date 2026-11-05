using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.12: "If any model in a unit moves into or through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous
/// Terrain Test. To take a Dangerous Terrain Test, roll as many dice as the number of wounds it would take to fully destroy the unit, and for each
/// result is 1 the unit takes one wound." 3.5.1 p.11 tests each affected model on its Tough value in dice.
/// </summary>
public class GfDangerous36Tests
{
    /// <summary>A Dangerous patch centred on (x, y); Armies.State stands the first unit's models at y = -20 + 1.5 × ordinal.</summary>
    static TerrainPlacement Patch(double x, double y, double width = 4, double length = 4) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Dangerous, null))]);

    static Destinations By(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    static readonly int[] Fours = Enumerable.Repeat(4, 40).ToArray();

    /// <summary>A Rush of 12" straight on, which no path round the patch fits: model 1 (y -18.5) crosses the patch at x -8..-4, the others pass beside it.</summary>
    static Sim Rushing(string rules, Tabletop.Content.Unit unit, IReadOnlyList<TerrainPlacement> terrain, Action<Sim>? before = null)
    {
        var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: terrain), Fours);
        before?.Invoke(sim);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, By(sim, unit, 12));
        return sim;
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 1, 3)]
    [InlineData(RuleSets.Gf360, 5, 7)]
    public void One_model_crossing_dangerous_ground_rolls_the_whole_units_remaining_wounds_under_3_6(string rules, int fiveSingles, int threeToughWounded)
    {
        var five = Unit("A", "4+", "4+", [], 5, []);
        var sim = Rushing(rules, five, [Patch(-6, -20)]);
        Assert.Equal(fiveSingles, sim.Steps("test").Single().Rolls.Single().Dice.Count);

        // Three Tough(3) models, the first (the one that crosses) with 2 wounds taken: 1 + 3 + 3 wounds left to destroy the unit.
        var tough = Unit("A", "4+", "4+", [], 3, [P("Tough", 3)]);
        sim = Rushing(rules, tough, [Patch(-6, -20)], s => s.Append([new WoundsSet(tough.Models[0].Id, 2)]));
        Assert.Equal(threeToughWounded, sim.Steps("test").Single().Rolls.Single().Dice.Count);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, new[] { 1, 1 })]
    [InlineData(RuleSets.Gf360, new[] { 5 })]
    public void The_unit_tests_once_per_activation_under_3_6(string rules, int[] dice)
    {
        // Model 1 is activated in a patch at x -12; model 3 (y -15.5) crosses a 1" patch at x -4 on the move. No other model touches either.
        var unit = Unit("A", "4+", "4+", [], 5, []);
        var sim = Rushing(rules, unit, [Patch(-12, -20), Patch(-4, -15.5, 1, 1)]);
        Assert.Equal(dice, sim.Steps("test").Select(s => s.Rolls.Single().Dice.Count).ToArray());
        Assert.All(sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence.StartsWith(GfNames.DangerousTerrain)),
            s => Assert.Equal(rules == RuleSets.Gf360 ? GfNames.DangerousTerrain36 : GfNames.DangerousTerrain, s.Sequence));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 4)]
    [InlineData(RuleSets.Gf360, 3)]
    public void A_destroyed_transports_passengers_test_as_a_unit_under_3_6(string rules, int dice)
    {
        // 3.6.0 p.15: "When a transport is destroyed, units inside must take a Dangerous Terrain Test". Two Tough(2) riders, one with a wound taken.
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 2, [P("Tough", 2)]);
        var container = bus.Models[0].Id;
        var sim = Sim.Gf(rules, State([Unit("A", "4+", "4+", [], 1, [])], [bus, riders], x2: 20), Fours);
        sim.Append([new ModelsMoved([.. riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container)))]),
            new WoundsSet(riders.Models[0].Id, 1)]);
        sim.Start(GfNames.TransportDestroyedSeq, new SequenceContext(riders.Id, [], [], null));
        Assert.Equal(dice, sim.Steps("test").Single().Rolls.Single().Dice.Count);
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == (rules == RuleSets.Gf360 ? GfNames.DangerousTerrain36 : GfNames.DangerousTerrain));
    }
}
