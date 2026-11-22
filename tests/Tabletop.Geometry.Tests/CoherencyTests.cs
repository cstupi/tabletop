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

    static IReadOnlyList<CoherencyGap> Gaps(params PlacedModel[] models) => Coherency.Gaps(models, 1, 3, 9);

    [Fact] public void A_coherent_unit_has_no_gaps() => Assert.Empty(Gaps(Fx.Model(0, 0), Fx.Model(2, 0), Fx.Model(4, 0)));

    // The straggler's gap is to the nearest model of the chain, edge to edge, with the closest points of the two bases.
    [Fact] public void A_straggler_is_named_with_its_nearest_model_of_the_chain()
    {
        var gap = Assert.Single(Gaps(Fx.Model(0, 0), Fx.Model(2, 0), Fx.Model(6, 0)));
        Assert.Equal((2, 1, CoherencyBreak.Chain, 1.0), (gap.Model, gap.To, gap.Break, gap.Needed));
        Assert.Equal(2.74, gap.Distance, 6);
        Assert.Equal(new Point2(5.37, 0), gap.OnModel);
        Assert.Equal(new Point2(2.63, 0), gap.OnTo);
    }

    // p.7's 9": a model of the chain too far from another is named with the farthest one.
    [Fact] public void A_spread_break_is_named_with_the_farthest_model()
    {
        var chain = Enumerable.Range(0, 7).Select(i => Fx.Model(2 * i, 0)).ToArray();
        var gaps = Gaps(chain);
        Assert.Equal(new[] { (0, 6), (6, 0) }, gaps.Select(g => (g.Model, g.To)));
        Assert.All(gaps, g => { Assert.Equal(CoherencyBreak.Spread, g.Break); Assert.Equal(9.0, g.Needed); Assert.Equal(10.74, g.Distance, 6); });
    }

    // The owner's Flayer Cavalry (2026-11-22): 60 x 35 mm ovals turned to their own facings, measured on the oval edges at those facings.
    // Rider 1 stood 0.92" from rider 3: in coherency; moved 0.15" further out along the gap it is 1.07" from its nearest rider and out.
    [Fact] public void Turned_ovals_are_measured_on_their_own_edges()
    {
        var oval = Base.Oval(60 / 25.4, 35 / 25.4);
        PlacedModel R(double x, double y, double yaw) => Fx.Model(oval, x, y, yaw: yaw);
        PlacedModel[] Unit(double x1, double y1) => [R(39.839, 20.306, 12.185), R(x1, y1, 71.19), R(35.683, 19.208, 71.19), R(37.878, 19.779, 71.19), R(36.120, 21.084, 71.19)];
        Assert.Empty(Gaps(Unit(38.625, 17.348)));
        var gap = Assert.Single(Gaps(Unit(38.625 + 0.05, 17.348 - 0.15)));
        Assert.Equal((1, 3, CoherencyBreak.Chain), (gap.Model, gap.To, gap.Break));
        Assert.InRange(gap.Distance, 1.0, 1.1);
    }

    // A pair at different elevation chains at 3": the gap names the 3" it needs.
    [Fact] public void An_elevated_straggler_needs_three_inches() =>
        Assert.Equal(3.0, Assert.Single(Gaps(Fx.Model(0, 0), Fx.Model(5, 0, z: 2))).Needed);
}
