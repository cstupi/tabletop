using Tabletop.Client.Core;
using Tabletop.Geometry;
using static Tabletop.Client.Tests.MovePathsTests;

namespace Tabletop.Client.Tests;

/// <summary>MovePathCache: the open Ask's paths as MovePaths.Proposed gives them, a ghost that stayed put keeping the very path it had.</summary>
public class MovePathCacheTests
{
    static void Same(IReadOnlyList<ModelPath> expected, IReadOnlyList<ModelPath> actual)
    {
        Assert.Equal(expected.Select(p => p.ModelId), actual.Select(p => p.ModelId));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Route, actual[i].Route);
            Assert.Equal(expected[i].Footprints, actual[i].Footprints);
            Assert.Equal(expected[i].Dangerous, actual[i].Dangerous);
        }
    }

    [Fact]
    public void Its_paths_are_the_proposed_paths_and_a_ghost_that_stayed_keeps_its_path()
    {
        var regions = FlayerRush.Regions();
        var cache = new MovePathCache(regions, FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes);
        var first = cache.Proposed(FlayerRush.To);
        Same(MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, FlayerRush.To), first);
        // One ghost moves on: its path is swept again, every other one is the same instance.
        var moved = FlayerRush.Ids[1];
        var to = FlayerRush.To.ToDictionary(kv => kv.Key, kv => kv.Key == moved ? kv.Value + new Point2(-1, 0) : kv.Value);
        var second = cache.Proposed(to);
        Same(MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, to), second);
        foreach (var path in second) Assert.Equal(path.ModelId != moved, first.Any(p => ReferenceEquals(p, path)));
        // Capped, every path is the shortest again; back where it was, a ghost not moved has no path.
        Same(MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, to, capped: true), cache.Proposed(to, capped: true));
        var home = to.ToDictionary(kv => kv.Key, kv => kv.Key == moved ? FlayerRush.Starts[moved].Placement.Position.Horizontal : kv.Value);
        Assert.DoesNotContain(cache.Proposed(home), p => p.ModelId == moved);
    }
}
