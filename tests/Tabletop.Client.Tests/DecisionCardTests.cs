using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class DecisionCardTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Guid SecondId = Guid.CreateVersion7(), SecondModel = Guid.CreateVersion7();

    static Ask AskOf(string question, LegalSet legal, string prompt = "prompt", Guid? team = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team ?? Samples.Team1], legal, false, false, null, null);

    /// <summary>The sample army with a second unit of one model, and the projection after the payloads.</summary>
    static (Projection P, Guid Second) Two(params EventPayload[] payloads)
    {
        var army = Samples.Army;
        var first = army.Units[0];
        var second = first with { Id = SecondId, Name = "Scouts", Models = [first.Models[0] with { Id = SecondModel }] };
        var board = new Board(Samples.Board.Table, [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(army with { Units = [first, second] }, Guid.CreateVersion7()))]);
        var a = new EventApplier(board, Game);
        foreach (var (x, i) in payloads.Select((x, i) => (x, i))) a.Apply(new GameEvent(i + 1, null, x, null));
        return (a.Projection, second.Id);
    }

    [Fact]
    public void Activation_options_group_by_unit_with_the_word_after_the_colon_and_keys_continuing()
    {
        var (_, second) = Two();
        var options = new Options([
            new AskOption("h1", "Battle Brothers: Hold", [Samples.Unit]), new AskOption("a1", "Battle Brothers: Advance", [Samples.Unit]),
            new AskOption("h2", "Scouts: Hold", [second], "Shaken: may only Hold"), new AskOption("x", "Scouts: Dance", [second], "Shaken: may only Hold"),
        ]);
        var ask = AskOf(AskQuestions.Activate, options, "Activate a unit");
        var (p, _) = Two(new AskOpened(ask));
        var n = new Names(Game, p);
        var numbered = DecisionView.Of(p, Game, n, Samples.Team1, []).Numbered;
        var groups = DecisionCard.Groups(ask, options.Choices, numbered, p, Game, n, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Equal(2, groups.Count);
        Assert.Equal(("Battle Brothers", 1, "●●●", (string?)null), (groups[0].Unit, groups[0].TeamOrdinal, groups[0].Pips, groups[0].Note));
        Assert.Equal(new[] { (1, "Hold", "hold"), (2, "Advance", "advance") }, groups[0].Tiles.Select(t => (t.Key, t.Word, t.Icon!)));
        Assert.Equal(("Scouts", "Shaken: may only Hold"), (groups[1].Unit, groups[1].Note));
        Assert.Equal(new[] { (3, "Hold", "hold"), (4, "Dance", "activation") }, groups[1].Tiles.Select(t => (t.Key, t.Word, t.Icon!)));
    }

    [Fact]
    public void Other_options_are_one_group_of_whole_labels()
    {
        var options = new Options([new AskOption("strike", "Strike back: now", [Samples.Unit]), new AskOption("pass", "Don't", [])]);
        var ask = AskOf("strike_back", options, "Strike back?");
        var (p, _) = Two(new AskOpened(ask));
        var g = Assert.Single(DecisionCard.Groups(ask, options.Choices, [(1, "Strike back: now", "strike"), (2, "Don't", "pass")], p, Game, new Names(Game, p), new Dictionary<Guid, int>()));
        Assert.Null(g.Unit);
        Assert.Equal(new[] { "Strike back: now", "Don't" }, g.Tiles.Select(t => t.Word));
        Assert.All(g.Tiles, t => Assert.Null(t.Icon));
    }

    [Fact]
    public void The_head_names_the_asks_icon_and_a_short_title()
    {
        var (p, _) = Two();
        var n = new Names(Game, p);
        Assert.Equal(new CardHead("activation", "Activate a unit"), DecisionCard.Head(AskOf(AskQuestions.Activate, new Options([]), "Activate a unit"), p, n));
        Assert.Equal(new CardHead("army", "Deploy one unit"), DecisionCard.Head(AskOf(AskQuestions.Deploy, new PieceSelection([], 1, 1), "Deploy one unit"), p, n));
        Assert.Equal(new CardHead("charge", "Charge which unit?"), DecisionCard.Head(AskOf("charge_target", new PieceSelection([], 1, 1), "Charge which unit?"), p, n));
        var longPrompt = "Spend tokens for or against the casting roll of the spell";
        Assert.Equal(new CardHead("cast", "Spend tokens", null, longPrompt), DecisionCard.Head(AskOf(AskQuestions.SpendTokens, new TokenSpend(new Dictionary<Guid, int>()), longPrompt), p, n));
        Assert.Equal(new CardHead("ready", "End or continue?", null, "4 rounds"), DecisionCard.Head(AskOf(AskQuestions.GameOver, new Options([]), "4 rounds"), p, n));
    }

    [Fact]
    public void A_moves_head_is_the_activations_action_and_its_unit()
    {
        var (p, _) = Two(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")));
        var n = new Names(Game, p);
        var move = AskOf("move", new MoveRegion([Samples.Model], 6, [], [], null), "Move up to 6\"");
        Assert.Equal(new CardHead("advance", "Advance", "Battle Brothers"), DecisionCard.Head(move, p, n));
        Assert.Equal(new CardHead("shoot", "Shoot", "Battle Brothers"), DecisionCard.Head(AskOf("declare_targets", new Assignment([], [], 2)), p, n));
        var (bare, _) = Two();
        Assert.Equal(new CardHead("advance", "Move", "Battle Brothers"), DecisionCard.Head(move, bare, new Names(Game, bare)));
    }

    [Fact]
    public void The_bar_reads_the_farthest_model_over_the_allowance()
    {
        var bar = DecisionCard.Bar([MoveRuler.Read(3, 12), MoveRuler.Read(7.5, 12)], 12, null);
        Assert.Equal(("7.5\" of 12\"", 0.625, false), (bar.Text, bar.Fraction, bar.Over));
        var over = DecisionCard.Bar([MoveRuler.Read(13, 12)], 12, 9);
        Assert.Equal((true, 1.0, 0.75), (over.Over, over.Fraction, over.MinimumFraction!.Value));
        Assert.Equal("0\" of 6\"", DecisionCard.Bar([], 6, null).Text);
    }

    [Fact]
    public void An_ask_not_mine_is_a_quiet_band_per_waiting_team()
    {
        var theirs = AskOf("remove_casualties", new Allocation([], 1), team: Samples.Team2);
        var mine = AskOf(AskQuestions.Activate, new Options([]));
        var (p, _) = Two(new AskOpened(theirs), new AskOpened(mine));
        Assert.Equal([new QuietBand(Samples.Team1, 1, "Team 1", "activating", true), new QuietBand(Samples.Team2, 2, "Team 2", "removing casualties", false)],
            DecisionCard.Quiet(p, Game, [Samples.Team1]));
        Assert.Equal("charge target", DecisionCard.Doing("charge_target"));
    }

    [Fact]
    public void A_sealed_choice_reveals_the_option_chosen()
    {
        var r = new SealedReveals(); var n = new Names(Game, null);
        var ask = Samples.Ask with { Id = Guid.CreateVersion7(), Teams = [Samples.Team1, Samples.Team2], Sealed = true };
        r.Read([new GameEvent(1, null, new AskOpened(ask), null), new GameEvent(2, null, new AskClosed(ask.Id, [new TeamAnswer(Samples.Team2, new Chosen("continue"))]), null)], n);
        Assert.Equal(new RevealChip(2, "Team 2", "continue"), Assert.Single(Assert.Single(r.Drain())));
    }

    [Fact]
    public void Key_caps()
    {
        Assert.Equal(("3", ""), (DecisionCard.KeyCap(3), DecisionCard.KeyCap(0)));
        Assert.Equal(("Hold", "Red unit 1"), (DecisionCard.Word("Red unit 1: Hold"), DecisionCard.Word("Red unit 1")));
    }
}
