using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A Spawn or Split reserve placed from the shelf around its anchor model.</summary>
public class DeployAroundTests
{
    static List<ModelInPlay> Squad() => TableUx.After().Models.Values.Where(m => m.UnitId == TableUx.Squad.Id).OrderBy(m => m.Slot.Ordinal).ToList();

    [Fact]
    public void Shelf_models_ring_the_anchor_touching_neither_it_nor_each_other()
    {
        var models = Squad();
        var anchor = new Placement(new Point3(30, 20, 0), new Yaw(0));
        var placed = DeployView.Around(models, anchor, Base.Circle(2), new TableSize(72, 48));
        Assert.Equal(models.Select(m => m.Id), placed.Select(r => r.ModelId));
        var at = placed.Select(r => ((OnTable)r.To).Placement.Position.Horizontal).ToList();
        Assert.All(at, x => Assert.Equal(1 + 0.2 + 0.5, x.DistanceTo(anchor.Position.Horizontal), 6));   // one ring: the anchor's half, a gap, the model's half
        for (var i = 0; i < at.Count; i++) for (var j = i + 1; j < at.Count; j++) Assert.True(at[i].DistanceTo(at[j]) >= 1.2);
        var clear = DeployView.Around(models, anchor, Base.Circle(2), new TableSize(72, 48), clearance: 1);     // the Ask's separation from the anchor's unit
        Assert.All(clear, r => Assert.Equal(1 + 1 + 0.5, ((OnTable)r.To).Placement.Position.Horizontal.DistanceTo(anchor.Position.Horizontal), 6));
    }

    [Fact]
    public void A_region_anchored_on_a_model_whose_movers_are_on_the_shelf_is_a_placement_not_a_disembark_from_inside()
    {
        var p = TableUx.After(TableUx.Deploy);
        var anchor = TableUx.ModelOf(TableUx.Squad);
        var reserve = TableUx.ModelOf(TableUx.Reserve);
        var place = new MoveRegion([reserve], 0, [], [], null, new MoveContainment([], anchor, 6), Separation: 1, PassesModels: true);
        Assert.True(MoveRuler.IsShelfPlacement(place, p));
        Assert.False(MoveRuler.IsShelfPlacement(place with { ModelIds = [TableUx.ModelOf(TableUx.Squad, 1)] }, p));   // on the table: not from the shelf
        Assert.False(MoveRuler.IsShelfPlacement(new MoveRegion([reserve], 6, [], [], null), p));                    // no anchor
    }

    [Fact]
    public void A_ring_past_the_table_edge_is_held_on_the_table()
    {
        var placed = DeployView.Around(Squad(), new Placement(new Point3(0.5, 0.5, 0), new Yaw(0)), Base.Circle(1), new TableSize(72, 48));
        Assert.All(placed, r => Assert.True(((OnTable)r.To).Placement.Position is { X: >= 0.5, Y: >= 0.5 }));
    }
}
