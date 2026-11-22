namespace Tabletop.Geometry.Tests;

/// <summary>
/// The formation search's shortcuts answer what the plain computations they stand in for answer, over many placements: ContactProbe.Under against
/// Distance.EdgeToEdge, Overlap.BasesFacing against Overlap.Bases, Movement.Fitting.Fits against Movement.Check, and Movement.Contacts against
/// ContactPoints' bisection on the plain distance.
/// </summary>
public class FormationSearchReferenceTests
{
    static readonly Base Oval = Base.Oval(2.362204724409449, 1.3779527559055118);
    static readonly Base[] Bases = [Oval, Base.Oval(4, 2.5), Base.Rect(2, 4), Base.Rect(1, 1), Base.Circle(1.26), Base.Circle(2.36)];

    static IEnumerable<(PlacedModel A, PlacedModel B)> Pairs()
    {
        var random = new Random(7);
        foreach (var a in Bases)
            foreach (var b in Bases)
                for (var i = 0; i < 300; i++)
                {
                    var (x, y) = (10 + random.NextDouble() * 5, 10 + random.NextDouble() * 5);
                    yield return (Fx.Model(a, 12.5, 12.5, yaw: random.NextDouble() * 360), Fx.Model(b, x, y, z: random.Next(3) * 0.75, yaw: random.NextDouble() * 360));
                }
    }

    [Fact]
    public void The_probe_answers_what_the_distance_answers()
    {
        foreach (var (a, b) in Pairs())
        {
            if (ContactProbe.For(a, b) is not { } probe) continue;
            foreach (var gap in new[] { 0.05, 0.3, 0.7, 2.0 })
                Assert.Equal(Distance.EdgeToEdge(a, b) < gap, probe.Under(a.Placement.Position.Horizontal, gap));
        }
    }

    /// <summary>Near the threshold, where the bisection asks: a base walked along a ray towards its gap from the target.</summary>
    [Fact]
    public void The_probe_answers_what_the_distance_answers_at_the_threshold()
    {
        var target = Fx.Model(Oval, 20, 20, yaw: 30);
        foreach (var b in Bases)
        {
            var start = Fx.Model(b, 10, 12, yaw: 75);
            var probe = ContactProbe.For(start, target);
            if (probe is null) continue;
            var points = Movement.ContactPoints(start, target, 0.3, 16);
            foreach (var p in points)
            {
                var dir = p - target.Placement.Position.Horizontal;
                for (var k = -40; k <= 40; k++)
                {
                    var at = target.Placement.Position.Horizontal + dir * (1 + k * 1e-7);
                    var there = start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, 0) } };
                    Assert.Equal(Distance.EdgeToEdge(there, target) < 0.3, probe.Under(at, 0.3));
                }
            }
        }
    }

    [Fact]
    public void Facing_overlap_answers_what_overlap_answers()
    {
        foreach (var (a, b) in Pairs())
        {
            Assert.Equal(Overlap.Bases(a, b), Overlap.BasesFacing(a, b));
            Assert.Equal(Overlap.Bases(b, a), Overlap.BasesFacing(b, a));
        }
    }

    [Fact]
    public void Contacts_are_the_bisection_on_the_plain_distance()
    {
        foreach (var (a, b) in Pairs().Where((_, i) => i % 25 == 0))
            foreach (var gap in new[] { 0.05, 0.7 })
                Assert.Equal(Plain(a, b, gap, 16), Movement.ContactPoints(a, b, gap, 16));
    }

    /// <summary>ContactPoints as it was: each bisection step measures Distance.EdgeToEdge.</summary>
    static List<Point2> Plain(PlacedModel start, PlacedModel target, double gap, int bearings)
    {
        var centre = target.Placement.Position.Horizontal;
        var facing = start.Placement.Position.Horizontal - centre;
        var bearing = facing.Length > 1e-9 ? Math.Atan2(facing.Y, facing.X) : 0;
        var points = new List<Point2>(bearings);
        for (var k = 0; k < bearings; k++)
        {
            var angle = bearing + (k + 1) / 2 * (k % 2 == 0 ? -1 : 1) * Math.Tau / bearings;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            PlacedModel There(double r) => start with { Placement = start.Placement with { Position = new Point3(centre.X + dir.X * r, centre.Y + dir.Y * r, start.Placement.Position.Z) } };
            double lo = 0, hi = 1;
            while (Distance.EdgeToEdge(There(hi), target) < gap && hi < 1024) hi *= 2;
            for (var i = 0; i < 40; i++) { var mid = (lo + hi) / 2; if (Distance.EdgeToEdge(There(mid), target) < gap) lo = mid; else hi = mid; }
            points.Add(centre + dir * hi);
        }
        return points;
    }

    /// <summary>Each rider of CavalryRushScene to points across the table, with and without the difficult cap, models in the way or passed.</summary>
    [Theory]
    [InlineData(0, 6.0, false)]
    [InlineData(2, 6.0, false)]
    [InlineData(4, null, false)]
    [InlineData(1, 6.0, true)]
    public void Fitting_answers_what_check_answers(int rider, double? cap, bool passes)
    {
        var start = CavalryRushScene.Riders[rider];
        var models = passes ? null : CavalryRushScene.Others;
        var fitting = new Movement.Fitting(start, 16, [CavalryRushScene.Building], CavalryRushScene.DifficultGround, cap, CavalryRushScene.Table, models,
            CavalryRushScene.Volumes, CavalryRushScene.Hills);
        var fits = 0;
        for (var x = 0.5; x < 72; x += 1.7)
            for (var y = 0.5; y < 30; y += 1.3)
            {
                var to = new Point2(x, y);
                var check = Movement.Check(start, to, 16, [CavalryRushScene.Building], CavalryRushScene.DifficultGround, cap, CavalryRushScene.Table, models,
                    CavalryRushScene.Volumes, steep: CavalryRushScene.Hills);
                var expected = check is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false, CrossesModels: false };
                Assert.Equal(expected, fitting.Fits(to));
                if (expected) fits++;
            }
        Assert.InRange(fits, 1, int.MaxValue);
    }
}
