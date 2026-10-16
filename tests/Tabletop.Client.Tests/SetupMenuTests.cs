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
        var item = MenuModel.ForSetupPiece(Draft, Hill.Id).Single();
        Assert.Equal("Remove", item.Label);
        Assert.Null(item.Build(null));
        Assert.Equal(new RemoveTerrain(Hill.Id), item.Edit?.Invoke(null));
        Assert.Equal(new RemoveSetupMarker(Objective.Id), MenuModel.ForSetupPiece(Draft, Objective.Id).Single().Edit?.Invoke(null));
        Assert.Empty(MenuModel.ForSetupPiece(Draft, Guid.CreateVersion7()));
    }
}
