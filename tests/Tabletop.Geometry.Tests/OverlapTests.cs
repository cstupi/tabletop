namespace Tabletop.Geometry.Tests;

public class OverlapTests
{
    [Fact] public void Overlapping_bases() => Assert.True(Overlap.Bases(Fx.Model(0, 0), Fx.Model(1, 0)));
    [Fact] public void Touching_bases_do_not_overlap() => Assert.False(Overlap.Bases(Fx.Model(0, 0), Fx.Model(1.26, 0)));
    [Fact] public void Separate_bases_do_not_overlap() => Assert.False(Overlap.Bases(Fx.Model(0, 0), Fx.Model(2, 0)));
    [Fact] public void Bases_on_different_storeys_do_not_overlap() => Assert.False(Overlap.Bases(Fx.Model(0, 0, 0), Fx.Model(0, 0, 4)));
    [Fact] public void Rect_and_circle_overlap() => Assert.True(Overlap.Bases(Fx.Model(Base.Rect(2, 2), 0, 0), Fx.Model(1.4, 0)));
    [Fact] public void A_model_inside_a_wall_intersects_it() => Assert.True(Overlap.WithSolid(Fx.Model(0, 0), Fx.Box(0, 0, 1, 6, 0, 4)));
    [Fact] public void Standing_on_a_hill_is_not_intersecting_it() => Assert.False(Overlap.WithSolid(Fx.Model(0, 0, 2), Fx.Box(0, 0, 6, 6, 0, 2)));
    [Fact] public void Under_a_slab_is_not_intersecting_it() => Assert.False(Overlap.WithSolid(Fx.Model(0, 0, 0), Fx.Box(0, 0, 6, 6, 4, 8)));
    [Fact] public void A_tall_model_reaches_a_slab() => Assert.True(Overlap.WithSolid(Fx.Model(0, 0, 0, height: 5), Fx.Box(0, 0, 6, 6, 4, 8)));
    [Fact] public void Area_volumes_never_intersect() => Assert.False(Overlap.WithSolid(Fx.Model(0, 0), Fx.Cylinder(0, 0, 4, 0, 8, VolumeKind.Area)));
}
