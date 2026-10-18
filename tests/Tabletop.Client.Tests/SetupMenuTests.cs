using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class SetupMenuTests
{
    static readonly TerrainPlacement Hill = new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(12, 12, 0), new Yaw(0), 1, []);
    static readonly Marker Objective = new(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
    static readonly TableState Draft = new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [Hill], [Objective], []);

    [Fact]
    public void A_terrain_piece_or_setup_marker_is_removed_by_its_own_edit_and_anything_else_has_none()
    {
        Assert.Equal(new RemoveTerrain(Hill.Id), MenuModel.RemoveFromDraft(Draft, Hill.Id));
        Assert.Equal(new RemoveSetupMarker(Objective.Id), MenuModel.RemoveFromDraft(Draft, Objective.Id));
        Assert.Null(MenuModel.RemoveFromDraft(Draft, Guid.CreateVersion7()));
    }

    [Fact]
    public void The_setup_menu_offers_remove_as_an_edit_not_a_command()
    {
        var item = MenuModel.ForSetupPiece(Draft, Hill.Id).Single(i => i.Label == "Remove");
        Assert.Equal("Remove", item.Label);
        Assert.Null(item.Build(null));
        Assert.Equal(new RemoveTerrain(Hill.Id), item.Edit?.Invoke(null));
        Assert.Equal(new RemoveSetupMarker(Objective.Id), MenuModel.ForSetupPiece(Draft, Objective.Id).Single().Edit?.Invoke(null));
        Assert.Empty(MenuModel.ForSetupPiece(Draft, Guid.CreateVersion7()));
    }

    [Fact]
    public void A_terrain_piece_turns_by_its_menu_and_a_marker_does_not()
    {
        var items = MenuModel.ForSetupPiece(Draft, Hill.Id);
        Assert.Equal(["Turn left 15°", "Turn right 15°", "Set facing…", "Remove"], items.Select(i => i.Label));
        Assert.Equal(new MoveTerrain(Hill.Id, Hill.Position, new Yaw(15)), items[0].Edit?.Invoke(null));
        Assert.Equal(new MoveTerrain(Hill.Id, Hill.Position, new Yaw(345)), items[1].Edit?.Invoke(null));
        Assert.Equal(new MoveTerrain(Hill.Id, Hill.Position, new Yaw(90)), items[2].Edit?.Invoke("450"));
        Assert.Null(items[2].Edit?.Invoke("north"));
        Assert.Equal(MenuInput.Integer, items[2].Input);
        Assert.Single(MenuModel.ForSetupPiece(Draft, Objective.Id));
    }

    [Fact]
    public void A_turn_that_swings_into_an_impassable_piece_moves_clear_of_it()
    {
        static TerrainVolume Box(double w, double l, TerrainDeclarations d) => new(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(w, l), new Point2(0, 0), new Yaw(0), 0, 4, d, null));
        var building = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("large-building"), new Point3(40, 30, 0), new Yaw(0), 1, [Box(6, 6, TerrainDeclarations.Impassable)]);
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("barricade"), new Point3(35.5, 30, 0), new Yaw(0), 1, [Box(1, 6, TerrainDeclarations.None)]);
        var turn = Assert.IsType<MoveTerrain>(MenuModel.TurnInDraft(Draft with { Terrain = [building, wall] }, wall.Id, 90));
        Assert.Equal(new Yaw(90), turn.Yaw);
        Assert.True(turn.Position.X <= 34 + 1e-3, $"at {turn.Position}");
    }
}
