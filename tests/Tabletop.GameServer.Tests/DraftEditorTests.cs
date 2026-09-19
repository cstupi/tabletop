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
        var d = Apply(Empty, new SetZoneStyle("long-edges", ["long-edges/1", "long-edges/2", "long-edges/3"]));
        Assert.Equal([(1, "long-edges/1"), (2, "long-edges/2"), (3, "long-edges/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        d = Apply(d, new SwapZones(1, 2));
        Assert.Equal([(1, "long-edges/2"), (2, "long-edges/1"), (3, "long-edges/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        RefusedEdit(d, new SwapZones(1, 9));
        Assert.Equal(2, Apply(d, new SetZoneStyle("corners", ["corners/1", "corners/2"])).Zones.Count);
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
}
