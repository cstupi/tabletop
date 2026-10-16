using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class RosterMenuTests
{
    static ModelSlot M() => new(Guid.CreateVersion7(), 1, "M", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, []);
    static Unit U(string name, params PropertyInstance[] props) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), props, [M()]);
    static readonly Unit Ambushers = U("Ambushers", new PropertyInstance("Ambush", null)), Scouts = U("Scouts", new PropertyInstance("Scout", null)), Line = U("Line"),
        Theirs = U("Theirs", new PropertyInstance("Ambush", null));
    static ArmyInPlay Of(Guid participant, params Unit[] units) => new(participant, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", units), Guid.CreateVersion7()));
    static readonly Board Board = new(Tabletop.Content.Tests.TableStateTests.Sample(), [Of(TableUx.P1, Ambushers, Scouts, Line), Of(TableUx.P2, Theirs)]);
    static Projection After(GameInfo game, params EventPayload[] history) { var a = new EventApplier(Board, game); a.ApplyAll(TableUx.Events(history)); return a.Projection; }
    static Subject S(Unit u) => new(SubjectKind.Unit, u.Id);

    [Fact]
    public void My_shelved_ambush_or_scout_unit_can_be_set_aside_and_returned()
    {
        var p = After(TableUx.Game);
        var item = MenuModel.ForRosterUnit(TableUx.Game, p, Ambushers.Id, TableUx.T1).Single();
        Assert.Equal(("Set aside", new SetCondition(S(Ambushers), "set-aside", true)), (item.Label, item.Build(null)));
        Assert.Equal("Set aside", MenuModel.ForRosterUnit(TableUx.Game, p, Scouts.Id, TableUx.T1).Single().Label);
        var aside = After(TableUx.Game, new ConditionSet(S(Ambushers), "set-aside", true));
        var back = MenuModel.ForRosterUnit(TableUx.Game, aside, Ambushers.Id, TableUx.T1).Single();
        Assert.Equal(("Return to deployment", new SetCondition(S(Ambushers), "set-aside", false)), (back.Label, back.Build(null)));
    }

    [Fact]
    public void Nothing_for_other_units_other_teams_a_unit_on_the_table_a_destroyed_one_or_outside_play()
    {
        var p = After(TableUx.Game);
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, p, Line.Id, TableUx.T1));
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, p, Theirs.Id, TableUx.T1));
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, p, Ambushers.Id, null));
        var placed = After(TableUx.Game, new ModelsMoved([new(Ambushers.Models[0].Id, new OnShelf(), new OnTable(new Placement(new Point3(5, 5, 0), new Yaw(0))))]));
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, placed, Ambushers.Id, TableUx.T1));
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, After(TableUx.Game, new WoundsSet(Ambushers.Models[0].Id, 1)), Ambushers.Id, TableUx.T1));   // destroyed
        var ended = TableUx.Game with { Status = GameStatus.Ended };
        Assert.Empty(MenuModel.ForRosterUnit(ended, After(ended), Ambushers.Id, TableUx.T1));
    }
}
