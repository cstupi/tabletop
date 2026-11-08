using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Which piece the pointer picks: a model under it, else a model within the slop of it, else the nearest other piece hit.</summary>
public class TablePickTests
{
    [Fact]
    public void A_model_hit_beats_terrain_hit_first()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 0)], slop: 12));
    }

    [Fact]
    public void A_model_the_pointer_just_misses_beats_the_terrain_it_is_over()
    {
        Assert.Equal(2, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 20), new(HitKind.Model, 15, 6)], slop: 12));
        Assert.Equal(0, TablePick.Best([new(HitKind.Other, 10, 0), new(HitKind.Model, 14, 20)], slop: 12));   // too far: the terrain
    }

    [Fact]
    public void Among_models_one_under_the_pointer_beats_one_beside_it_and_the_nearer_hit_wins()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Model, 10, 3), new(HitKind.Model, 20, 0)], slop: 12));
        Assert.Equal(1, TablePick.Best([new(HitKind.Model, 20, 0), new(HitKind.Model, 12, 0)], slop: 12));
    }

    [Fact]
    public void Without_a_model_the_nearest_other_hit_wins_and_a_miss_is_none()
    {
        Assert.Equal(1, TablePick.Best([new(HitKind.Other, 20, 0), new(HitKind.Other, 12, 0), new(HitKind.Other, 5, 4)], slop: 12));
        Assert.Null(TablePick.Best([new(HitKind.Other, 5, 4)], slop: 12));
        Assert.Null(TablePick.Best([], slop: 12));
    }
}
