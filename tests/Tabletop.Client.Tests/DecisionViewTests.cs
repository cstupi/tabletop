using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class DecisionViewTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly string[] Unmuted = [];

    static Ask AskOf(string question, LegalSet legal, string prompt = "prompt", Guid? team = null, bool allowPass = false, string? window = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team ?? Samples.Team1], legal, allowPass, false, window, null);

    static DecisionModel Of(Ask ask, Guid? me, IReadOnlyCollection<string>? muted = null)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        return DecisionView.Of(a.Projection, Game, new Names(Game, a.Projection), me, muted ?? Unmuted);
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
    public void Past_nine_an_entry_has_no_key()
    {
        var many = new Options(Enumerable.Range(1, 11).Select(i => new AskOption($"o{i}", $"Option {i}", [])).ToList());
        var d = Of(AskOf(AskQuestions.Activate, many), Samples.Team1);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0], d.Numbered.Select(x => x.Key));
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
    public void A_muted_window_is_an_auto_pass()
    {
        var ask = AskOf(AskQuestions.React, new Options([new AskOption("r", "React", [])]), allowPass: true, window: "reaction");
        Assert.True(Of(ask, Samples.Team1, ["reaction"]).AutoPass);
        Assert.False(Of(ask, Samples.Team1).AutoPass);
    }
    /// <summary>activate offers one option per unit and action: a click on such a unit cannot answer, so it selects the unit and narrows the box.</summary>
    [Fact]
    public void Only_a_piece_one_choice_names_answers_a_click()
    {
        var other = Guid.CreateVersion7();
        var options = new Options([new AskOption("a", "Advance", [Samples.Unit]), new AskOption("h", "Hold", [Samples.Unit]), new AskOption("s", "Strike", [other])]);
        Assert.Equal([other], DecisionView.ClickAnswers(options));
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

    [Fact]
    public void The_selected_unit_s_note_is_read_off_my_open_choice()
    {
        var ask = AskOf(AskQuestions.Activate, new Options([new AskOption("i", "Squad: Idle", [Samples.Unit], ShakenNote), new AskOption("o", "Other: Hold", [Guid.CreateVersion7()])]));
        var a = new EventApplier(Samples.Board, Game);
        a.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        Assert.Equal(ShakenNote, DecisionView.NoteOf(a.Projection, Game, Samples.Team1, Unmuted, Samples.Unit));
        Assert.Null(DecisionView.NoteOf(a.Projection, Game, Samples.Team2, Unmuted, Samples.Unit));          // not my Ask
        Assert.Null(DecisionView.NoteOf(a.Projection, Game, Samples.Team1, Unmuted, Guid.CreateVersion7()));  // a unit with no choice
    }
}
