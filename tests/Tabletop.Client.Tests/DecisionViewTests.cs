using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class DecisionViewTests
{
    static readonly GameInfo Game = Samples.Game;

    static Ask AskOf(string question, LegalSet legal, string prompt = "prompt", Guid? team = null, bool allowPass = false, string? window = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team ?? Samples.Team1], legal, allowPass, false, window, null);

    static DecisionModel Of(Ask ask, Guid? me)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        return DecisionView.Of(a.Projection, Game, new Names(Game, a.Projection), me);
    }

    [Fact]
    public void Options_are_numbered()
    {
        var ask = AskOf(AskQuestions.Activate, new Options([new AskOption("a", "Advance", [Samples.Unit]), new AskOption("h", "Hold", [Samples.Unit])]), "Activate a unit");
        var d = Of(ask, Samples.Team1);
        Assert.Equal((ask.Id, AskMode.Choose, "Activate a unit"), (d.Ask!.Id, d.Mode, d.Title));
        Assert.Equal(new[] { (1, "Advance", "a"), (2, "Hold", "h") }, d.Numbered);
    }

    [Fact]
    public void The_tenth_entry_is_keyed_0_and_past_ten_none()
    {
        var many = new Options(Enumerable.Range(1, 11).Select(i => new AskOption($"o{i}", $"Option {i}", [])).ToList());
        var d = Of(AskOf(AskQuestions.Activate, many), Samples.Team1);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0], d.Numbered.Select(x => x.Key));
    }

    [Fact]
    public void A_deploy_ask_numbers_the_shelved_units_and_a_target_pick_numbers_nothing()
    {
        var deploy = Of(AskOf(AskQuestions.Deploy, new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Equal(new[] { (1, "Battle Brothers", Samples.Unit.ToString()) }, deploy.Numbered);
        var charge = Of(AskOf("charge_target", new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Empty(charge.Numbered);
    }

    [Fact]
    public void A_stoppable_move_offers_cast_when_a_moving_model_holds_spell_tokens()
    {
        var stoppable = new MoveRegion([Samples.Model], 6, [], [], null, Stoppable: true);
        var a = new EventApplier(Samples.Board, Game);
        Assert.Equal("Stop here", DecisionView.StopLabel(a.Projection, stoppable));
        Assert.Null(DecisionView.StopLabel(a.Projection, stoppable with { Stoppable = false }));
        a.Apply(new GameEvent(1, null, new SpellTokensSet(Samples.Model, 2), null));
        Assert.Equal("Cast here", DecisionView.StopLabel(a.Projection, stoppable));
    }

    [Fact]
    public void Casualties_title_the_count()
    {
        var casualties = Of(AskOf("remove_casualties", new Allocation([Samples.Model], 3), "Assign 3 wound(s)"), Samples.Team1);
        Assert.Equal("Remove 3", casualties.Title);
    }

    [Fact]
    public void Nothing_addressed_to_me_gives_no_ask()
    {
        var ask = AskOf("remove_casualties", new Allocation([Samples.Model], 2), team: Samples.Team2);
        foreach (var me in new Guid?[] { Samples.Team1, null })
        {
            var d = Of(ask, me);
            Assert.Null(d.Ask); Assert.Null(d.Mode);
            Assert.Equal("", d.Title);
            Assert.Empty(d.Numbered);
        }
    }

    [Fact]
    public void A_window_that_allows_a_pass_is_shown_with_its_choices()
    {
        var ask = AskOf(AskQuestions.React, new Options([new AskOption("r", "React", [])]), allowPass: true, window: "reaction");
        var d = Of(ask, Samples.Team1);
        Assert.Equal(ask, d.Ask);
        Assert.Equal([(1, "React", "r")], d.Numbered);
    }
    /// <summary>activate offers one option per unit and action: a click on such a unit cannot answer, so it selects the unit and narrows the box.</summary>
    [Fact]
    public void Only_a_piece_one_choice_names_answers_a_click()
    {
        var orc = TableUx.ModelOf(TableUx.Orcs);
        var options = new Options([new AskOption("a", "Advance", [orc]), new AskOption("h", "Hold", [orc]), new AskOption("s", "Strike", [TableUx.Orcs.Id])]);
        Assert.Equal([TableUx.Orcs.Id], DecisionView.ClickAnswers(options, Picking(options, TableUx.T1), TableUx.After(TableUx.Deploy), TableUx.Game));
    }

    static Ask Picking(Options o, Guid team) => new(Guid.CreateVersion7(), "charge_target", "x", [team], o, false, false, null, null);

    /// <summary>The owner, 2026-11-14 ("Selecting a unit shouldnt automatically execute idle") and 2026-11-21 ("Its automatically moving to
    /// the shooting phase when i reclick the unit"): a click on a unit of the team answering the Ask never answers it, even when one option
    /// names it (a Shaken unit's Idle, the acting unit's Shoot); the click selects it. Only a pick between the other team's units answers.</summary>
    [Fact]
    public void A_click_answers_only_with_the_other_teams_units()
    {
        var orc = TableUx.ModelOf(TableUx.Orcs);
        var options = new Options([new AskOption("s", "Shoot", [TableUx.Squad.Id]), new AskOption("i", "Idle", [TableUx.Reserve.Id]), new AskOption("c", "Charge", [orc])]);
        var p = TableUx.After(TableUx.Deploy);
        Assert.Equal([orc], DecisionView.ClickAnswers(options, Picking(options, TableUx.T1), p, TableUx.Game));
        Assert.True(DecisionView.ClickAnswers(options, Picking(options, TableUx.T2), p, TableUx.Game).SetEquals([TableUx.Squad.Id, TableUx.Reserve.Id]));
    }

    [Fact]
    public void A_unit_narrows_the_box_to_its_choices_numbered_from_one()
    {
        var a = new EventApplier(Samples.Board, Game);
        var other = Guid.CreateVersion7();
        var options = new Options([new AskOption("x", "Other: Hold", [other]), new AskOption("a", "Advance", [Samples.Unit]), new AskOption("m", "Charge", [Samples.Model])]);
        var narrowed = DecisionView.ChoicesOf(options, a.Projection, Samples.Unit);
        Assert.Equal(["a", "m"], narrowed.Select(c => c.Id));                                    // by the unit id, or by one of its models
        Assert.Equal(new[] { (1, "Advance", "a"), (2, "Charge", "m") }, DecisionView.Number(narrowed));
        Assert.Empty(DecisionView.ChoicesOf(options, a.Projection, Guid.CreateVersion7()));
    }

    /// <summary>The owner, 2026-11-22, after undoing a unit's Rush: the unit stays selected, so the activation drawn again keeps to its
    /// choices with the keys it had (Charge 4), not every unit's (where 4 was its Hold).</summary>
    [Fact]
    public void An_activation_drawn_with_a_unit_selected_starts_narrowed_to_it()
    {
        var p = new EventApplier(Samples.Board, Game).Projection;
        var options = new Options([new AskOption("x", "Other: Hold", [Guid.CreateVersion7()]), new AskOption("a", "Advance", [Samples.Unit]), new AskOption("m", "Charge", [Samples.Model])]);
        Assert.Equal(["a", "m"], DecisionView.Narrowed(options, p, Samples.Unit)!.Select(c => c.Id));
        Assert.Null(DecisionView.Narrowed(options, p, null));
        Assert.Null(DecisionView.Narrowed(options, p, Guid.CreateVersion7()));   // a unit with no choice here: every unit's
    }

    [Fact]
    public void An_option_names_one_unit_by_its_id_or_its_models_ids()
    {
        var p = new EventApplier(Samples.Board, Game).Projection;
        Assert.Equal(Samples.Unit, DecisionView.UnitNamed([Samples.Unit], p));
        Assert.Equal(Samples.Unit, DecisionView.UnitNamed([Samples.Model], p));
        Assert.Equal(Samples.Unit, DecisionView.UnitNamed([Samples.Unit, Samples.Model], p));
        Assert.Null(DecisionView.UnitNamed([], p));
        Assert.Null(DecisionView.UnitNamed([Samples.Unit, Guid.CreateVersion7()], p));          // a piece that is no unit or model
    }

    const string ShakenNote = "Shaken: must spend this activation being idle";

    [Fact]
    public void A_unit_s_note_shows_once_after_its_buttons_and_an_unnoted_unit_shows_none()
    {
        var other = Guid.CreateVersion7();
        var rows = DecisionView.Rows([
            new AskOption("i", "Squad: Idle", [Samples.Unit], ShakenNote),
            new AskOption("h", "Other: Hold", [other]), new AskOption("a", "Other: Advance", [other])]);
        Assert.Equal([("i", null), (null, ShakenNote), ("h", null), ("a", null)], rows.Select(r => (r.Option?.Id, r.Note)));
        var two = DecisionView.Rows([new AskOption("h", "Gun: Hold", [Samples.Unit], "Artillery"), new AskOption("x", "Gun: Advance", [Samples.Unit], "Artillery")]);
        Assert.Equal([("h", null), ("x", null), (null, "Artillery")], two.Select(r => (r.Option?.Id, r.Note)));
    }
}
