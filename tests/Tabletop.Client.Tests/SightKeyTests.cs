using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class SightKeyTests
{
    static readonly Guid Observer = TableUx.Squad.Id, Target = TableUx.Orcs.Id;
    static ModelRelocation Move(Guid model, Location from, double x, double y) => new(model, from, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    static Location At(Projection p, Guid model) => p.Models[model].Location;

    [Fact]
    public void Same_placements_give_the_same_key()
    {
        var a = SightKey.Of(TableUx.After(TableUx.Deploy), Observer, Target);
        var b = SightKey.Of(TableUx.After(TableUx.Deploy), Observer, Target);
        Assert.Equal(a, b);
    }

    [Fact]
    public void An_observer_or_target_model_moving_changes_the_key()
    {
        var p = TableUx.After(TableUx.Deploy);
        var before = SightKey.Of(p, Observer, Target);
        var rifle = TableUx.ModelOf(TableUx.Squad); var orc = TableUx.ModelOf(TableUx.Orcs);
        Assert.NotEqual(before, SightKey.Of(TableUx.After(TableUx.Deploy, new ModelsMoved([Move(rifle, At(p, rifle), 11, 10)])), Observer, Target));
        Assert.NotEqual(before, SightKey.Of(TableUx.After(TableUx.Deploy, new ModelsMoved([Move(orc, At(p, orc), 40, 31)])), Observer, Target));
    }

    [Fact]
    public void A_model_of_another_unit_moving_leaves_the_key()
    {
        var p = TableUx.After(TableUx.Deploy);
        var reserve = TableUx.ModelOf(TableUx.Reserve);
        var moved = TableUx.After(TableUx.Deploy, new ModelsMoved([Move(reserve, At(p, reserve), 20, 20)]));
        Assert.Equal(SightKey.Of(p, Observer, Target), SightKey.Of(moved, Observer, Target));
    }

    [Fact]
    public void A_model_leaving_the_table_changes_the_key()
    {
        var p = TableUx.After(TableUx.Deploy);
        var orc = TableUx.ModelOf(TableUx.Orcs);
        var gone = TableUx.After(TableUx.Deploy, new ModelsMoved([new ModelRelocation(orc, At(p, orc), new OnShelf())]));
        Assert.NotEqual(SightKey.Of(p, Observer, Target), SightKey.Of(gone, Observer, Target));
    }

    [Fact]
    public void Terrain_changing_changes_the_key()
    {
        var p = TableUx.After(TableUx.Deploy);
        var t = TableUx.Board.Table;
        var shifted = TableUx.Board with { Table = t with { Terrain = [t.Terrain[0] with { Position = new Point3(13, 12, 0) }] } };
        var q = new EventApplier(shifted, TableUx.Game); q.ApplyAll(TableUx.Events(TableUx.Deploy));
        Assert.NotEqual(SightKey.Of(p, Observer, Target), SightKey.Of(q.Projection, Observer, Target));
        var same = new EventApplier(TableUx.Board with { Table = t with { } }, TableUx.Game); same.ApplyAll(TableUx.Events(TableUx.Deploy));
        Assert.Equal(SightKey.Of(p, Observer, Target), SightKey.Of(same.Projection, Observer, Target));
    }

    [Fact]
    public void The_pair_is_part_of_the_key()
    {
        var p = TableUx.After(TableUx.Deploy);
        Assert.NotEqual(SightKey.Of(p, Observer, Target), SightKey.Of(p, TableUx.Reserve.Id, Target));
    }

    [Fact]
    public void Cache_asks_a_new_pair_once_then_only_after_a_change_and_drops_unassigned_pairs()
    {
        var cache = new SightCache();
        var p = TableUx.After(TableUx.Deploy);
        Assert.True(cache.NeedsAsk(Target, SightKey.Of(p, Observer, Target)));                // new pair
        cache.Asked(Target, SightKey.Of(p, Observer, Target));
        Assert.False(cache.NeedsAsk(Target, SightKey.Of(p, Observer, Target)));              // nothing moved
        var rifle = TableUx.ModelOf(TableUx.Squad);
        var moved = TableUx.After(TableUx.Deploy, new ModelsMoved([Move(rifle, At(p, rifle), 11, 10)]));
        Assert.True(cache.NeedsAsk(Target, SightKey.Of(moved, Observer, Target)));
        cache.Keep([]);                                                                       // no longer assigned
        Assert.True(cache.NeedsAsk(Target, SightKey.Of(p, Observer, Target)));
    }
}
