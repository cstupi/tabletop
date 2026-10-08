using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class DeployViewTests
{
    static readonly GameInfo Game = Samples.Game;
    static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Samples.Board, Game); a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null))); return a; }
    static Ask DeployAsk(string question = AskQuestions.Deploy, Guid? team = null) =>
        new(Guid.CreateVersion7(), question, "Deploy one unit", [team ?? Samples.Team1], new PieceSelection([Samples.Unit], 1, 1), false, false, null, null);
    static AskClosed Closed(Ask a, AskAnswer answer, Guid? team = null) => new(a.Id, [new TeamAnswer(team ?? Samples.Team1, answer)]);
    static readonly ModelRelocation Placed = new(Samples.Model, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0))));

    [Fact]
    public void A_unit_picked_at_deploy_with_models_still_on_the_shelf_is_mid_deploy()
    {
        var ask = DeployAsk();
        var a = Applier(new AskOpened(ask), new AskAnswered(ask.Id, Samples.Team1, new SelectedPieces([Samples.Unit]), false), Closed(ask, new SelectedPieces([Samples.Unit])));
        var mid = DeployView.MidDeploy(a.Projection, a.Log, Samples.Team1)!;
        Assert.Equal(Samples.Unit, mid.UnitId);
        Assert.Equal([Samples.Model], mid.ShelvedModelIds);
        Assert.Equal(AskQuestions.Deploy, mid.Question);
    }

    [Theory]
    [InlineData(AskQuestions.DeployScouts)]
    [InlineData(AskQuestions.DeployAmbusher)]
    public void Scouts_and_ambushers_deploy_the_same_way(string question)
    {
        var ask = DeployAsk(question);
        var a = Applier(new AskOpened(ask), Closed(ask, new SelectedPieces([Samples.Unit])));
        Assert.Equal(Samples.Unit, DeployView.MidDeploy(a.Projection, a.Log, Samples.Team1)!.UnitId);
    }

    [Fact]
    public void Nothing_is_mid_deploy_once_every_model_is_placed_or_while_the_Ask_is_open_or_for_the_other_team_or_after_a_pass_or_a_later_Ask()
    {
        var ask = DeployAsk();
        var picked = new EventPayload[] { new AskOpened(ask), Closed(ask, new SelectedPieces([Samples.Unit])) };
        var done = Applier([.. picked, new ModelsMoved([Placed])]);
        Assert.Null(DeployView.MidDeploy(done.Projection, done.Log, Samples.Team1));
        var open = Applier(new AskOpened(ask));
        Assert.Null(DeployView.MidDeploy(open.Projection, open.Log, Samples.Team1));
        var open2 = Applier(picked);
        Assert.Null(DeployView.MidDeploy(open2.Projection, open2.Log, Samples.Team2));
        Assert.Null(DeployView.MidDeploy(open2.Projection, open2.Log, null));
        var passed = Applier(new AskOpened(ask), Closed(ask, new Passed()));
        Assert.Null(DeployView.MidDeploy(passed.Projection, passed.Log, Samples.Team1));
        var activate = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [Samples.Team2], new Options([]), true, false, null, null);
        var later = Applier([.. picked, new AskOpened(activate)]);
        Assert.Null(DeployView.MidDeploy(later.Projection, later.Log, Samples.Team1));
    }

    static ModelInPlay Model(int ordinal, Base b) => new(Guid.CreateVersion7(), Samples.Unit, new ModelSlot(Guid.CreateVersion7(), ordinal, $"m{ordinal}", new Dictionary<string, string>(), [], b, 1, null, []));

    [Fact]
    public void Models_lay_out_in_a_row_centred_on_the_point_spaced_by_the_widest_base()
    {
        var models = new[] { Model(1, Base.Circle(1)), Model(2, Base.Circle(2)), Model(3, Base.Circle(1)) };
        var moves = DeployView.Layout(models, new Point3(36, 24, 0), new TableSize(72, 48));
        Assert.Equal(models.Select(m => m.Id), moves.Select(m => m.ModelId));
        Assert.All(moves, m => Assert.IsType<OnShelf>(m.From));
        var at = moves.Select(m => ((OnTable)m.To).Placement).ToList();
        Assert.All(at, p => Assert.Equal(0, p.Yaw.Degrees));
        Assert.All(at, p => Assert.Equal(24, p.Position.Y));
        var pitch = at[1].Position.X - at[0].Position.X;
        Assert.True(pitch > 2 && pitch < 2.6);                               // the 2" base plus a small gap
        Assert.Equal(at[1].Position.X - at[0].Position.X, at[2].Position.X - at[1].Position.X, 9);
        Assert.Equal(36, at[1].Position.X, 9);                               // centred
    }

    [Fact]
    public void A_large_unit_wraps_to_rows_of_five_and_stays_on_the_table()
    {
        var models = Enumerable.Range(1, 12).Select(i => Model(i, Base.Circle(1))).ToList();
        var at = DeployView.Layout(models, new Point3(0.5, 0.5, 0), new TableSize(72, 48)).Select(m => ((OnTable)m.To).Placement.Position).ToList();
        Assert.Equal(3, at.Select(p => p.Y).Distinct().Count());
        Assert.Equal(5, at.Select(p => p.X).Distinct().Count());
        Assert.All(at, p => { Assert.InRange(p.X, 0.5, 71.5); Assert.InRange(p.Y, 0.5, 47.5); });
        Assert.Equal(12, at.Distinct().Count());
    }

    [Fact]
    public void Pieces_with_no_node_are_the_eligible_units_and_models_not_on_the_table()
    {
        var shelved = Applier();
        Assert.Equal([Samples.Unit], DeployView.OffTable(shelved.Projection, [Samples.Unit]));
        Assert.Equal([Samples.Model], DeployView.OffTable(shelved.Projection, [Samples.Model]));
        var onTable = Applier(new ModelsMoved([Placed]));
        Assert.Empty(DeployView.OffTable(onTable.Projection, [Samples.Unit, Samples.Model]));
    }
}
