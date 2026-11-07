using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RosterOrderTests
{
    static readonly Dictionary<Guid, int> NoMax = new();

    [Fact]
    public void Rows_sort_may_act_then_activated_then_off_the_table_and_the_acting_unit_is_marked()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"),
            new SequenceStarted(Guid.CreateVersion7(), "advance", new SequenceContext(TableUx.Squad.Id, [], [], "Advance")));
        var r = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, NoMax, [TableUx.T1]);
        Assert.Equal(new[] { "Squad", "Reserve" }, r[0].Rows.Select(x => x.Name));   // activated, then off the table
        Assert.Equal(new[] { true, false }, r[0].Rows.Select(x => x.Acting));
        Assert.Equal(new[] { true, false }, r.Select(t => t.Mine));
    }

    [Fact]
    public void A_unit_that_may_act_comes_before_one_that_has_whatever_the_army_order()
    {
        var placed = new ModelsMoved([new(TableUx.ModelOf(TableUx.Reserve), new OnShelf(), new OnTable(new Placement(new Point3(20, 10, 0), new Yaw(0))))]);
        var p = TableUx.After(TableUx.Deploy, placed, new ActionRecorded(TableUx.Squad.Id, "Hold"));
        var rows = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, NoMax)[0].Rows;
        Assert.Equal(new[] { "Reserve", "Squad" }, rows.Select(x => x.Name));
        Assert.Equal(new[] { RosterMark.MayAct, RosterMark.Activated }, rows.Select(x => x.Mark));
        var off = TableUx.After();
        Assert.All(RosterView.Of(off, TableUx.Game, new Names(TableUx.Game, off), TableUx.T1, NoMax)[0].Rows, x => Assert.Equal(RosterMark.OffTable, x.Mark));
    }

    [Fact]
    public void Rows_of_the_same_kind_sort_by_name()
    {
        var a = new RosterRow(Guid.NewGuid(), "Zeta", false, 1, 1, false, []);
        var b = new RosterRow(Guid.NewGuid(), "Alpha", false, 1, 1, false, []);
        var c = new RosterRow(Guid.NewGuid(), "Beta", true, 1, 1, false, []);
        Assert.Equal(new[] { "Alpha", "Zeta", "Beta" }, RosterView.Sorted([a, b, c]).Select(x => x.Name));
    }
}
