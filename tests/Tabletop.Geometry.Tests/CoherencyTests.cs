namespace Tabletop.Geometry.Tests;

public class CoherencyTests
{
    static CoherencyResult Opr(params PlacedModel[] models) => Coherency.Check(models, 1, 3, 9);

    [Fact] public void A_single_model_is_coherent() => Assert.True(Opr(Fx.Model(0, 0)).Coherent);
    [Fact] public void No_models_are_coherent() => Assert.True(Coherency.Check([], 1, 3, 9).Coherent);
    [Fact] public void A_chain_within_one_inch_is_coherent()
    {
        var r = Opr(Fx.Model(0, 0), Fx.Model(2, 0), Fx.Model(4, 0));
        Assert.True(r.Coherent);
        Assert.Empty(r.OutOfCoherency);
    }
    [Fact] public void A_straggler_is_flagged()
    {
        var r = Opr(Fx.Model(0, 0), Fx.Model(2, 0), Fx.Model(6, 0));
        Assert.False(r.Coherent);
        Assert.Equal(new[] { 2 }, r.OutOfCoherency);
    }
    [Fact] public void Equal_groups_keep_the_earliest() => Assert.Equal(new[] { 2, 3 }, Opr(Fx.Model(0, 0), Fx.Model(2, 0), Fx.Model(10, 0), Fx.Model(12, 0)).OutOfCoherency);
    [Fact] public void A_long_chain_breaks_the_spread()
    {
        var chain = Enumerable.Range(0, 7).Select(i => Fx.Model(2 * i, 0)).ToArray();
        Assert.Equal(new[] { 0, 6 }, Opr(chain).OutOfCoherency);
    }
    [Fact] public void Exactly_one_inch_counts() => Assert.True(Opr(Fx.Model(0, 0), Fx.Model(2.26, 0)).Coherent);

    // p.11: a pair at different elevation chains at 3" instead of 1". Measured horizontally, as every
    // distance here is: 4" apart on 1.26" bases is 2.74" edge to edge, which is over 1" and under 3".
    [Fact] public void A_pair_at_different_elevation_chains_at_three_inches() =>
        Assert.True(Opr(Fx.Model(0, 0), Fx.Model(4, 0, z: 2)).Coherent);
    [Fact] public void The_same_pair_at_one_elevation_is_flagged() =>
        Assert.Equal(new[] { 1 }, Opr(Fx.Model(0, 0), Fx.Model(4, 0)).OutOfCoherency);
    // Alternating elevation chains every adjacent pair at 3", so the unit is one body; p.7's 9" spread
    // has no elevation clause, so the ends are still flagged at 10.74" apart.
    [Fact] public void The_allowance_does_not_widen_the_spread() =>
        Assert.Equal(new[] { 0, 4 }, Opr(Fx.Model(0, 0), Fx.Model(3, 0, z: 2), Fx.Model(6, 0), Fx.Model(9, 0, z: 2), Fx.Model(12, 0)).OutOfCoherency);
}
