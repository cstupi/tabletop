using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.AppliedKindsTests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.36 Relic Wargear, Mystic Banners, 3. Banner of Eternal Flame: "The Hero and its unit may ignore all terrain effects." Carried by a model
/// (the Hero) for its whole unit, or granted to a unit from the menu for the game. It ignores difficult terrain's cap (p.11) and the dangerous terrain
/// test, moving through it or activated in it (p.11); it is no leave to move through impassable terrain, which Flying names apart from its
/// effects (p.13 "may move through units and terrain, and ignore terrain effects").
/// </summary>
public class GfEternalFlameTests
{
    static PropertySet Set => TestProperties.Core;

    /// <summary>A 2" x 4" Area patch at (-17, -18.5): unit A's model starts at (-20, -20) and crosses it moving 5" or more along X.</summary>
    static TerrainPlacement Patch(TerrainDeclarations kind) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(-17, -18.5, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 30), new Point2(0, 0), new Yaw(0), 0, 1, kind, null))]);   // long: a path cannot go round it

    static Sim Activated(string sequence, string action, bool banner, TerrainPlacement terrain, double x1 = -20, bool granted = false, Tabletop.Content.Unit? withUnit = null)
    {
        var a = Unit("A", "4+", "4+", [], 2, banner ? [P(GfNames.EternalFlame)] : []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: x1, x2: 20, terrain: [terrain]), GrimdarkFutureRules.Instance, Set, new ScriptedDice(1, 1, 1, 1));
        if (granted) sim.Append([new GrantAdded(a.Id, new Grant(Guid.CreateVersion7(), Set.Entry(GfNames.EternalFlame)!.Effects[0].Id, GfNames.EternalFlame, a.Id, 1))]);
        sim.Start(sequence, new SequenceContext(a.Id, [], [], action));
        return sim;
    }

    static MoveRegion Region(Sim sim) => Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);

    [Fact]
    public void The_banner_is_in_the_catalog_as_its_page_words_it()
    {
        var entry = Set.Entry(GfNames.EternalFlame)!;
        Assert.Equal("The Hero and its unit may ignore all terrain effects.", entry.RuleText);
        Assert.Equal(Aggregation.Any, entry.Aggregation);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_rush_across_difficult_terrain_is_not_held_to_six_inches(bool banner, bool granted)
    {
        var sim = Activated(GfNames.RushSeq, GfNames.Rush, banner, Patch(TerrainDeclarations.Difficult), granted: granted);
        var carried = banner || granted;
        Assert.Equal(carried ? null : GfNames.DifficultCap, Region(sim).DifficultCap);
        Shift(sim, UnitOf(sim, 0).Models[0].Id, 8);
        if (carried) Assert.Contains(GfNames.EternalFlame, Applied(sim, "move"));
        if (granted) Assert.Single(sim.Unit(UnitOf(sim, 0).Id).Grants);                                   // a relic is the game's: the move spends nothing
    }

    [Fact]
    public void A_move_through_dangerous_terrain_takes_no_test()
    {
        var with = Activated(GfNames.AdvanceSeq, GfNames.Advance, true, Patch(TerrainDeclarations.Dangerous));
        Shift(with, UnitOf(with, 0).Models[0].Id, 5);
        Assert.Empty(with.Steps("test"));
        Assert.Contains(GfNames.EternalFlame, Applied(with, "move"));

        var without = Activated(GfNames.AdvanceSeq, GfNames.Advance, false, Patch(TerrainDeclarations.Dangerous));
        Shift(without, UnitOf(without, 0).Models[0].Id, 5);
        Assert.NotEmpty(without.Steps("test"));
    }

    [Fact]
    public void A_unit_activated_in_dangerous_terrain_takes_no_test()
    {
        var with = Activated(GfNames.HoldSeq, GfNames.Hold, true, Patch(TerrainDeclarations.Dangerous), x1: -17);
        Assert.Empty(with.Steps("test"));
        Assert.Contains(GfNames.EternalFlame, Applied(with, "activation-terrain"));

        var without = Activated(GfNames.HoldSeq, GfNames.Hold, false, Patch(TerrainDeclarations.Dangerous), x1: -17);
        Assert.NotEmpty(without.Steps("test"));
    }

    [Fact]
    public void The_banner_is_no_leave_to_move_through_impassable_terrain()
    {
        var sim = Activated(GfNames.AdvanceSeq, GfNames.Advance, true, Patch(TerrainDeclarations.Impassable));
        Assert.NotEmpty(Region(sim).ImpassableVolumeIds);
        Assert.False(Region(sim).PassesModels);
    }
}
