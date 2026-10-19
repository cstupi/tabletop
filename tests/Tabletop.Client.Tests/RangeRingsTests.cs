using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RangeRingsTests
{
    /// <summary>Review Focus 4. Squad: rifles 24" on the models at (10,10) and (12,10), a pistol 12" at (14,10), a knife with no Range; 1" bases.</summary>
    [Fact]
    public void Rings_per_printed_range_around_their_carriers_as_one_outline()
    {
        var p = TableUx.After(TableUx.Deploy);
        var rings = RangeRings.Of(p, TableUx.Squad.Id);
        Assert.Equal(new[] { (12.0, "12\""), (24.0, "24\"") }, rings.Select(r => (r.Inches, r.Label)));
        Assert.Equal(RangeRings.Points, rings[0].Segments.Count);                                                       // one carrier, one whole ring
        Assert.All(rings[0].Segments, s => Assert.Equal(12.5, s.A.DistanceTo(new Point2(14, 10)), 3));                  // range from the base edge
        Point2[] rifles = [new(10, 10), new(12, 10)];
        Assert.InRange(rings[1].Segments.Count, RangeRings.Points / 2, 2 * RangeRings.Points - 1);                      // the overlap is cut out
        Assert.All(rings[1].Segments, s => Assert.All(rifles, c => Assert.True(c.DistanceTo(s.A) >= 24.5 - 1e-3 && c.DistanceTo(s.B) >= 24.5 - 1e-3)));
        Assert.All(rings[1].Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.Equal(24.5, rifles.Min(c => c.DistanceTo(q)), 3));  // carriers only: no 24" ring around the pistol
        Assert.All(rings, r => Assert.All(r.Segments.SelectMany(s => new[] { s.A, s.B }),                                               // one closed outline: no holes
            q => Assert.Equal(2, r.Segments.Sum(s => (s.A.DistanceTo(q) < 1e-3 ? 1 : 0) + (s.B.DistanceTo(q) < 1e-3 ? 1 : 0)))));
        Assert.Empty(RangeRings.Of(p, TableUx.Orcs.Id));                                                                 // melee only
        Assert.Equal(((double?)24.0, (double?)null, (double?)null), (RangeRings.Inches("24\""), RangeRings.Inches("-"), RangeRings.Inches("0")));
    }
    [Fact]
    public void The_cache_recomputes_only_when_the_unit_or_its_placements_change()
    {
        var a = TableUx.Applier(TableUx.Deploy); var cache = new RangeRingCache();
        var first = cache.Of(a.Projection, TableUx.Squad.Id);
        Assert.Same(first, cache.Of(a.Projection, TableUx.Squad.Id));
        Assert.Empty(cache.Of(a.Projection, TableUx.Orcs.Id));
        var back = cache.Of(a.Projection, TableUx.Squad.Id);
        Assert.NotSame(first, back);
        a.ApplyAll([new GameEvent(2, TableUx.P1, new ModelsMoved([new ModelRelocation(TableUx.Squad.Models[2].Id, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))),
            new OnTable(new Placement(new Point3(20, 10, 0), new Yaw(0))))]), null)]);
        var moved = cache.Of(a.Projection, TableUx.Squad.Id);
        Assert.NotSame(back, moved);
        Assert.Equal(RangeRings.Of(a.Projection, TableUx.Squad.Id).Select(r => r.Segments.Count), moved.Select(r => r.Segments.Count));
    }
}
