using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class UnitActionsViewTests
{
    static readonly Guid Squad = TableUx.Squad.Id, Orcs = TableUx.Orcs.Id;

    static Ask AskOf(string question, LegalSet legal, Guid? team = null) =>
        new(Guid.CreateVersion7(), question, "Activate a unit", [team ?? TableUx.T1], legal, false, false, null, null);

    static readonly Options Activation = new([
        new AskOption("sh", "Squad: Hold", [Squad]), new AskOption("sa", "Squad: Advance", [Squad]),
        new AskOption("sr", "Squad: Rush", [Squad]), new AskOption("sc", "Squad: Charge", [Squad]),
    ]);

    static Projection Open(Ask ask) => TableUx.After(TableUx.Deploy, new AskOpened(ask));

    [Fact]
    public void My_activation_shows_the_selected_units_options_as_the_decision_box_tiles()
    {
        var ask = AskOf(AskQuestions.Activate, Activation);
        var p = Open(ask);
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, Squad, watching: false)!;
        Assert.Equal((ask.Id, Squad), (v.AskId, v.UnitId));
        Assert.Equal(new Point3(12, 10, 1.5), v.Anchor);                           // the centroid, above the tallest model
        var hits = DecisionView.ChoicesOf(Activation, p, Squad);
        var box = DecisionCard.Groups(ask, hits, DecisionView.Number(hits), p, TableUx.Game, new Names(TableUx.Game, p), new Dictionary<Guid, int>()).SelectMany(g => g.Tiles);
        Assert.Equal(box, v.Options);
        Assert.Equal(new[] { ("sh", "hold", "Hold", 1), ("sa", "advance", "Advance", 2), ("sr", "rush", "Rush", 3), ("sc", "charge", "Charge", 4) },
            v.Options.Select(o => (o.OptionId, o.Icon!, o.Word, o.Key)));
        Assert.Equal("Advance  2", UnitActions.Tip(v.Options[1]));
    }

    [Fact]
    public void An_option_not_offered_is_not_drawn_and_another_units_options_stay_in_the_box()
    {
        var options = new Options([new AskOption("sh", "Squad: Hold", [Squad]), new AskOption("oh", "Orcs: Hold", [Orcs]), new AskOption("sa", "Squad: Advance", [Squad])]);
        var p = Open(AskOf(AskQuestions.ActOrEndPhase, options));
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, Squad, false)!;
        Assert.Equal(new[] { "sh", "sa" }, v.Options.Select(o => o.OptionId));
    }

    [Fact]
    public void The_keys_are_the_decision_boxs_numbering_when_given()
    {
        var p = Open(AskOf(AskQuestions.Activate, Activation));
        IReadOnlyList<(int, string, string)> full = [(5, "Squad: Hold", "sh"), (6, "Squad: Advance", "sa"), (7, "Squad: Rush", "sr"), (8, "Squad: Charge", "sc")];
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, Squad, false, full)!;
        Assert.Equal(new[] { 5, 6, 7, 8 }, v.Options.Select(o => o.Key));
        Assert.Equal("Hold  5", UnitActions.Tip(v.Options[0]));
        Assert.Equal("Hold", UnitActions.Tip(v.Options[0] with { Key = 0 }));
    }

    [Fact]
    public void Hidden_for_other_asks_other_teams_watching_and_other_selections()
    {
        var mine = Open(AskOf(AskQuestions.Activate, Activation));
        Assert.Null(UnitActionsView.Of(mine, TableUx.Game, TableUx.T1, Squad, watching: true));       // watching or replaying
        Assert.Null(UnitActionsView.Of(mine, TableUx.Game, null, Squad, false));                      // no team
        Assert.Null(UnitActionsView.Of(mine, TableUx.Game, TableUx.T1, null, false));                 // nothing selected
        Assert.Null(UnitActionsView.Of(mine, TableUx.Game, TableUx.T1, Orcs, false));                 // a unit the Ask offers nothing
        Assert.Null(UnitActionsView.Of(mine, TableUx.Game, TableUx.T1, TableUx.Reserve.Id, false));   // nothing on the table
        var theirs = Open(AskOf(AskQuestions.Activate, Activation, TableUx.T2));
        Assert.Null(UnitActionsView.Of(theirs, TableUx.Game, TableUx.T1, Squad, false));              // another team's Ask
        var other = Open(AskOf("strike_back", new Options([new AskOption("s", "Squad: Strike back", [Squad])])));
        Assert.Null(UnitActionsView.Of(other, TableUx.Game, TableUx.T1, Squad, false));               // not an activation
        var ask = AskOf(AskQuestions.Activate, Activation);
        var answered = TableUx.After(TableUx.Deploy, new AskOpened(ask), new AskAnswered(ask.Id, TableUx.T1, new Chosen("sh"), false));
        Assert.Null(UnitActionsView.Of(answered, TableUx.Game, TableUx.T1, Squad, false));            // once answered
    }

    [Fact]
    public void The_row_sits_centred_above_the_point_inside_the_view()
    {
        var view = new HudRect(0, 0, 1280, 720);
        Assert.Equal(new HudRect(580, 300, 120, 40), UnitActionsPlace.Rect(640, 340, 120, 40, view, [], 8));
        Assert.Equal(new HudRect(8, 8, 120, 40), UnitActionsPlace.Rect(10, 20, 120, 40, view, [], 8));   // pulled inside the view's margin
        Assert.Equal(new HudRect(1152, 672, 120, 40), UnitActionsPlace.Rect(1270, 900, 120, 40, view, [], 8));
    }

    [Fact]
    public void The_row_keeps_off_the_hud_panels()
    {
        var view = new HudRect(0, 0, 1280, 720);
        var decision = new HudRect(840, 400, 432, 312);
        var r = UnitActionsPlace.Rect(900, 500, 120, 40, view, [decision], 8);
        Assert.False(r.Overlaps(decision with { X = decision.X - 8, W = decision.W + 16, Y = decision.Y - 8, H = decision.H + 16 }));
        Assert.Equal(new HudRect(840, 352, 120, 40), r);                       // the nearest clear place: above the panel
        var header = new HudRect(260, 0, 760, 96);
        Assert.Equal(new HudRect(580, 104, 120, 40), UnitActionsPlace.Rect(640, 60, 120, 40, view, [header], 8));   // under the header
    }
}
