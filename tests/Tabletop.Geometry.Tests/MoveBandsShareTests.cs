namespace Tabletop.Geometry.Tests;

/// <summary>
/// An area walked in shares (MoveReach.Bands with a share: the coarse cells c ≡ part mod parts, which the browser walks on several workers) is the
/// whole area: every share's triangles together are Bands' triangles, each band, point for point, in the same order within each coarse cell.
/// </summary>
public class MoveBandsShareTests
{
    static MoveReach Rider(int i)
    {
        var difficult = CavalryRushScene.DifficultGround.Skip(1).Concat(Movement.SteepEntering(CavalryRushScene.Riders[i], CavalryRushScene.Hills)).ToList();
        return MoveReach.Build(CavalryRushScene.Riders[i], 16, [CavalryRushScene.Building], difficult, 6, CavalryRushScene.Table, CavalryRushScene.Others,
            CavalryRushScene.Volumes, [CavalryRushScene.DifficultGround[0]], [CavalryRushScene.Lairs[1]], 6);
    }

    static List<(Point2, Point2, Point2)> Triangles(IReadOnlyList<Point2> t) =>
        Enumerable.Range(0, t.Count / 3).Select(i => (t[3 * i], t[3 * i + 1], t[3 * i + 2])).OrderBy(x => x.Item1.X).ThenBy(x => x.Item1.Y)
            .ThenBy(x => x.Item2.X).ThenBy(x => x.Item2.Y).ThenBy(x => x.Item3.X).ThenBy(x => x.Item3.Y).ToList();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 3)]
    [InlineData(2, 4)]
    public void The_shares_together_are_the_whole_area(int rider, int parts)
    {
        var reach = Rider(rider);
        var whole = reach.Bands();
        var shares = Enumerable.Range(0, parts).Select(part => reach.Bands(null, part, parts)).ToList();
        Assert.Equal(Triangles(whole.Clear), Triangles(shares.SelectMany(s => s.Clear).ToList()));
        Assert.Equal(Triangles(whole.Difficult), Triangles(shares.SelectMany(s => s.Difficult).ToList()));
        Assert.Equal(Triangles(whole.Dangerous), Triangles(shares.SelectMany(s => s.Dangerous).ToList()));
        if (parts > 1) Assert.All(shares, s => Assert.True(s.Clear.Count < whole.Clear.Count));
    }

    [Fact]
    public void A_minimum_cuts_each_share_as_it_cuts_the_whole()
    {
        var reach = Rider(1);
        var origin = CavalryRushScene.Riders[1].Placement.Position.Horizontal;
        Func<Point2, double> least = _ => 5;
        var whole = reach.Bands(least);
        var shares = Enumerable.Range(0, 3).Select(part => reach.Bands(least, part, 3)).ToList();
        Assert.Equal(Triangles(whole.Clear), Triangles(shares.SelectMany(s => s.Clear).ToList()));
        Assert.DoesNotContain(whole.Clear, p => p.DistanceTo(origin) < 4.5);
    }

    [Fact]
    public void A_share_out_of_range_is_refused()
    {
        var reach = Rider(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => reach.Bands(null, 3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => reach.Bands(null, 0, 0));
    }
}
