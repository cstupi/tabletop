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
    public void Options_are_numbered_and_the_hint_names_the_keys()
    {
        var ask = AskOf(AskQuestions.Activate, new Options([new AskOption("a", "Advance", [Samples.Unit]), new AskOption("h", "Hold", [Samples.Unit])]), "Activate a unit");
        var d = Of(ask, Samples.Team1);
        Assert.Equal((ask.Id, AskMode.Choose, "Activate a unit"), (d.Ask!.Id, d.Mode, d.Title));
        Assert.Equal(new[] { (1, "Advance", "a"), (2, "Hold", "h") }, d.Numbered);
        Assert.Equal("1–2: pick · click a glowing unit", d.Hint);
        Assert.Equal("1: pick", Of(AskOf("shoot_or_not", new Options([new AskOption("s", "Shoot", [])])), Samples.Team1).Hint);
    }

    [Fact]
    public void Past_nine_an_entry_has_no_key()
    {
        var many = new Options(Enumerable.Range(1, 11).Select(i => new AskOption($"o{i}", $"Option {i}", [])).ToList());
        var d = Of(AskOf(AskQuestions.Activate, many), Samples.Team1);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0], d.Numbered.Select(x => x.Key));
        Assert.Equal("1–9: pick", d.Hint);
    }

    [Fact]
    public void A_deploy_ask_numbers_the_shelved_units_and_a_target_pick_asks_for_a_click()
    {
        var deploy = Of(AskOf(AskQuestions.Deploy, new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Equal(new[] { (1, "Battle Brothers", Samples.Unit.ToString()) }, deploy.Numbered);
        Assert.Equal("1: pick a unit to deploy", deploy.Hint);
        var charge = Of(AskOf("charge_target", new PieceSelection([Samples.Unit], 1, 1)), Samples.Team1);
        Assert.Empty(charge.Numbered);
        Assert.Equal("Click a glowing unit", charge.Hint);
        Assert.Equal("Click a glowing model", Of(AskOf("takedown_target", new PieceSelection([Samples.Model], 1, 1)), Samples.Team1).Hint);
        Assert.Equal("Click a glowing unit · Enter: done", Of(AskOf("spell_targets", new PieceSelection([Samples.Unit], 1, 2)), Samples.Team1).Hint);
    }

    [Fact]
    public void Each_mode_has_its_hint_and_casualties_title_the_count()
    {
        Assert.Equal("Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · Esc: reset",
            Of(AskOf("move", new MoveRegion([Samples.Model], 6, [], [], null)), Samples.Team1).Hint);
        Assert.Equal("Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · C: stop here to cast or pick · Esc: reset",
            Of(AskOf("move", new MoveRegion([Samples.Model], 6, [], [], null, Stoppable: true)), Samples.Team1).Hint);
        Assert.Equal("Click a target · Click a chip: that group alone · Enter: fire · Esc: clear",
            Of(AskOf("declare_targets", new Assignment([new AskOption("Rifles", "Rifles", [])], [Samples.Unit], 2)), Samples.Team1).Hint);
        var casualties = Of(AskOf("remove_casualties", new Allocation([Samples.Model], 3), "Assign 3 wound(s)"), Samples.Team1);
        Assert.Equal(("Remove 3", "Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm"), (casualties.Title, casualties.Hint));
        Assert.Equal("Set the tokens per caster · Enter: spend",
            Of(AskOf(AskQuestions.SpendTokens, new TokenSpend(new Dictionary<Guid, int> { [Samples.Model] = 1 })), Samples.Team1).Hint);
        Assert.Equal("Enter: Continue", Of(AskOf(AskQuestions.Confirm, new Confirmation()), Samples.Team1).Hint);
        Assert.Equal("Enter: End the round", Of(AskOf(AskQuestions.EndRound, new Confirmation()), Samples.Team1).Hint);
    }

    [Fact]
    public void Nothing_addressed_to_me_gives_no_ask()
    {
        var ask = AskOf("remove_casualties", new Allocation([Samples.Model], 2), team: Samples.Team2);
        foreach (var me in new Guid?[] { Samples.Team1, null })
        {
            var d = Of(ask, me);
            Assert.Null(d.Ask); Assert.Null(d.Mode);
            Assert.Equal(("", ""), (d.Title, d.Hint));
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
}
