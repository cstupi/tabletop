using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class LowDetailTests
{
    [Fact] public void A_model_within_the_cap_keeps_its_full_detail() => Assert.Null(LowDetail.Ratio(LowDetail.TriangleCap));
    [Fact] public void A_model_over_the_cap_is_scaled_to_it() => Assert.Equal(0.25, LowDetail.Ratio(4 * LowDetail.TriangleCap));

    [Fact] public void The_first_level_within_the_surface_share_is_picked() =>
        Assert.Equal(1, LowDetail.PickLevel(150_000, [90_000, 36_000, 12_000], 0.25));
    [Fact] public void No_level_small_enough_takes_the_coarsest() =>
        Assert.Equal(1, LowDetail.PickLevel(150_000, [90_000, 60_000], 0.1));
    [Fact] public void A_surface_without_levels_keeps_its_base() => Assert.Null(LowDetail.PickLevel(150_000, [], 0.25));

    [Fact]
    public void Compacting_keeps_only_the_vertices_the_new_base_draws_and_remaps_every_list()
    {
        // Six vertices; the new base draws 1, 3, 5. The coarser level draws 3 and 5 (and 1), the dropped full level drew all six.
        var c = LowDetail.Compact([5, 3, 1, 1, 3, 5], [[3, 5, 1]]);
        Assert.Equal([5, 3, 1], c.Used);
        Assert.Equal([0, 1, 2, 2, 1, 0], c.Base);
        Assert.Equal([1, 0, 2], Assert.Single(c.Levels));
    }

    [Fact]
    public void A_coarser_level_drawing_a_vertex_the_base_does_not_keeps_it()
    {
        var c = LowDetail.Compact([0, 1, 2], [[0, 2, 4]]);
        Assert.Equal([0, 1, 2, 4], c.Used);
        Assert.Equal([0, 2, 3], c.Levels[0]);
    }

    [Fact]
    public void Gather_takes_each_used_vertex_s_components()
    {
        float[] tangents = [0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 1];   // three vertices, four floats each
        Assert.Equal([2f, 2, 2, 1, 0, 0, 0, 1], LowDetail.Gather(tangents, 4, [2, 0]));
    }
}
