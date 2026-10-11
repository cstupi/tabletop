namespace Tabletop.Geometry.Tests;

/// <summary>Movement.Region's body prefilter: far bases are dropped, and the outline is unchanged against a brute-force region.</summary>
public class MovementRegionPrefilterTests
{
    static readonly TableSize Opr = new(72, 48);

    /// <summary>Region as it was before the prefilter: every other base and every volume tested exactly at every step.</summary>
    static MovementRegion BruteForce(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel> models)
    {
        var origin = start.Placement.Position.Horizontal;
        var baseOutline = start.BaseOutline();
        var impassableOutlines = impassable.Select(v => v.WorldOutline()).ToList();
        var difficultOutlines = difficult.Select(v => v.WorldOutline()).ToList();
        bool Touches(Outline at, List<Outline> outlines) => outlines.Any(o => Outline.InteriorsIntersect(at, o));
        var slab = table is { } t ? Table.Outline(t) : null;
        var bodies = models.Select(m => m.BaseOutline()).Where(o => !Outline.InteriorsIntersect(baseOutline, o)).ToList();
        var boundary = new List<Point2>(Movement.Rays);
        var capped = false;
        var startLimit = allowance;
        if (difficultCap is { } cap0 && cap0 < startLimit && Touches(baseOutline, difficultOutlines))
        {
            startLimit = cap0;
            capped = true;
        }
        for (var r = 0; r < Movement.Rays; r++)
        {
            var angle = 2 * Math.PI * r / Movement.Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var limit = startLimit;
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Movement.Step, limit);
                var atOutline = baseOutline.Shift(dir * d);
                if (Touches(atOutline, impassableOutlines) || Touches(atOutline, bodies) || (slab is not null && !slab.ContainsOutline(atOutline))) break;
                if (difficultCap is { } cap && cap < limit && Touches(atOutline, difficultOutlines))
                {
                    limit = cap;
                    capped = true;
                    if (d > limit + Tolerance.Distance) break;
                }
                reached = d;
                if (d >= limit - Tolerance.Distance) break;
            }
            boundary.Add(origin + dir * reached);
        }
        return new MovementRegion(origin, allowance, capped, boundary);
    }

    static Base RandomBase(Random random) => random.Next(3) switch
    {
        0 => Base.Circle(0.8 + random.NextDouble() * 3),
        1 => Base.Oval(1.5 + random.NextDouble() * 4, 1 + random.NextDouble() * 2),
        _ => Base.Rect(1 + random.NextDouble() * 4, 1 + random.NextDouble() * 3),
    };

    [Fact]
    public void Bases_beyond_the_allowance_and_both_radii_are_dropped_and_bases_the_start_overlaps_are_ignored()
    {
        var start = Fx.Model(20, 20);                                        // 1.26" base, radius 0.63
        var near = Fx.Model(20 + 6 + 0.63 + 0.63 - 0.01, 20);                // just within 6" + both radii
        var far = Fx.Model(20 + 6 + 0.63 + 0.63 + 0.01, 20);                 // just beyond
        var overlapped = Fx.Model(20.5, 20);
        var kept = Movement.NearBodies(start, 6, [near, far, overlapped]);
        Assert.Equal([near.BaseOutline()], kept.Select(b => b.Outline));
    }

    [Fact]
    public void The_region_matches_the_brute_force_region_on_random_layouts()
    {
        var random = new Random(20261011);
        var blocked = 0;
        for (var layout = 0; layout < 20; layout++)
        {
            var start = Fx.Model(RandomBase(random), 4 + random.NextDouble() * 64, 4 + random.NextDouble() * 40, yaw: random.NextDouble() * 360);
            var allowance = random.Next(4) == 0 ? random.NextDouble() * 3 : 3 + random.NextDouble() * 12;
            var origin = start.Placement.Position.Horizontal;
            Point2 Around(double spread) => new(origin.X + (random.NextDouble() * 2 - 1) * spread, origin.Y + (random.NextDouble() * 2 - 1) * spread);
            var models = Enumerable.Range(0, random.Next(0, 40)).Select(_ =>
            {
                var at = random.Next(3) == 0 ? Around(allowance + 4) : new Point2(random.NextDouble() * 72, random.NextDouble() * 48);
                return Fx.Model(RandomBase(random), at.X, at.Y, yaw: random.NextDouble() * 360);
            }).ToList();
            var impassable = Enumerable.Range(0, random.Next(0, 4)).Select(_ =>
            {
                var at = Around(allowance + 6);
                return Fx.Box(at.X, at.Y, 1, 2 + random.NextDouble() * 8, 0, 4, d: TerrainDeclarations.Impassable, yaw: random.NextDouble() * 360);
            }).ToList();
            var difficult = Enumerable.Range(0, random.Next(0, 3)).Select(_ =>
            {
                var at = Around(allowance + 4);
                return Fx.Cylinder(at.X, at.Y, 1 + random.NextDouble() * 4, 0, 4, VolumeKind.Area, TerrainDeclarations.Difficult);
            }).ToList();
            double? cap = random.Next(2) == 0 ? 6 : null;
            TableSize? table = random.Next(4) == 0 ? null : Opr;

            var expected = BruteForce(start, allowance, impassable, difficult, cap, table, models);
            var actual = Movement.Region(start, allowance, impassable, difficult, cap, table, models);
            Assert.Equal(expected.CappedByDifficultTerrain, actual.CappedByDifficultTerrain);
            Assert.Equal(expected.Boundary, actual.Boundary);
            var open = Movement.Region(start, allowance, impassable, difficult, cap, table);
            if (!open.Boundary.SequenceEqual(actual.Boundary)) blocked++;
        }
        Assert.InRange(blocked, 4, 19);   // the layouts exercise bases that stop the base short
    }
}
