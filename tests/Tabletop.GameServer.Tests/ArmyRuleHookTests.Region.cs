using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    /// <summary>The move Ask shows the cap ApplyMove enforces: Strider and Flying are not held to the difficult-terrain cap (final review M5).</summary>
    [Theory]
    [InlineData("Strider", true)]
    [InlineData("Flying", true)]
    [InlineData(null, false)]
    public void A_hit_and_run_move_Ask_carries_the_difficult_cap_the_move_will_be_held_to(string? rule, bool exempt)
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var defender = Unit("D", "4+", "4+", [P("Hit & Run Fighter"), .. rule is null ? Array.Empty<Tabletop.Content.PropertyInstance>() : [P(rule)]], 1, [], Melee("Spear", 1));
        var sim = MeleeSim(charger, defender);
        sim.Answer(Samples.Team2, new Chosen($"Hit & Run Fighter|{defender.Id:N}"));
        Assert.Equal(exempt ? null : GfDifficultCap, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).DifficultCap);
    }

    /// <summary>p.13 Flying "may move through units and terrain", and p.11 lets a rule that ignores impassable terrain move through it: a Flying unit's
    /// Hit & Run move lists no impassable volume to sweep. Flying is read from the live set.</summary>
    [Theory]
    [InlineData("Flying", true)]
    [InlineData(null, false)]
    public void A_hit_and_run_move_Ask_lists_impassable_terrain_unless_the_unit_flies(string? rule, bool flies)
    {
        var wall = new Tabletop.Content.TerrainPlacement(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("wall"), new Tabletop.Geometry.Point3(40, 40, 0), new Tabletop.Geometry.Yaw(0), 1,
            [new Tabletop.Content.TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Solid, Tabletop.Geometry.Footprint.Rect(1, 10), new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Yaw(0), 0, 4, Tabletop.Geometry.TerrainDeclarations.Impassable, null))]);
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var defender = Unit("D", "4+", "4+", [P("Hit & Run Fighter"), .. rule is null ? Array.Empty<Tabletop.Content.PropertyInstance>() : [P(rule)]], 1, [], Melee("Spear", 1));
        var sim = MeleeSim(charger, defender, terrain: [wall]);
        sim.Answer(Samples.Team2, new Chosen($"Hit & Run Fighter|{defender.Id:N}"));
        var listed = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).ImpassableVolumeIds;
        if (flies) Assert.Empty(listed); else Assert.Equal([wall.Volumes[0].Id], listed);
    }

    static double? GfDifficultCap => Tabletop.GameServer.Rules.GrimdarkFuture.GfNames.DifficultCap;
}
