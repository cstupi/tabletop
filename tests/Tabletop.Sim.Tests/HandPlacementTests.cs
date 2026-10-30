using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Run;

namespace Tabletop.Sim.Tests;

public class HandPlacementTests
{
    static readonly TableSize Table = new(72, 48);

    static ModelSlot Slot() => new(Guid.CreateVersion7(), 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);

    [Fact]
    public void An_anchor_the_random_draws_miss_is_found_clear_of_every_base_and_inside_the_zone()
    {
        // Frontline/1 is the table's last 12" (y 36..48). A model covering x 0..70 of it leaves a 2" strip at the east edge: one 1.26" base fits
        // there, which 40 random draws over the zone's 72" mostly miss. The zone's centre is on the covering base, and a hand placement on a
        // base is refused under strict (move_overlap), so the run would stall.
        var cover = new PlacedModel(new ModelBody(Base.Rect(12, 70), 1), new Placement(new Point3(35, 42, 0), new Yaw(0)));
        var slots = new[] { Slot() };
        for (var seed = 0ul; seed < 10; seed++)
        {
            var anchor = HandPlacement.ChooseAnchor(slots, [new Point2(0, 0)], "frontline/1", Table, [cover], new Chooser(seed));
            var placed = new PlacedModel(new ModelBody(slots[0].Base, slots[0].Height), new Placement(new Point3(anchor.X, anchor.Y, 0), new Yaw(0)));
            Assert.True(ZoneStyles.BaseFullyWithin("frontline/1", Table, placed), $"seed {seed}: {anchor} is not fully within the zone");
            Assert.True(Distance.EdgeToEdge(placed, cover) > 1e-6, $"seed {seed}: {anchor} touches the covering base");
        }
    }

    [Fact]
    public void The_anchor_is_inside_the_zone_the_legs_resolve()
    {
        // side-battle/1 under 3.6.0 p.21's legs runs 36" up the short edge; a cover over y 0..29.5, x 0..54 leaves only the corner the
        // 3.5.1 triangle (30" up) does not reach.
        var legs = new ZoneLegs(2.0 / 3, 0.75);
        var cover = new PlacedModel(new ModelBody(Base.Rect(29.5, 54), 1), new Placement(new Point3(27, 14.75, 0), new Yaw(0)));
        var slots = new[] { Slot() };
        for (var seed = 0ul; seed < 5; seed++)
        {
            var anchor = HandPlacement.ChooseAnchor(slots, [new Point2(0, 0)], "side-battle/1", Table, [cover], new Chooser(seed), legs);
            var placed = new PlacedModel(new ModelBody(slots[0].Base, slots[0].Height), new Placement(new Point3(anchor.X, anchor.Y, 0), new Yaw(0)));
            Assert.True(ZoneStyles.BaseFullyWithin("side-battle/1", Table, placed, legs), $"seed {seed}: {anchor} is not fully within the zone");
            Assert.False(ZoneStyles.BaseFullyWithin("side-battle/1", Table, placed), $"seed {seed}: {anchor} is inside the 3.5.1 triangle too");
            Assert.True(Distance.EdgeToEdge(placed, cover) > 1e-6, $"seed {seed}: {anchor} touches the covering base");
        }
    }
}
