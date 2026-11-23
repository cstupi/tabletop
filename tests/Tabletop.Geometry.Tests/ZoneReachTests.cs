namespace Tabletop.Geometry.Tests;

/// <summary>ZoneReach: the area a base placed "fully within X of their deployment zone" (OPR p.14 Scout 12", 3.6.0 p.15 9") stands in, as an outline.</summary>
public class ZoneReachTests
{
    static readonly TableSize Opr = new(72, 48);

    static double Area(IReadOnlyList<(Point2 A, Point2 B)> segments) => segments.Sum(s => s.A.Cross(s.B)) / 2;

    [Fact] public void Frontline_grown_by_nine_is_the_band_from_27_to_the_edge()
    {
        var outline = ZoneReach.Outline(ZoneStyles.Resolve("frontline/1", Opr), 9, Opr);
        Assert.Equal(72 * 21, Area(outline), 3);
        Assert.Equal(27, outline.Min(s => Math.Min(s.A.Y, s.B.Y)), 6);
    }

    [Fact] public void Zero_reach_is_the_zone_itself()
    {
        var outline = ZoneReach.Outline(ZoneStyles.Resolve("frontline/2", Opr), 0, Opr);
        Assert.Equal(72 * 12, Area(outline), 6);
    }

    [Fact] public void A_corner_square_grown_by_nine_rounds_its_inner_corner_and_stops_at_the_table()
    {
        var outline = ZoneReach.Outline(ZoneStyles.Resolve("opposing-forces/1", Opr), 9, Opr);
        // 33 x 33 less the corner square the arc leaves out: 81 - a quarter of pi 9 squared.
        Assert.Equal(33 * 33 - 81 + Math.PI * 81 / 4, Area(outline), 0);
        Assert.All(outline, s => Assert.True(s.A.X >= -1e-9 && s.A.X <= 72 + 1e-9 && s.A.Y >= -1e-9 && s.A.Y <= 48 + 1e-9));
    }

    [Fact] public void Two_triangles_grown_join_into_one_outline_whose_every_point_is_the_reach_away_or_on_the_table_edge()
    {
        var zone = ZoneStyles.Resolve("disordered/1", Opr);
        var outline = ZoneReach.Outline(zone, 9, Opr);
        var outlines = zone.Select(z => new PolygonOutline(z.Vertices)).ToList();
        Assert.All(outline, s =>
        {
            var onEdge = Math.Abs(s.A.X) < 1e-6 || Math.Abs(s.A.X - 72) < 1e-6 || Math.Abs(s.A.Y) < 1e-6 || Math.Abs(s.A.Y - 48) < 1e-6;
            if (!onEdge) Assert.Equal(9, outlines.Min(o => o.DistanceTo(s.A)), 1);
        });
        var single = Area(ZoneReach.Outline([zone[0]], 9, Opr)) + Area(ZoneReach.Outline([zone[1]], 9, Opr));
        Assert.InRange(Area(outline), 0.5 * single, single - 1);    // the overlap is counted once
    }
}
