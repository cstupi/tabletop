namespace Tabletop.Geometry.Tests;

/// <summary>`SightBlockers` against the per-pair oracle: `LineOfSight.Check` and `Cover.SightBlockersBetween` over the volumes with the ignored id removed.</summary>
public class SightBlockersTests
{
    [Fact] public void Clear_line_reads_as_clear_and_unscreened()
    {
        var sight = new SightBlockers([]);
        Assert.True(sight.Check(Fx.Model(0, 0), Fx.Model(10, 0)).Clear);
        var reading = sight.Measure(Fx.Model(0, 0), Fx.Model(10, 0));
        Assert.True(reading.Clear);
        Assert.Empty(reading.Blockers);
    }

    [Fact] public void A_wall_obstructs_and_the_reading_names_it()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 4, d: Fx.Blocks);
        var reading = new SightBlockers([wall]).Measure(Fx.Model(0, 0), Fx.Model(10, 0));
        Assert.False(reading.Clear);
        Assert.Equal(new[] { wall.Id }, reading.Blockers);
    }

    [Fact] public void A_clear_line_past_a_low_wall_is_screened()
    {
        var low = Fx.Box(5, 0, 1, 6, 0, 1, d: Fx.Blocks);
        var reading = new SightBlockers([low]).Measure(Fx.Model(0, 0), Fx.Model(10, 0));
        Assert.True(reading.Clear);
        Assert.Equal(new[] { low.Id }, reading.Blockers);
    }

    [Fact] public void The_ignored_volume_never_blocks()
    {
        var targetBody = Bodies.AsBlockingVolume(Guid.CreateVersion7(), Fx.Model(10, 0));
        var sight = new SightBlockers([targetBody]);
        var reading = sight.Measure(Fx.Model(0, 0), Fx.Model(10, 0), ignoring: targetBody.Id);
        Assert.True(reading.Clear);
        Assert.Empty(reading.Blockers);
        Assert.False(sight.Check(Fx.Model(0, 0), Fx.Model(10, 0)).Clear);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Matches_the_per_pair_test_on_random_tables(int seed)
    {
        var rng = new Random(seed);
        var (clear, screened, blocked) = (0, 0, 0);
        for (var table = 0; table < 8; table++)
        {
            var (volumes, models) = Layout(rng);
            var sight = new SightBlockers(volumes);
            for (var pair = 0; pair < 40; pair++)
            {
                var from = models[rng.Next(5)];   // the observing unit: no bodies of its own in the way
                var to = models[rng.Next(models.Count)];
                Guid? ignoring = rng.Next(4) switch { 0 => null, 1 => to.Id, 2 => from.Id, _ => volumes[rng.Next(volumes.Count)].Id };
                var reading = AssertSame(sight, volumes, from.Model, to.Model, ignoring);
                if (!reading.Clear) blocked++;
                else if (reading.Blockers.Count > 0) screened++;
                else clear++;
            }
        }
        Assert.True(clear > 0 && screened > 0 && blocked > 0, $"clear {clear}, screened {screened}, blocked {blocked}");
    }

    [Fact] public void Matches_the_per_pair_test_on_tolerance_edges()
    {
        // Lines grazing edges and corners, thin slabs crossed by nearly flat lines, and bodies stacked over one another.
        var rng = new Random(42);
        for (var table = 0; table < 80; table++)
        {
            var volumes = new List<WorldVolume>();
            for (var i = 0; i < 12; i++)
            {
                var x = rng.Next(0, 13) * 0.5;
                var y = rng.Next(-6, 7) * 0.5;
                var bottom = rng.Next(0, 5) * 0.5;
                var top = bottom + (rng.Next(3) == 0 ? 1e-6 * rng.Next(0, 3) : rng.Next(1, 5) * 0.5);
                var d = rng.Next(3) == 0 ? Fx.ForestSight : Fx.Blocks;
                volumes.Add(rng.Next(2) == 0
                    ? Fx.Box(x, y, rng.Next(1, 5) * 0.5, rng.Next(1, 5) * 0.5, bottom, top, d: d, yaw: rng.Next(0, 4) * 45)
                    : Fx.Cylinder(x, y, rng.Next(1, 5) * 0.5, bottom, top, d: d));
            }
            var models = new List<PlacedModel>();
            for (var i = 0; i < 8; i++)
            {
                var x = rng.Next(-2, 15) * 0.5;
                var y = rng.Next(-6, 7) * 0.5;
                models.Add(Fx.Model(x, y, z: rng.Next(0, 6) * 0.5, height: rng.Next(1, 5) * 0.5));
                if (rng.Next(3) == 0) models.Add(Fx.Model(x, y, z: rng.Next(0, 6) * 0.5 + 1e-7 * rng.Next(0, 3), height: 1));
            }
            var sight = new SightBlockers(volumes);
            foreach (var from in models)
                foreach (var to in models)
                    AssertSame(sight, volumes, from, to, rng.Next(3) == 0 ? volumes[rng.Next(volumes.Count)].Id : null);
        }
    }

    [Fact] public void Reports_input_order_and_repeats_a_volume_listed_twice()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 4, d: Fx.Blocks);
        var low = Fx.Box(4, 0, 1, 6, 0, 1, d: Fx.Blocks);
        var notBlocking = wall with { Volume = wall.Volume with { Declarations = TerrainDeclarations.None } };
        List<WorldVolume> volumes = [wall, low, notBlocking, wall];
        AssertSame(new SightBlockers(volumes), volumes, Fx.Model(0, 0), Fx.Model(10, 0), null);
    }

    static SightReading AssertSame(SightBlockers sight, IReadOnlyList<WorldVolume> volumes, PlacedModel from, PlacedModel to, Guid? ignoring)
    {
        var left = volumes.Where(v => v.Id != ignoring).ToList();
        var oracle = LineOfSight.Check(from, to, left);
        var check = sight.Check(from, to, ignoring);
        Assert.Equal(oracle.Clear, check.Clear);
        Assert.Equal(oracle.ObstructedBy, check.ObstructedBy);
        var reading = sight.Measure(from, to, ignoring);
        Assert.Equal(oracle.Clear, reading.Clear);
        Assert.Equal(oracle.Clear ? Cover.SightBlockersBetween(from, to, left) : oracle.ObstructedBy, reading.Blockers);
        return reading;
    }

    /// <summary>A 72 × 48 table: terrain of every footprint, yaw and scale, some of it see-in-and-out or not blocking, and models of every base shape, some raised; all but the first five (the observers) also blocking bodies.</summary>
    static (List<WorldVolume> Volumes, List<(Guid Id, PlacedModel Model)> Models) Layout(Random rng)
    {
        double R(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        var volumes = new List<WorldVolume>();
        for (var i = rng.Next(3, 10); i > 0; i--)
        {
            var d = rng.Next(5) switch { 0 => Fx.ForestSight, 1 => TerrainDeclarations.Cover, _ => Fx.Blocks };
            var bottom = rng.Next(4) == 0 ? R(0, 3) : 0;
            var top = bottom + R(0.2, 8);
            var kind = rng.Next(2) == 0 ? VolumeKind.Solid : VolumeKind.Area;
            volumes.Add(rng.Next(2) == 0
                ? Fx.Box(R(0, 72), R(0, 48), R(0.3, 6), R(0.3, 6), bottom, top, kind, d, yaw: R(0, 360), scale: R(0.5, 2), z: rng.Next(5) == 0 ? R(0, 3) : 0)
                : Fx.Cylinder(R(0, 72), R(0, 48), R(0.3, 3), bottom, top, kind, d, scale: R(0.5, 2), z: rng.Next(5) == 0 ? R(0, 3) : 0));
        }
        var models = new List<(Guid, PlacedModel)>();
        for (var i = rng.Next(8, 40); i > 0; i--)
        {
            var b = rng.Next(3) switch { 0 => Base.Circle(R(0.8, 3)), 1 => Base.Oval(R(1, 4), R(0.8, 3)), _ => Base.Rect(R(1, 5), R(0.8, 4)) };
            var m = Fx.Model(b, R(0, 72), R(0, 48), z: rng.Next(6) == 0 ? R(0, 6) : 0, height: R(0.5, 4), yaw: R(0, 360));
            var id = Guid.CreateVersion7();
            models.Add((id, m));
            if (models.Count > 5) volumes.Add(Bodies.AsBlockingVolume(id, m));
        }
        return (volumes, models);
    }
}
