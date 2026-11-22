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
        Assert.Null(UnitActionsView.Of(other, TableUx.Game, TableUx.T1, Orcs, false));                // another Ask, an enemy selected
        var ask = AskOf(AskQuestions.Activate, Activation);
        var answered = TableUx.After(TableUx.Deploy, new AskOpened(ask), new AskAnswered(ask.Id, TableUx.T1, new Chosen("sh"), false));
        Assert.Null(UnitActionsView.Of(answered, TableUx.Game, TableUx.T1, Squad, false));            // once answered
    }

    // Every step of the activation (the owner, 2026-11-21: "where is my icons to shoot?"): the bar stands over the acting unit for every open
    // Ask of mine, the Ask's tiles and then the card's foot actions with their numbers (CardActions), the same the decision box shows.
    static readonly Guid Frame = Guid.CreateVersion7();
    static EventPayload Acting(string action) => new SequenceStarted(Frame, action.ToLowerInvariant(), new SequenceContext(Squad, [], [], action));
    static IReadOnlyList<Guid> SquadModels => TableUx.Squad.Models.Select(m => m.Id).ToList();

    [Fact]
    public void A_move_stands_over_the_acting_unit_with_commit_and_stop_here()
    {
        var ask = AskOf("move", new MoveRegion(SquadModels, 6, [], [], null, Stoppable: true));
        var p = TableUx.After(TableUx.Deploy, Acting("Advance"), new AskOpened(ask));
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, null, false)!;                  // nothing selected: the activation's unit
        Assert.Equal((ask.Id, Squad), (v.AskId, v.UnitId));
        Assert.Empty(v.Options);
        Assert.Equal(new[] { (CardActions.Commit, 1, true), (CardActions.Stop, 2, false) }, v.Actions.Select(a => (a.Id, a.Key, a.Primary)));
        Assert.Equal(CardActions.Numbered(CardActions.Of(ask, AskMode.Move, p), 0), v.Actions);
    }

    [Fact]
    public void Shooting_holds_fire_and_clear_and_a_target_over_each_enemy_it_may_shoot()
    {
        var ask = AskOf("declare_targets", new Assignment([new AskOption("g1", "Rifles", [])], [Orcs], 1));
        var p = TableUx.After(TableUx.Deploy, Acting("Hold"), new AskOpened(ask));
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, Orcs, false)!;                  // an enemy selected: still over my acting unit
        Assert.Equal(Squad, v.UnitId);
        Assert.Equal(new[] { (CardActions.Fire, 1), (CardActions.Clear, 2) }, v.Actions.Select(a => (a.Id, a.Key)));
        Assert.Equal([new UnitTarget(Orcs, new Point3(40, 30, 1.5))], v.Targets);
        Assert.Equal([new UnitTarget(Orcs, new Point3(40, 30, 1.5))], UnitActionsView.TargetsOf(p, [Orcs, TableUx.Reserve.Id]));   // none off the table
    }

    [Fact]
    public void Another_options_ask_shows_all_its_tiles_then_pass_keyed_after_them()
    {
        var ask = new Ask(Guid.CreateVersion7(), "shoot_or_not", "Shoot?", [TableUx.T1],
            new Options([new AskOption("s", "Shoot", [Squad]), new AskOption("n", "Do not shoot", [Squad])]), true, false, null, null);
        var p = TableUx.After(TableUx.Deploy, Acting("Hold"), new AskOpened(ask));
        var v = UnitActionsView.Of(p, TableUx.Game, TableUx.T1, null, false)!;
        Assert.Equal(new[] { ("s", 1), ("n", 2) }, v.Options.Select(o => (o.OptionId, o.Key)));
        Assert.Equal(new[] { (CardActions.Pass, 3) }, v.Actions.Select(a => (a.Id, a.Key)));
    }

    [Fact]
    public void With_no_activation_of_mine_it_stands_over_the_selected_unit_of_mine_the_ask_names()
    {
        var ask = AskOf("strike_back", new Options([new AskOption("s", "Squad: Strike back", [Squad])]));
        var p = Open(ask);
        Assert.Equal(Squad, UnitActionsView.Of(p, TableUx.Game, TableUx.T1, Squad, false)!.UnitId);
        Assert.Null(UnitActionsView.Of(p, TableUx.Game, TableUx.T1, null, false));
        var casualties = AskOf("remove_casualties", new Allocation(SquadModels, 1));
        var v = UnitActionsView.Of(Open(casualties), TableUx.Game, TableUx.T1, Squad, false)!;
        Assert.Equal(new[] { CardActions.Confirm, CardActions.Auto, CardActions.Clear }, v.Actions.Select(a => a.Id));
    }

    [Fact]
    public void An_activation_ends_with_pass_keyed_after_the_units_tiles()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.ActOrEndPhase, "Activate", [TableUx.T1], Activation, true, false, null, null);
        var v = UnitActionsView.Of(Open(ask), TableUx.Game, TableUx.T1, Squad, false)!;
        Assert.Equal(new[] { (CardActions.Pass, 5, "End phase") }, v.Actions.Select(a => (a.Id, a.Key, a.Word)));
    }

    [Fact]
    public void The_row_sits_centred_a_step_above_the_models_inside_the_view()
    {
        var view = new HudRect(0, 0, 1280, 720);
        Assert.Equal(new HudRect(580, 300, 120, 40), UnitActionsPlace.Rect(640, 348, 120, 40, view, [], 8));
        Assert.Equal(new HudRect(8, 8, 120, 40), UnitActionsPlace.Rect(10, 20, 120, 40, view, [], 8));   // pulled inside the view's margin
        Assert.Equal(new HudRect(1152, 672, 120, 40), UnitActionsPlace.Rect(1270, 900, 120, 40, view, [], 8));
    }

    [Fact]
    public void The_row_stands_a_step_above_the_units_name_label()
    {
        var view = new HudRect(0, 0, 1280, 720);
        Assert.Equal(new HudRect(580, 232, 120, 40), UnitActionsPlace.Rect(640, 348, 120, 40, view, [], 8, new UnitLabel(330, 50)));   // label top 280
        Assert.Equal(new HudRect(580, 300, 120, 40), UnitActionsPlace.Rect(640, 348, 120, 40, view, [], 8, new UnitLabel(400, 20)));   // a label under the models' top
        Assert.Equal(new HudRect(580, 8, 120, 40), UnitActionsPlace.Rect(640, 120, 120, 40, view, [], 8, new UnitLabel(90, 60)));      // still inside the view
    }

    [Fact]
    public void The_row_keeps_off_the_hud_panels()
    {
        var view = new HudRect(0, 0, 1280, 720);
        var decision = new HudRect(840, 400, 432, 312);
        var r = UnitActionsPlace.Rect(900, 508, 120, 40, view, [decision], 8);
        Assert.False(r.Overlaps(decision with { X = decision.X - 8, W = decision.W + 16, Y = decision.Y - 8, H = decision.H + 16 }));
        Assert.Equal(new HudRect(840, 352, 120, 40), r);                       // the nearest clear place: above the panel
        var header = new HudRect(260, 0, 760, 96);
        Assert.Equal(new HudRect(580, 104, 120, 40), UnitActionsPlace.Rect(640, 68, 120, 40, view, [header], 8));   // under the header
    }
}

public class UnitActionsHoldTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    [Fact]
    public void The_first_place_is_taken_at_once_and_a_held_bar_keeps_its_place()
    {
        var hold = new UnitActionsHold();
        Assert.Equal(new HudRect(100, 200, 120, 40), hold.Step(A, new HudRect(100, 200, 120, 40), held: false, 0.016f));
        // The models were dragged and the name label moved 20 px up: under the pointer the bar stays.
        Assert.Equal(new HudRect(100, 200, 120, 40), hold.Step(A, new HudRect(100, 180, 120, 40), held: true, 0.016f));
        Assert.Equal(new HudRect(100, 200, 160, 40), hold.Step(A, new HudRect(100, 180, 160, 40), held: true, 0.016f));   // a new button widens it in place
    }

    [Fact]
    public void Let_go_it_eases_to_the_new_place_without_a_jump()
    {
        var hold = new UnitActionsHold();
        hold.Step(A, new HudRect(100, 200, 120, 40), false, 0.016f);
        var want = new HudRect(100, 180, 120, 40);
        var first = hold.Step(A, want, false, 0.016f);
        Assert.True(first.Y < 200 && first.Y > 195, $"{first}");   // a fifth of the gap or less in one frame
        var at = first;
        for (var i = 0; i < 60; i++) at = hold.Step(A, want, false, 0.016f);
        Assert.Equal(want, at);   // settled within a second
    }

    [Fact]
    public void Another_unit_or_a_hidden_bar_takes_its_place_at_once()
    {
        var hold = new UnitActionsHold();
        hold.Step(A, new HudRect(100, 200, 120, 40), false, 0.016f);
        Assert.Equal(new HudRect(600, 300, 120, 40), hold.Step(B, new HudRect(600, 300, 120, 40), false, 0.016f));
        hold.Reset();
        Assert.Equal(new HudRect(10, 10, 120, 40), hold.Step(B, new HudRect(10, 10, 120, 40), true, 0.016f));
    }
}
