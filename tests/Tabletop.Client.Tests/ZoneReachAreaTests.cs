using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>Where a deploy pick's unit may stand, from its PieceSelection.ZoneReach: the placing team's zone grown by the reach (p.14 Scout "fully within
/// 12” of their deployment zone", 3.6.0 p.15 9"). The sample table is 72 x 48, frontline: Team 1 holds y 36–48. Reserve (team 1, a 1" base) is being placed.</summary>
public class ZoneReachAreaTests
{
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    static MidDeploy Scout(double? reach) => new(TableUx.Reserve.Id, AskQuestions.DeployScouts, [TableUx.ModelOf(TableUx.Reserve)], null, reach);

    [Fact]
    public void The_area_is_the_team_s_zone_grown_by_the_reach_and_labelled_with_it()
    {
        var ring = ZoneReachArea.Of(TableUx.After(), TableUx.Game, Scout(9), ZoneLegs.Default)!;
        Assert.Equal(RingKind.Reach, ring.Kind);
        Assert.Equal("Scout 9\"", ring.Label);
        Assert.Equal(27, ring.Segments.Min(s => Math.Min(s.A.Y, s.B.Y)), 6);
        Assert.Equal(72 * 21, ring.Segments.Sum(s => s.A.Cross(s.B)) / 2, 3);
    }

    [Fact]
    public void A_pick_with_no_reach_draws_nothing() => Assert.Null(ZoneReachArea.Of(TableUx.After(), TableUx.Game, Scout(null), ZoneLegs.Default));

    [Theory]
    [InlineData(9.0, 27.6, false)]   // the base's near edge 27.1: inside
    [InlineData(9.0, 27.4, true)]    // 26.9: 0.1" beyond
    [InlineData(12.0, 24.4, true)]
    [InlineData(12.0, 24.6, false)]
    public void A_ghost_with_any_of_its_base_beyond_the_reach_is_marked(double reach, double y, bool marked)
    {
        var ghost = TableUx.ModelOf(TableUx.Reserve);
        var found = ZoneReachArea.Breaches(TableUx.After(), TableUx.Game, Scout(reach), ZoneLegs.Default, new Dictionary<Guid, Placement> { [ghost] = At(20, y) });
        Assert.Equal(marked, found.ContainsKey(ghost));
        if (marked)
        {
            Assert.Equal($"placed beyond {reach}\" of its deployment zone", found[ghost]);
            Assert.Equal($"Beyond {reach}\"", MarkWords.Short(found[ghost]));
        }
    }

    [Fact]
    public void Zero_reach_marks_a_ghost_outside_the_zone()
    {
        var ghost = TableUx.ModelOf(TableUx.Reserve);
        var found = ZoneReachArea.Breaches(TableUx.After(), TableUx.Game, Scout(0), ZoneLegs.Default, new Dictionary<Guid, Placement> { [ghost] = At(20, 35.8) });
        Assert.Equal("placed outside its deployment zone", found[ghost]);
        Assert.Equal("Outside its zone", MarkWords.Short(found[ghost]));
        Assert.Equal("Your zone", ZoneReachArea.Of(TableUx.After(), TableUx.Game, Scout(0), ZoneLegs.Default)!.Label);
    }

    [Fact]
    public void Contact_words_come_first()
    {
        var ghost = TableUx.ModelOf(TableUx.Reserve);
        var proposed = new Dictionary<Guid, Placement> { [ghost] = At(20, 20) };
        var words = ZoneReachArea.With(new Dictionary<Guid, string> { [ghost] = "overlaps a base" }, TableUx.After(), TableUx.Game, Scout(9), ZoneLegs.Default, proposed);
        Assert.Equal("overlaps a base", words[ghost]);
        Assert.Equal("placed beyond 9\" of its deployment zone", ZoneReachArea.With(new Dictionary<Guid, string>(), TableUx.After(), TableUx.Game, Scout(9), ZoneLegs.Default, proposed)[ghost]);
    }
}
