using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RosterViewTests
{
    [Fact]
    public void My_team_first_with_activation_summed_wounds_conditions_and_off_table_units()
    {
        var s = TableUx.Squad; var third = TableUx.ModelOf(s, 2);
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(s.Id, "Advance"), new WoundsSet(TableUx.ModelOf(s), 1), new ConditionSet(new Subject(SubjectKind.Unit, s.Id), "shaken", true),
            new ConditionSet(new Subject(SubjectKind.Unit, s.Id), "moved this round", true), new ModelsMoved([new(third, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))), new OnShelf())]), new WoundsSet(third, 1));
        var max = new Dictionary<Guid, int> { [TableUx.ModelOf(s)] = 3, [TableUx.ModelOf(s, 1)] = 3 };
        var r = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T2, max);
        Assert.Equal(new[] { "Team 2", "Team 1" }, r.Select(t => t.Name));
        Assert.Equal(("Squad", true, 5, 7, false, "shaken"), (r[1].Rows[0].Name, r[1].Rows[0].Activated, r[1].Rows[0].Left, r[1].Rows[0].Max, r[1].Rows[0].OffTable, r[1].Rows[0].Conditions.Single()));   // 2 + 3 + a removed model's 0; Bookkeeping's moved this round is left out
        Assert.Equal(("Reserve", true), (r[1].Rows[1].Name, r[1].Rows[1].OffTable));
        Assert.Equal((1, 1, false), (r[0].Rows[0].Left, r[0].Rows[0].Max, r[0].Rows[0].Activated));                  // a one-wound unit counts alive models
        Assert.Equal(("●●○", "Shaken"), (RosterView.Pips(2, 3), RosterView.Words("shaken")));
        Assert.Equal("Set aside", RosterView.Words("set-aside"));
    }
}

public class RosterDiffTests
{
    static RosterRow Row(Guid id, int left = 3, params string[] conditions) => new(id, "Squad", false, left, 3, false, conditions.ToList());
    static readonly Guid T = Guid.NewGuid(), A = Guid.NewGuid(), B = Guid.NewGuid();
    static (Guid, bool)[][] Flat(IReadOnlyList<IReadOnlyList<(Guid Unit, bool Keep)>>? kept) => kept!.Select(t => t.ToArray()).ToArray();

    [Fact]
    public void Rows_are_equal_by_value_conditions_included()
    {
        Assert.Equal(Row(A, 3, "shaken"), Row(A, 3, "shaken"));
        Assert.NotEqual(Row(A, 3, "shaken"), Row(A, 3));
        Assert.Equal(Row(A, 3, "shaken").GetHashCode(), Row(A, 3, "shaken").GetHashCode());
    }

    [Fact]
    public void A_render_keeps_each_row_whose_content_and_selection_stand_wherever_it_moved()
    {
        var before = new[] { new RosterTeam(T, "Team 1", 1, [Row(A), Row(B)]) };
        Assert.Equal(new[] { new[] { (A, true), (B, false) } }, Flat(RosterView.Keep(before, null, [new RosterTeam(T, "Team 1", 1, [Row(A), Row(B, 2)])], null)));   // B lost a wound
        Assert.Equal(new[] { new[] { (B, true), (A, true) } }, Flat(RosterView.Keep(before, null, [new RosterTeam(T, "Team 1", 1, [Row(B), Row(A)])], null)));       // a new order moves rows
        Assert.Equal(new[] { new[] { (A, false), (B, true) } }, Flat(RosterView.Keep(before, null, before, A)));                                                      // A selected: its edge changes
        Assert.Equal(new[] { new[] { (A, false), (B, false) } }, Flat(RosterView.Keep(before, A, before, B)));                                                        // the selection moved
        var c = Guid.NewGuid();
        Assert.Equal(new[] { new[] { (A, true), (c, false), (B, true) } }, Flat(RosterView.Keep(before, null, [new RosterTeam(T, "Team 1", 1, [Row(A), Row(c), Row(B)])], null)));
    }

    [Fact]
    public void A_new_team_line_or_no_earlier_list_rebuilds_the_whole_list()
    {
        var before = new[] { new RosterTeam(T, "Team 1", 1, [Row(A), Row(B)]) };
        Assert.Null(RosterView.Keep(before, null, [new RosterTeam(T, "Team 1 (you)", 1, [Row(A), Row(B)])], null));
        Assert.Null(RosterView.Keep(before, null, [new RosterTeam(T, "Team 1", 1, [Row(A), Row(B)], Mine: true)], null));
        Assert.Null(RosterView.Keep(before, null, [.. before, new RosterTeam(Guid.NewGuid(), "Team 2", 2, [])], null));
        Assert.Null(RosterView.Keep(null, null, before, null));
        Assert.Null(RosterView.Keep(before, null, [new RosterTeam(Guid.NewGuid(), "Team 1", 1, [Row(A), Row(B)])], null));
    }

    [Fact]
    public void Teams_in_a_new_order_keep_their_rows()
    {
        var u = Guid.NewGuid(); var second = new RosterTeam(Guid.NewGuid(), "Team 2", 2, [Row(u)], Mine: true);
        var before = new[] { new RosterTeam(T, "Team 1", 1, [Row(A), Row(B)], Mine: true), second };
        Assert.Equal(new[] { new[] { (u, true) }, new[] { (A, true), (B, true) } }, Flat(RosterView.Keep(before, null, [second, before[0]], null)));
    }
}
