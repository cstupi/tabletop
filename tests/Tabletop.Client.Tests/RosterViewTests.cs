using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RosterViewTests
{
    [Fact]
    public void My_team_first_with_activation_summed_wounds_conditions_and_off_table_units()
    {
        var s = TableUx.Squad; var third = TableUx.ModelOf(s, 2);
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(s.Id, "Advance"), new WoundsSet(TableUx.ModelOf(s), 1), new ConditionSet(new Subject(SubjectKind.Unit, s.Id), "shaken", true),
            new ModelsMoved([new(third, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))), new OnShelf())]), new WoundsSet(third, 1));
        var max = new Dictionary<Guid, int> { [TableUx.ModelOf(s)] = 3, [TableUx.ModelOf(s, 1)] = 3 };
        var r = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T2, max);
        Assert.Equal(new[] { "Team 2", "Team 1" }, r.Select(t => t.Name));
        Assert.Equal(("Squad", true, 5, 7, false, "shaken"), (r[1].Rows[0].Name, r[1].Rows[0].Activated, r[1].Rows[0].Left, r[1].Rows[0].Max, r[1].Rows[0].OffTable, r[1].Rows[0].Conditions.Single()));   // 2 + 3 + a removed model's 0
        Assert.Equal(("Reserve", true), (r[1].Rows[1].Name, r[1].Rows[1].OffTable));
        Assert.Equal((1, 1, false), (r[0].Rows[0].Left, r[0].Rows[0].Max, r[0].Rows[0].Activated));                  // a one-wound unit counts alive models
        Assert.Equal(("●●○", "Shaken"), (RosterView.Pips(2, 3), RosterView.Words("shaken")));
    }
}
