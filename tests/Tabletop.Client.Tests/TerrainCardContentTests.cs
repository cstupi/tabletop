using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.Geometry.TerrainDeclarations;
namespace Tabletop.Client.Tests;

public class TerrainCardContentTests
{
    static readonly Guid Area = Guid.CreateVersion7(), Wall = Guid.CreateVersion7(), Asset = Guid.CreateVersion7();

    static TerrainPlacement Ruin(double yaw = 45, double scale = 1, TerrainDeclarations d = TerrainDeclarations.Cover | Difficult | BlocksLineOfSight | SeeInAndOut, string? notes = null) =>
        new(Guid.Parse("0190a000-0000-7000-8000-000000000001"), VisualRef.OfBuiltin("ruin"), new Point3(10, 10, 0), new Yaw(yaw), scale,
        [
            new(Area, new Volume(VolumeKind.Area, Footprint.Rect(8, 6), new Point2(0, 0), new Yaw(0), 0, 4, d, notes)),
            new(Wall, new Volume(VolumeKind.Solid, Footprint.Circle(1.5), new Point2(0, 0), new Yaw(0), 0, 2, None, null)),
        ]);

    static TableState Table(params TerrainPlacement[] terrain) => TableStateTests.Sample() with { Terrain = terrain };

    [Fact]
    public void A_selected_piece_shows_its_name_source_facing_and_each_volume()
    {
        var c = TerrainCardContent.Of(Table(Ruin()), GameStatus.Playing, Ruin().Id)!;
        Assert.Equal("Ruin", c.Name);
        Assert.Equal("Built-in ruin", c.Source);
        Assert.Equal(45, c.Facing);
        Assert.Equal("0–4\"", c.Height);
        Assert.False(c.Editable);
        Assert.Equal(new TerrainCardVolume(Area, "Area", "8 × 6\"", "0–4\"", ["Cover", "Difficult", "Blocks LOS", "See in/out"], null), c.Volumes[0]);
        Assert.Equal(new TerrainCardVolume(Wall, "Solid", "3\" across", "0–2\"", ["Open"], null), c.Volumes[1]);
    }

    [Fact]
    public void Scale_applies_to_footprints_and_heights_and_facing_reads_0_to_359()
    {
        var c = TerrainCardContent.Of(Table(Ruin(yaw: -15, scale: 0.5)), GameStatus.Setup, Ruin().Id)!;
        Assert.Equal(345, c.Facing);
        Assert.Equal("4 × 3\"", c.Volumes[0].Footprint);
        Assert.Equal("0–2\"", c.Volumes[0].Heights);
        Assert.Equal(0.5, c.Scale);
        Assert.Equal(0, TerrainCardContent.Of(Table(Ruin(yaw: 359.6)), GameStatus.Setup, Ruin().Id)!.Facing);
    }

    [Fact]
    public void Setup_edits_and_play_reads()
    {
        Assert.True(TerrainCardContent.Of(Table(Ruin()), GameStatus.Setup, Ruin().Id)!.Editable);
        Assert.False(TerrainCardContent.Of(Table(Ruin()), GameStatus.Ended, Ruin().Id)!.Editable);
        Assert.Null(TerrainCardContent.Of(Table(Ruin()), GameStatus.Lobby, Ruin().Id));
    }

    [Fact]
    public void Nothing_selected_a_unit_or_a_marker_shows_no_terrain_card()
    {
        var table = Table(Ruin());
        Assert.Null(TerrainCardContent.Of(table, GameStatus.Setup, null));
        Assert.Null(TerrainCardContent.Of(table, GameStatus.Setup, Guid.CreateVersion7()));
        Assert.Null(TerrainCardContent.Of(table, GameStatus.Setup, table.Markers[0].Id));
        Assert.Null(TerrainCardContent.Of(null, GameStatus.Setup, Ruin().Id));
    }

    [Fact]
    public void An_asset_piece_reads_Terrain_from_the_library()
    {
        var piece = Ruin() with { Mesh = VisualRef.OfAsset(Asset) };
        var c = TerrainCardContent.Of(Table(piece), GameStatus.Setup, piece.Id)!;
        Assert.Equal("Terrain", c.Name);
        Assert.Equal($"Library asset {Names.Short(Asset)}", c.Source);
    }

    [Fact]
    public void Built_again_is_equal_and_each_shown_field_is_a_change()
    {
        TerrainCardContent Of(TerrainPlacement t) => TerrainCardContent.Of(Table(t), GameStatus.Setup, t.Id)!;
        var before = Of(Ruin());
        Assert.Equal(before, Of(Ruin()));
        Assert.Equal(before.GetHashCode(), Of(Ruin()).GetHashCode());
        Assert.Equal(before, Of(Ruin() with { Position = new Point3(30, 20, 0) }));   // a drag on the table does not rebuild the card
        Assert.NotEqual(before, Of(Ruin(yaw: 60)));
        Assert.NotEqual(before, Of(Ruin(scale: 2)));
        Assert.NotEqual(before, Of(Ruin(d: TerrainDeclarations.Cover)));
        Assert.NotEqual(before, Of(Ruin(notes: "crumbling")));
        Assert.NotEqual(before, Of(Ruin() with { Mesh = VisualRef.OfBuiltin("hill-small") }));
    }

    [Fact]
    public void A_setup_marker_shows_its_kind_size_and_label_in_setup_only()
    {
        var table = Table(Ruin()); var objective = table.Markers[0];
        Assert.Equal(new MarkerCardContent(objective.Id, "Objective", "3\" across", null), MarkerCardContent.Of(table, GameStatus.Setup, objective.Id));
        Assert.Null(MarkerCardContent.Of(table, GameStatus.Playing, objective.Id));
        Assert.Null(MarkerCardContent.Of(table, GameStatus.Setup, Ruin().Id));
    }

    [Fact]
    public void The_card_reads_the_draft_in_setup_and_the_board_in_play()
    {
        var draft = Table(Ruin()); var p = SheetFocusTests.Activating(null);
        Assert.Same(draft, TerrainCardContent.TableOf(GameStatus.Setup, draft, p));
        Assert.Same(p.Board.Table, TerrainCardContent.TableOf(GameStatus.Playing, draft, p));
        Assert.Same(p.Board.Table, TerrainCardContent.TableOf(GameStatus.Ended, draft, p));
        Assert.Null(TerrainCardContent.TableOf(GameStatus.Lobby, draft, p));
    }
}
