using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class DraftEditorTests
{
    static readonly TableState Empty = new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static TableState Apply(TableState d, SetupEdit e) { var r = DraftEditor.Apply(d, e, out var missing); Assert.Null(missing); return r!; }
    static void RefusedEdit(TableState d, SetupEdit e) { Assert.Null(DraftEditor.Apply(d, e, out var missing)); Assert.NotNull(missing); }
    static TerrainPlacement Hill(double x, double y) => TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(x, y, 0) };

    [Fact]
    public void Size_and_texture()
    {
        var d = Apply(Apply(Empty, new SetTableSize(new TableSize(48, 48))), new SetTexture(VisualRef.OfBuiltin("desert")));
        Assert.Equal((48.0, "desert"), (d.Size.Width, d.Texture.Builtin));
    }

    [Fact]
    public void Terrain_add_move_scale_override_swap_remove()
    {
        var hill = Hill(10, 10);
        var d = Apply(Empty, new AddTerrain(hill));
        RefusedEdit(d, new AddTerrain(hill));                                            // same id twice
        d = Apply(d, new MoveTerrain(hill.Id, new Point3(20, 20, 0), new Yaw(90)));
        d = Apply(d, new ScaleTerrain(hill.Id, 2));
        var overridden = hill.Volumes[0] with { Volume = hill.Volumes[0].Volume with { Declarations = TerrainDeclarations.Impassable } };
        d = Apply(d, new OverrideTerrainVolumes(hill.Id, [overridden]));
        d = Apply(d, new SwapTerrainMesh(hill.Id, VisualRef.OfBuiltin("ruin")));
        var t = d.Terrain.Single();
        Assert.Equal((20.0, 90.0, 2.0, "ruin", TerrainDeclarations.Impassable), (t.Position.X, t.Yaw.Degrees, t.Scale, t.Mesh.Builtin, t.Volumes[0].Volume.Declarations));
        Assert.Empty(Apply(d, new RemoveTerrain(hill.Id)).Terrain);
        RefusedEdit(d, new MoveTerrain(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0)));
        RefusedEdit(d, new RemoveTerrain(Guid.CreateVersion7()));
    }

    [Fact]
    public void Markers_add_move_remove()
    {
        var m = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        var d = Apply(Empty, new AddSetupMarker(m));
        d = Apply(d, new MoveSetupMarker(m.Id, new Point3(30, 24, 0), new Yaw(0)));
        Assert.Equal(30, d.Markers.Single().Position.X);
        Assert.Empty(Apply(d, new RemoveSetupMarker(m.Id)).Markers);
        RefusedEdit(d, new MoveSetupMarker(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0)));
    }

    [Fact]
    public void Zones_follow_the_style_by_ordinal_and_swap()
    {
        var d = Apply(Empty, new SetZoneStyle("free-for-all", ["free-for-all/1", "free-for-all/2", "free-for-all/3"]));
        Assert.Equal([(1, "free-for-all/1"), (2, "free-for-all/2"), (3, "free-for-all/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        d = Apply(d, new SwapZones(1, 2));
        Assert.Equal([(1, "free-for-all/2"), (2, "free-for-all/1"), (3, "free-for-all/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        RefusedEdit(d, new SwapZones(1, 9));
        Assert.Equal(2, Apply(d, new SetZoneStyle("spearhead", ["spearhead/1", "spearhead/2"])).Zones.Count);
    }

    [Fact]
    public void Replacing_the_draft_takes_the_layout_whole_but_keeps_the_games_row_id()
    {
        var layout = TableStateTests.Sample();
        var d = Apply(Empty, new ReplaceDraft(layout));
        Assert.Equal(Empty.Id, d.Id);
        Assert.Equal(layout.Size, d.Size);
        Assert.Equal(layout.Terrain.Count + layout.Markers.Count + layout.Zones.Count, d.Terrain.Count + d.Markers.Count + d.Zones.Count);
    }

    [Fact]
    public void Off_slab_terrain_is_dropped_by_the_corner_origin_rule()
    {
        var d = Apply(Apply(Apply(Empty, new AddTerrain(Hill(10, 10))), new AddTerrain(Hill(80, 10))), new AddTerrain(Hill(72, -1)));
        Assert.True(DraftEditor.OnSlab(d, new Point3(72, 48, 0)));
        Assert.False(DraftEditor.OnSlab(d, new Point3(72.5, 48, 0)));
        Assert.Single(DraftEditor.DropOffSlab(d).Terrain);
    }

    // Controller ruling: a JSON property left out never reaches the VisualRef converter, so the parent record receives
    // default(VisualRef) — both fields null, IsValid false. The row's check constraint would reject the write after the
    // board was already broadcast, so DraftEditor.Apply must catch this itself and name the visual in `missing`.
    public static IEnumerable<object[]> EditsWithAnInvalidVisual()
    {
        var hill = Hill(10, 10);
        var withHill = Empty with { Terrain = [hill] };
        yield return new object[] { Empty, new SetTexture(default) };
        yield return new object[] { Empty, new AddTerrain(TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Mesh = default }) };
        yield return new object[] { withHill, new SwapTerrainMesh(hill.Id, default) };
        yield return new object[] { Empty, new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(0, 0, 0), new Yaw(0), 3, null, default)) };
        yield return new object[] { Empty, new ReplaceDraft(TableStateTests.Sample() with { Texture = default }) };
    }

    [Theory]
    [MemberData(nameof(EditsWithAnInvalidVisual))]
    public void An_edit_naming_an_invalid_visual_is_refused(TableState draft, SetupEdit edit)
    {
        var result = DraftEditor.Apply(draft, edit, out var missing);
        Assert.Null(result);
        Assert.NotNull(missing);
    }

    static TerrainVolume Vol(Footprint f) { var v = Hill(0, 0).Volumes[0]; return v with { Volume = v.Volume with { Footprint = f } }; }

    public static TheoryData<string, Func<TerrainPlacement, SetupEdit>> Degenerate => new()
    {
        { "scale zero", h => new ScaleTerrain(h.Id, 0) },
        { "scale negative", h => new ScaleTerrain(h.Id, -1) },
        { "scale NaN", h => new ScaleTerrain(h.Id, double.NaN) },
        { "scale infinite", h => new ScaleTerrain(h.Id, double.PositiveInfinity) },
        { "add with omitted scale", h => new AddTerrain(h with { Id = Guid.CreateVersion7(), Scale = 0 }) },
        { "replace draft with scale zero", h => new ReplaceDraft(Empty with { Terrain = [h with { Scale = 0 }] }) },
        { "rect width zero", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(0, 2))]) },
        { "rect length zero", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(2, 0))]) },
        { "rect negative", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(-2, 2))]) },
        { "circle radius zero", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Circle(0))]) },
        { "footprint NaN", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(double.NaN, 2))]) },
        { "add with zero footprint", h => new AddTerrain(h with { Id = Guid.CreateVersion7(), Volumes = [Vol(Footprint.Rect(0, 2))] }) },
        { "table width zero", h => new SetTableSize(new TableSize(0, 48)) },
        { "table length zero", h => new SetTableSize(new TableSize(72, 0)) },
        { "table negative", h => new SetTableSize(new TableSize(-72, 48)) },
        { "table infinite", h => new SetTableSize(new TableSize(72, double.PositiveInfinity)) },
        { "replace draft with zero table edge", h => new ReplaceDraft(Empty with { Size = new TableSize(0, 48) }) },
        { "scale 1e-4 on the 6x6 hill", h => new ScaleTerrain(h.Id, 1e-4) },
        { "add with a 1e-4 scale", h => new AddTerrain(h with { Id = Guid.CreateVersion7(), Scale = 1e-4 }) },
        { "tiny rect dimension", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(1e-7, 2))]) },
        { "undefined footprint kind", h => new OverrideTerrainVolumes(h.Id, [Vol(new Footprint((FootprintKind)7, 2, 2))]) },
        { "tiny table edge", h => new SetTableSize(new TableSize(1e-9, 48)) },
        { "tiny table whose zones cannot be built", h => new SetTableSize(new TableSize(1e-3, 1e-3)) },
        { "replace draft with a tiny table edge", h => new ReplaceDraft(Empty with { Size = new TableSize(1e-9, 48) }) },
    };

    public static TheoryData<string, Func<TerrainPlacement, SetupEdit>> SmallButValid => new()
    {
        { "scale 2e-4 on the 6x6 hill", h => new ScaleTerrain(h.Id, 2e-4) },
        { "1e-3 rect dimension", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Rect(1e-3, 2))]) },
        { "circle radius 1e-4", h => new OverrideTerrainVolumes(h.Id, [Vol(Footprint.Circle(1e-4))]) },
        { "0.05 table", h => new SetTableSize(new TableSize(0.05, 0.05)) },
    };

    [Theory, MemberData(nameof(SmallButValid))]
    public void A_small_outline_that_geometry_can_build_is_accepted(string _, Func<TerrainPlacement, SetupEdit> edit)
    {
        var hill = Hill(10, 10);
        Apply(Apply(Empty, new AddTerrain(hill)), edit(hill));
    }

    [Theory, MemberData(nameof(Degenerate))]
    public void A_degenerate_outline_is_refused(string _, Func<TerrainPlacement, SetupEdit> edit)
    {
        var hill = Hill(10, 10);
        var d = Apply(Empty, new AddTerrain(hill));
        RefusedEdit(d, edit(hill));
    }
}
