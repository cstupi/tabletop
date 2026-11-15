using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// The unit card's cover shield (ModelFacts.InCover): p.11 / 3.6.0 p.12 "If the majority of models in a unit are fully inside a piece of
/// Cover terrain ... (for multi-model units), or that are mostly inside Cover terrain ... (for single-model units)", the terrain half
/// alone, which needs no shooter. The wood is a 3"×3" Cover area centred on (10, -6); every base has radius 0.63.
/// </summary>
public class InCoverFactsTests
{
    static readonly Point3 Wood = new(10, -6, 0);

    static TerrainPlacement Piece(TerrainDeclarations declarations) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), Wood, new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, declarations, null))]);

    static (IReadOnlyList<Guid> InCover, Unit Unit) Laid(string rules, int models, Point3[] at, TerrainDeclarations declarations = TerrainDeclarations.Cover)
    {
        var (sim, unit) = Placed(rules, models, at, declarations);
        var gf = rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.Instance;
        return (((IGameRules)gf).InCover(sim.State), unit);
    }

    static (Sim Sim, Unit Unit) Placed(string rules, int models, Point3[] at, TerrainDeclarations declarations = TerrainDeclarations.Cover)
    {
        var unit = Unit("Squad", "4+", "4+", [], models, []);
        var sim = Sim.Gf(rules, State([Unit("Other", "4+", "4+", [], 1, [])], [unit], terrain: [Piece(declarations)]));
        sim.Append([new ModelsMoved(unit.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(new Placement(at[i], new Yaw(0))))).ToList())]);
        return (sim, unit);
    }

    static Point3 In(double dx) => Wood with { X = Wood.X + dx };
    static readonly Point3 Out1 = new(20, 6, 0), Out2 = new(22, 6, 0);

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Two_of_three_models_fully_inside_cover_terrain_put_the_unit_in_cover(string rules)
    {
        var (inCover, unit) = Laid(rules, 3, [In(-0.7), In(0.7), Out1]);
        Assert.Contains(unit.Id, inCover);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void One_of_three_inside_is_no_majority(string rules)
    {
        var (inCover, unit) = Laid(rules, 3, [In(0), Out1, Out2]);
        Assert.DoesNotContain(unit.Id, inCover);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Half_of_a_multi_model_unit_is_no_majority(string rules)
    {
        var (inCover, unit) = Laid(rules, 2, [In(0), Out1]);
        Assert.DoesNotContain(unit.Id, inCover);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_model_of_a_multi_model_unit_straddling_the_edge_is_not_fully_inside(string rules)
    {
        var (inCover, unit) = Laid(rules, 3, [In(1.3), In(-1.3), Out1]);   // centres 0.2" inside the 1.5" half-width: rims stick out
        Assert.DoesNotContain(unit.Id, inCover);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_single_model_mostly_inside_is_in_cover(string rules)
    {
        var (inCover, unit) = Laid(rules, 1, [In(1.3)]);
        Assert.Contains(unit.Id, inCover);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Terrain_declared_only_see_in_and_out_or_dense_gives_no_cover(string rules)
    {
        var (inCover, unit) = Laid(rules, 1, [In(0)], TerrainDeclarations.SeeInAndOut | TerrainDeclarations.Difficult);
        Assert.DoesNotContain(unit.Id, inCover);
    }

    /// <summary>A 3.6 Ruins piece (Cover, Unstable, Sight blocking, Dense, 0–4"), 4"×4", and a lone Flying Tough(6) model on a 60 mm base
    /// whose centre is inside and whose rim sticks out: on the ground of an area, or resting on a solid's top.</summary>
    [Theory]
    [InlineData(VolumeKind.Area, 0.0)]
    [InlineData(VolumeKind.Solid, 4.0)]
    public void A_lone_large_flying_model_in_ruins_is_in_cover(VolumeKind kind, double z)
    {
        var ruins = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), Wood, new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, Footprint.Rect(4, 4), new Point2(0, 0), new Yaw(0), 0, 4,
                TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut, null))]);
        var horror = Unit("Horror", "4+", "3+", [], 1, [P("Flying"), P("Tough", 6)]);
        horror = horror with { Models = [horror.Models[0] with { Base = Base.Circle(2.36), Height = 3.54 }] };
        var sim = Sim.Gf36(State([Unit("Other", "4+", "4+", [], 1, [])], [horror], terrain: [ruins]));
        sim.Append([new ModelsMoved([new ModelRelocation(horror.Models[0].Id, sim.Model(horror.Models[0].Id).Location,
            new OnTable(new Placement(new Point3(Wood.X + 1.2, Wood.Y, z), new Yaw(0))))])]);
        Assert.Contains(horror.Id, ((IGameRules)GrimdarkFutureRules.V360).InCover(sim.State));
    }

    [Fact]
    public void Wrapping_rulesets_forward_it()
    {
        var (sim, unit) = Placed(RuleSets.Gf351, 1, [In(0)]);
        Assert.Equal([unit.Id], new WithSequences(GrimdarkFutureRules.Instance).InCover(sim.State));
        Assert.Equal([unit.Id], FakeRules.Gf().InCover(sim.State));
        Assert.Empty(((IGameRules)new StubRules()).InCover(sim.State));
    }
}
