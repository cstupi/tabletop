using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>Passengers spec: the roster's Put in transport / Take out of transport, the roster's mark, the deploy pick, and Board transport in play.</summary>
public class PassengerMenuTests
{
    static ModelSlot M(int ordinal = 1) => new(Guid.CreateVersion7(), ordinal, "M", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, []);
    static Unit U(string name, params ModelSlot[] models) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [], models);
    static readonly Unit Rhino = U("Rhino", M()), Squad = U("Squad", M(1), M(2)), Theirs = U("Theirs", M());
    static ArmyInPlay Of(Guid participant, params Unit[] units) => new(participant, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", units), Guid.CreateVersion7()));
    static readonly Board Board = new(Tabletop.Content.Tests.TableStateTests.Sample(), [Of(TableUx.P1, Rhino, Squad), Of(TableUx.P2, Theirs)]);
    static Projection After(params EventPayload[] history) { var a = new EventApplier(Board, TableUx.Game); a.ApplyAll(TableUx.Events(history)); return a.Projection; }
    static Guid Hull => Rhino.Models[0].Id;
    static readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Facts = new Dictionary<Guid, IReadOnlyList<Guid>> { [Squad.Id] = [Rhino.Models[0].Id] };
    static ModelsMoved In() => new(Squad.Models.Select(m => new ModelRelocation(m.Id, new OnShelf(), new Inside(Hull))).ToList());
    static ModelRelocation OnTable(Guid model) => new(model, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0))));

    [Fact]
    public void A_shelved_unit_is_offered_each_Transport_the_server_named()
    {
        var p = After();
        var item = MenuModel.ForRosterUnit(TableUx.Game, p, Squad.Id, TableUx.T1, Facts).Single();
        Assert.Equal("Put in transport: Rhino", item.Label);
        Assert.Equal(In().Moves, Assert.IsType<MoveModels>(item.Build(null)).Moves);
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, p, Squad.Id, TableUx.T1));                 // no facts, no offer
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, p, Theirs.Id, TableUx.T1, new Dictionary<Guid, IReadOnlyList<Guid>> { [Theirs.Id] = [Hull] }));   // not mine
    }

    [Fact]
    public void A_unit_inside_a_Transport_on_the_shelf_may_be_taken_out()
    {
        var p = After(In());
        var item = MenuModel.ForRosterUnit(TableUx.Game, p, Squad.Id, TableUx.T1, Facts).Single();
        Assert.Equal("Take out of transport", item.Label);
        Assert.Equal(Squad.Models.Select(m => new ModelRelocation(m.Id, new Inside(Hull), new OnShelf())), Assert.IsType<MoveModels>(item.Build(null)).Moves);
        Assert.Empty(MenuModel.ForRosterUnit(TableUx.Game, After(In(), new ModelsMoved([OnTable(Hull)])), Squad.Id, TableUx.T1, Facts));   // the Transport deployed
    }

    [Fact]
    public void The_roster_marks_a_passenger_and_dims_it_while_its_Transport_is_off_the_table()
    {
        var max = new Dictionary<Guid, int>();
        RosterRow Row(Projection p) => RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, max)[0].Rows.Single(r => r.UnitId == Squad.Id);
        Assert.Equal(("Rhino", true), (Row(After(In())).Inside, Row(After(In())).OffTable));
        var deployed = After(In(), new ModelsMoved([OnTable(Hull)]));
        Assert.Equal(("Rhino", false), (Row(deployed).Inside, Row(deployed).OffTable));
        Assert.Null(Row(After()).Inside);
    }

    [Fact]
    public void A_deploy_pick_lists_only_units_with_a_model_on_the_shelf()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Deploy, "Deploy one unit", [TableUx.T1], new PieceSelection([Rhino.Id, Squad.Id], 1, 1), false, false, null, null);
        Assert.Equal([Rhino.Id], DeployView.Pickable(After(In()), ask));
        Assert.Equal([Rhino.Id, Squad.Id], DeployView.Pickable(After(), ask));
        var target = ask with { Question = "pick_target" };
        Assert.Equal([Rhino.Id, Squad.Id], DeployView.Pickable(After(In()), target));            // only a deploy pick places from the shelf
    }

    [Fact]
    public void Board_transport_answers_my_move_Ask_with_every_model_inside()
    {
        var p = After(new ModelsMoved([OnTable(Hull), .. Squad.Models.Select(m => OnTable(m.Id))]));
        var region = new MoveRegion(Squad.Models.Select(m => m.Id).ToList(), 6, [], [], null, new MoveContainment([Hull], null, null));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [TableUx.T1], region, false, false, null, Guid.CreateVersion7());
        var item = MenuModel.ForBoarding(p, ask, Squad.Models[1].Id).Single();
        Assert.Equal("Board transport: Rhino", item.Label);
        var answer = Assert.IsType<AnswerAsk>(item.Build(null));
        Assert.Equal(ask.Id, answer.AskId);
        Assert.Equal(Squad.Models.Select(m => new ModelRelocation(m.Id, p.Models[m.Id].Location, new Inside(Hull))), Assert.IsType<Destinations>(answer.Answer).Moves);
        Assert.Empty(MenuModel.ForBoarding(p, ask, Hull));                                          // not a moving model
        Assert.Empty(MenuModel.ForBoarding(p, ask with { Legal = region with { Containment = null } }, Squad.Models[0].Id));
        Assert.Empty(MenuModel.ForBoarding(p, null, Squad.Models[0].Id));
    }
}
