using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class SetupEditsTests
{
    static readonly TableSize Size = new(72, 48);
    static Volume Vol(VolumeKind kind = VolumeKind.Solid, Footprint? footprint = null) =>
        new(kind, footprint ?? Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null);
    static TerrainPlacement Piece(params Volume[] volumes) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin"), new Point3(10, 10, 0), new Yaw(0), 1, volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v)).ToList());
    static Func<double> Draws(params double[] d) { var q = new Queue<double>(d); return q.Dequeue; }

    [Fact]
    public void Beside_spots_follow_the_draws()
    {
        var draws = Draws(0.0, 0.0, 0.99, 0.99);
        Assert.Equal(new Point3(76, 4, 0), SetupEdits.Beside(Size, draws));
        Assert.Equal(new Point3(92, 28, 0), SetupEdits.Beside(Size, draws));
    }

    [Fact]
    public void A_built_in_piece_is_placed_beside_the_table_at_yaw_zero()
    {
        var name = BuiltinContent.Terrain.Keys.Order().First();
        var edit = SetupEdits.AddBuiltin(name, Size, Draws(0.5, 0.5));
        var expected = BuiltinContent.Place(name, SetupEdits.Beside(Size, Draws(0.5, 0.5)), new Yaw(0));
        Assert.Equal(expected with { Id = edit.Placement.Id, Volumes = edit.Placement.Volumes }, edit.Placement);
        Assert.Equal(expected.Volumes.Select(v => v.Volume), edit.Placement.Volumes.Select(v => v.Volume));
    }

    [Fact]
    public void An_asset_piece_takes_each_volume_under_a_new_id()
    {
        var asset = Guid.CreateVersion7(); var volumes = new[] { Vol(), Vol(VolumeKind.Area) };
        var p = SetupEdits.AddAsset(asset, volumes, Size, Draws(0, 0)).Placement;
        Assert.Equal((VisualRef.OfAsset(asset), 1.0, new Yaw(0), new Point3(76, 4, 0)), (p.Mesh, p.Scale, p.Yaw, p.Position));
        Assert.Equal(volumes, p.Volumes.Select(v => v.Volume));
        Assert.Equal(2, p.Volumes.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public void An_objective_goes_to_a_free_spot()
    {
        var draft = TableUx.Board.Table;
        var o = SetupEdits.Objective(draft).Marker;
        Assert.Equal((MarkerKind.Objective, SetupLimits.FreeSpot(draft.Size, draft.Markers, 3), 3.0, (string?)null, "objective-ring"), (o.Kind, o.Position, o.Diameter, o.Label, o.Visual.Builtin));
        Assert.Equal(new Yaw(0), o.Yaw);
    }

    [Fact]
    public void Zones_send_the_styles_shapes()
    {
        var style = ZoneStyles.All[0];
        var edit = SetupEdits.Zones(style);
        Assert.Equal(style, edit.Style);
        Assert.Equal(ZoneStyles.ShapesOf(style), edit.Shapes);
    }

    [Fact]
    public void Table_size_texture_swap_zones_scale_swap_and_removes_are_their_edits()
    {
        var id = Guid.CreateVersion7(); var asset = Guid.CreateVersion7();
        Assert.Equal(new SetTableSize(new TableSize(60, 40)), SetupEdits.TableSize(60, 40));
        Assert.Equal(new SetTexture(VisualRef.OfBuiltin("grass")), SetupEdits.Texture("grass"));
        Assert.Equal(new SwapZones(1, 2), SetupEdits.SwapZones(1, 2));
        Assert.Equal(new ScaleTerrain(id, 1.5), SetupEdits.Scale(id, 1.5));
        Assert.Equal(new SwapTerrainMesh(id, VisualRef.OfBuiltin("ruin")), SetupEdits.SwapBuiltin(id, "ruin"));
        Assert.Equal(new SwapTerrainMesh(id, VisualRef.OfAsset(asset)), SetupEdits.SwapAsset(id, asset));
        Assert.Equal(new RemoveTerrain(id), SetupEdits.RemoveTerrain(id));
        Assert.Equal(new RemoveSetupMarker(id), SetupEdits.RemoveMarker(id));
    }

    [Fact]
    public void Clear_terrain_keeps_everything_but_the_terrain()
    {
        var draft = TableUx.Board.Table;
        Assert.NotEmpty(draft.Terrain);
        Assert.Equal(draft with { Terrain = [] }, SetupEdits.ClearTerrain(draft).Draft);
    }

    [Fact]
    public void Random_and_load_replace_the_draft()
    {
        var draft = TableUx.Board.Table; var laid = new RandomTable(1, [Piece(Vol())], [], []);
        Assert.Equal(LayoutChoices.WithTerrain(draft, laid), SetupEdits.Random(draft, laid).Draft);
        Assert.Equal(draft, SetupEdits.Load(draft).Draft);
    }

    [Fact]
    public void Reset_volumes_gives_the_volumes_new_ids()
    {
        var piece = Piece(Vol(), Vol(VolumeKind.Area)); var volumes = piece.Volumes.Select(v => v.Volume).ToList();
        var edit = SetupEdits.ResetVolumes(piece.Id, volumes);
        Assert.Equal(piece.Id, edit.PlacementId);
        Assert.Equal(volumes, edit.Volumes.Select(v => v.Volume));
        Assert.Empty(edit.Volumes.Select(v => v.Id).Intersect(piece.Volumes.Select(v => v.Id)));
    }

    [Fact]
    public void Changing_one_volume_keeps_the_others()
    {
        var piece = Piece(Vol(), Vol(VolumeKind.Area));
        var edit = SetupEdits.ChangeVolume(piece, piece.Volumes[1].Id, SetupEdits.WithTop(5));
        Assert.Equal(piece.Id, edit.PlacementId);
        Assert.Equal(piece.Volumes[0], edit.Volumes[0]);
        Assert.Equal(piece.Volumes[1] with { Volume = piece.Volumes[1].Volume with { Top = 5 } }, edit.Volumes[1]);
    }

    [Fact]
    public void A_circle_footprint_change_sets_both_halves()
    {
        var circle = Vol(footprint: new Footprint(FootprintKind.Circle, 2, 2)); var rect = Vol(footprint: Footprint.Rect(4, 8));
        Assert.Equal(new Footprint(FootprintKind.Circle, 3, 3), SetupEdits.WithFootprintA(3)(circle).Footprint);
        Assert.Equal(Footprint.Rect(3, 8), SetupEdits.WithFootprintA(3)(rect).Footprint);
        Assert.Equal(Footprint.Rect(4, 3), SetupEdits.WithFootprintB(3)(rect).Footprint);
    }

    [Fact]
    public void Kind_bottom_and_top_change_their_field()
    {
        var v = Vol();
        Assert.Equal(VolumeKind.Area, SetupEdits.WithKind(VolumeKind.Area)(v).Kind);
        Assert.Equal(1.5, SetupEdits.WithBottom(1.5)(v).Bottom);
        Assert.Equal(7.0, SetupEdits.WithTop(7)(v).Top);
    }

    [Fact]
    public void A_declaration_toggles_its_flag()
    {
        var on = SetupEdits.WithDeclaration(TerrainDeclarations.Cover, true)(Vol());
        Assert.Equal(TerrainDeclarations.Cover, on.Declarations);
        Assert.Equal(TerrainDeclarations.None, SetupEdits.WithDeclaration(TerrainDeclarations.Cover, false)(on).Declarations);
    }

    [Fact]
    public void Notes_are_clipped_and_empty_clears_them()
    {
        Assert.Equal(TextLimits.Notes, SetupEdits.WithNotes(new string('x', 600))(Vol()).Notes!.Length);
        Assert.Null(SetupEdits.WithNotes("")(Vol() with { Notes = "old" }).Notes);
        Assert.Equal("kept", SetupEdits.WithNotes("kept")(Vol()).Notes);
    }
}
