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

    /// <summary>Near an edge or a corner a ring spot off the table is skipped, never pulled back onto the table toward the anchor: every
    /// model lands on the table, clear of the anchor by the separation and of the others, whatever the anchor's base.</summary>
    [Theory]
    [InlineData(0.5, 0.5, false)]
    [InlineData(2, 24, false)]
    [InlineData(71, 47, true)]
    [InlineData(36, 1.5, true)]
    public void Near_the_edge_every_model_lands_on_the_table_and_clear_of_the_anchor(double x, double y, bool rect)
    {
        var models = Squad(); var size = new TableSize(72, 48);
        var anchorBase = rect ? Base.Rect(5, 3) : Base.Circle(1);
        var anchor = new Placement(new Point3(x, y, 0), new Yaw(0));
        var placed = DeployView.Around(models, anchor, anchorBase, size, clearance: 1);
        Assert.Equal(models.Count, placed.Count);
        var bodies = placed.Select(r => new PlacedModel(new ModelBody(models.First(m => m.Id == r.ModelId).Slot.Base, 1), ((OnTable)r.To).Placement)).ToList();
        var at = new PlacedModel(new ModelBody(anchorBase, 1), anchor);
        foreach (var b in bodies)
        {
            var half = Math.Max(b.Body.Base.A, b.Body.Base.B) / 2; var c = b.Placement.Position;
            Assert.True(c.X >= half && c.X <= size.Width - half && c.Y >= half && c.Y <= size.Length - half, $"{c} off the table");
            Assert.True(Distance.BodyToBody(b, at) >= 1 - 1e-6, $"{c} within 1\" of the anchor");
        }
        for (var i = 0; i < bodies.Count; i++) for (var j = i + 1; j < bodies.Count; j++) Assert.True(Distance.BodyToBody(bodies[i], bodies[j]) > 0, "two ghosts overlap");
    }
}
