using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>The area a placement keeps out of, from the Ask's KeepAway: Orcs (team 2) at (40, 30) on a 1" base; Squad (team 1) at (10..14, 10); Reserve
/// (team 1) on the shelf, the unit being placed.</summary>
public class KeepAwayAreaTests
{
    static readonly KeepAway NineFromOrcs = new([new AwayFrom(TableUx.Orcs.Id, 9)]);
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    static ModelRelocation Move(Guid model, double x, double y) => new(model, new OnTable(At(40, 30)), new OnTable(At(x, y)));

    [Fact]
    public void The_area_is_each_listed_units_bases_grown_by_its_distance()
    {
        var ring = KeepAwayArea.Of(TableUx.After(TableUx.Deploy), NineFromOrcs)!;
        Assert.Equal(RingKind.KeepAway, ring.Kind);
        Assert.Equal("Too close 9\"", ring.Label);
        Assert.All(ring.Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.Equal(9.5, q.DistanceTo(new Point2(40, 30)), 3));
    }

    [Fact]
    public void Units_kept_at_different_distances_read_the_rule_s_own_the_shortest()
    {
        var keep = new KeepAway([new AwayFrom(TableUx.Orcs.Id, 12), new AwayFrom(TableUx.Squad.Id, 9)]);
        Assert.Equal("Too close 9\"", KeepAwayArea.Of(TableUx.After(TableUx.Deploy), keep)!.Label);
    }

    [Fact]
    public void No_listed_model_on_the_table_draws_nothing() => Assert.Null(KeepAwayArea.Of(TableUx.After(), NineFromOrcs));

    [Fact]
    public void The_area_follows_the_enemy_when_it_moves_and_its_stamp_changes()
    {
        var before = TableUx.After(TableUx.Deploy);
        var after = TableUx.After(TableUx.Deploy, new ModelsMoved([Move(TableUx.ModelOf(TableUx.Orcs), 50, 30)]));
        Assert.NotEqual(KeepAwayArea.Stamps(before, NineFromOrcs), KeepAwayArea.Stamps(after, NineFromOrcs));
        Assert.All(KeepAwayArea.Of(after, NineFromOrcs)!.Segments, s => Assert.Equal(9.5, s.A.DistanceTo(new Point2(50, 30)), 3));
        Assert.Equal(KeepAwayArea.Stamps(before, NineFromOrcs), KeepAwayArea.Stamps(TableUx.After(TableUx.Deploy), NineFromOrcs));
    }

    [Fact]
    public void A_ghost_within_the_distance_is_marked_and_one_over_it_is_not()
    {
        var p = TableUx.After(TableUx.Deploy);
        var ghost = TableUx.ModelOf(TableUx.Reserve);
        var near = KeepAwayArea.Breaches(p, NineFromOrcs, new Dictionary<Guid, Placement> { [ghost] = At(40, 21) }, _ => "Orcs");     // 8" apart edge to edge
        Assert.Equal("placed within 9\" of Orcs", near[ghost]);
        Assert.Equal("M1 would be placed within 9\" of Orcs", ContactCheck.Sentence(near[ghost], "M1"));
        Assert.Empty(KeepAwayArea.Breaches(p, NineFromOrcs, new Dictionary<Guid, Placement> { [ghost] = At(40, 19.9) }, _ => "Orcs"));   // 9.1"
    }

    [Fact]
    public void A_lifting_model_clears_a_ghost_within_its_reach_and_cuts_the_area()
    {
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new ModelRelocation(TableUx.ModelOf(TableUx.Reserve), new OnShelf(), new OnTable(At(40, 18)))]));
        var keep = NineFromOrcs with { IgnoredNear = [TableUx.ModelOf(TableUx.Reserve)], IgnoredWithin = 6 };
        var ghost = TableUx.ModelOf(TableUx.Squad);
        Assert.Empty(KeepAwayArea.Breaches(p, keep, new Dictionary<Guid, Placement> { [ghost] = At(40, 23) }, _ => "Orcs"));
        Assert.NotEqual(KeepAwayArea.Of(p, NineFromOrcs)!.Segments.Count, KeepAwayArea.Of(p, keep)!.Segments.Count);
    }

    [Fact]
    public void The_keep_away_is_the_deploy_picks_for_its_unit_else_the_open_move_Asks()
    {
        var mid = new MidDeploy(TableUx.Reserve.Id, AskQuestions.DeployAmbusher, [TableUx.ModelOf(TableUx.Reserve)], NineFromOrcs);
        Assert.Same(NineFromOrcs, KeepAwayArea.For(mid, null));
        var move = new Ask(Guid.CreateVersion7(), "move", "Vanguard", [TableUx.T1], new MoveRegion([TableUx.ModelOf(TableUx.Squad)], 9, [], [], null, KeepAway: NineFromOrcs), true, false, null, null);
        Assert.Same(NineFromOrcs, KeepAwayArea.For(null, move));
        Assert.Null(KeepAwayArea.For(null, null));
    }
}
