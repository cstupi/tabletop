using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class AskAnswersTests
{
    static readonly Guid AskId = Guid.CreateVersion7();
    static Ask AskOf(LegalSet legal) => new(AskId, AskQuestions.Deploy, "x", [TableUx.T1], legal, false, false, null, null);
    static readonly Guid M0 = TableUx.ModelOf(TableUx.Squad), M1 = TableUx.ModelOf(TableUx.Squad, 1), M2 = TableUx.ModelOf(TableUx.Squad, 2);
    static Projection P => TableUx.After(TableUx.Deploy);
    static MoveRegion Region(params Guid[] ids) => new(ids, 6, [], [], null);
    static ModelBody Body(Projection p, Guid id) { var m = p.Models[id]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

    [Fact]
    public void Answer_names_the_team_it_was_rendered_for()
    {
        var ask = AskOf(new Options([]));
        Assert.Equal(new AnswerAsk(ask.Id, new Chosen("hold"), TableUx.T2), AskAnswers.Answer(ask, new Chosen("hold"), TableUx.T2));
    }

    [Fact]
    public void Resolve_names_the_clicked_id_its_unit_or_the_sets_model_of_the_unit()
    {
        var p = P;
        Assert.Equal(M0, AskAnswers.Resolve(p, M0, [M0]));
        Assert.Equal(TableUx.Squad.Id, AskAnswers.Resolve(p, M0, [TableUx.Squad.Id]));
        Assert.Equal(M0, AskAnswers.Resolve(p, TableUx.Squad.Id, [M0]));
        Assert.Null(AskAnswers.Resolve(p, TableUx.ModelOf(TableUx.Orcs), [M0]));
    }

    [Fact]
    public void Click_on_a_piece_one_option_names_chooses_it()
    {
        var options = new Options([new AskOption("a", "A", [TableUx.Squad.Id]), new AskOption("b", "B", [TableUx.Orcs.Id])]);
        Assert.Equal(new Chosen("b"), AskAnswers.ChosenByClick(options, P, TableUx.ModelOf(TableUx.Orcs)));
    }

    [Fact]
    public void Click_on_a_piece_two_options_name_chooses_nothing()
    {
        var both = new Options([new AskOption("a", "A", [TableUx.Squad.Id]), new AskOption("b", "B", [M0])]);
        Assert.Null(AskAnswers.ChosenByClick(both, P, M0));
    }

    [Fact]
    public void Spent_drops_models_that_spend_nothing()
        => Assert.Equal(new Dictionary<Guid, int> { [M1] = 2 }, AskAnswers.Spent(new Dictionary<Guid, int> { [M0] = 0, [M1] = 2 }).Counts);

    [Fact]
    public void Move_follows_the_regions_model_order_from_where_each_stands()
    {
        var p = P;
        var proposed = new Dictionary<Guid, Point3> { [M0] = new(11, 15, 0), [M2] = new(15, 15, 0) };
        var d = AskAnswers.Move(p, Region(M2, M0), proposed, new Dictionary<Guid, Yaw> { [M2] = new(90) }, continues: false, exit: null);
        Assert.Equal([M2, M0], d.Moves.Select(m => m.ModelId));
        Assert.Equal(p.Models[M2].Location, d.Moves[0].From);
        Assert.Equal(new OnTable(new Placement(new Point3(15, 15, 0), new Yaw(90))), d.Moves[0].To);
        Assert.Equal(new OnTable(new Placement(new Point3(11, 15, 0), new Yaw(0))), d.Moves[1].To);   // its placement's yaw
        Assert.False(d.Continues);
    }

    [Fact]
    public void Stop_sends_the_ghosts_as_they_stand_and_continues()
    {
        var proposed = new Dictionary<Guid, Point3> { [M0] = new(10, 10, 0) };
        Assert.True(AskAnswers.Move(P, Region(M0), proposed, new Dictionary<Guid, Yaw>(), continues: true, exit: null).Continues);
    }

    [Fact]
    public void Facing_of_a_disembarking_model_is_the_exit_yaw_at_its_drop()
    {
        var from = new OnTable(new Placement(new Point3(12, 10, 0), new Yaw(0)));
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new ModelRelocation(M1, from, new Inside(M0))]));
        var at0 = ((OnTable)p.Models[M0].Location).Placement;
        var site = new ExitSite(6, [], new TableSize(72, 48));
        var expected = MoveRuler.ExitYawAt(Body(p, M1), new PlacedModel(Body(p, M0), at0), 6, new Point2(13, 10), [], site.Size);
        Assert.NotNull(expected);
        Assert.Equal(expected, AskAnswers.FacingOf(p, M1, new Yaw(135), new Point2(13, 10), site));   // the ghost's 135° ignored
        Assert.Equal(at0.Yaw, AskAnswers.FacingOf(p, M1, null, new Point2(40, 40), site));           // no fitting facing: the container's
        Assert.Equal(at0.Yaw, AskAnswers.FacingOf(p, M1, new Yaw(135), new Point2(13, 10), null));   // no exit site: the container's
    }

    [Fact]
    public void Facing_of_a_shelf_model_is_its_ghosts_or_zero()
    {
        var p = P; var reserve = TableUx.ModelOf(TableUx.Reserve);
        Assert.Equal(new Yaw(45), AskAnswers.FacingOf(p, reserve, new Yaw(45), new Point2(5, 5), null));
        Assert.Equal(new Yaw(0), AskAnswers.FacingOf(p, reserve, null, new Point2(5, 5), null));
    }

    [Fact]
    public void Embark_moves_every_model_of_the_region_inside_the_container()
    {
        var p = P; var legal = Region(M2, M1);
        var d = AskAnswers.Embark(p, legal, M0);
        Assert.Equal(legal.ModelIds, d.Moves.Select(m => m.ModelId));
        Assert.All(d.Moves, m => Assert.Equal(new Inside(M0), m.To));
        Assert.Equal(p.Models[M2].Location, d.Moves[0].From);
        Assert.False(d.Continues);
    }

    [Fact]
    public void Board_transport_item_is_the_embark_answer()
    {
        var p = P;
        var legal = new MoveRegion([M1], 6, [], [], null, new MoveContainment([M0], null, null));
        var ask = AskOf(legal);
        var item = Assert.Single(MenuModel.ForBoarding(p, ask, M1));
        Assert.Equivalent(new AnswerAsk(ask.Id, AskAnswers.Embark(p, legal, M0)), item.Build(null));
    }
}
