using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>The owner, 2026-11-21: "standardize on action hot keys - if 1,2,3 decides then users should hit 1 not enter to confirm actions
/// that appear in the lower right." Every action on the card is numbered in one sequence: tiles first, then the foot's, the primary first.</summary>
public class CardActionsTests
{
    static Ask AskOf(LegalSet legal, string question = "x", bool allowPass = false) => new(Guid.CreateVersion7(), question, "p", [TableUx.T1], legal, allowPass, false, null, null);
    static Projection P => TableUx.After(TableUx.Deploy);
    static IReadOnlyList<string> Ids(IEnumerable<CardAction> a) => a.Select(x => x.Id).ToList();

    [Fact]
    public void Shooting_holds_fire_then_clear_then_pass()
    {
        var ask = AskOf(new Assignment([], [], 1), "declare_targets", allowPass: true);
        var a = CardActions.Of(ask, AskMode.Assign, P);
        Assert.Equal([CardActions.Fire, CardActions.Clear, CardActions.Pass], Ids(a));
        Assert.Equal([true, false, false], a.Select(x => x.Primary));
        Assert.Equal(["Fire", "Clear", "Pass"], a.Select(x => x.Word));
    }

    [Fact]
    public void A_move_commits_then_stops_here_when_stoppable()
    {
        var squad = TableUx.Squad.Models.Select(m => m.Id).ToList();
        var stoppable = new MoveRegion(squad, 6, [], [], null, Stoppable: true);
        Assert.Equal([CardActions.Commit, CardActions.Stop], Ids(CardActions.Of(AskOf(stoppable, "move"), AskMode.Move, P)));
        Assert.Equal("Stop here", CardActions.Of(AskOf(stoppable, "move"), AskMode.Move, P)[1].Word);
        Assert.Equal([CardActions.Commit], Ids(CardActions.Of(AskOf(new MoveRegion(squad, 6, [], [], null), "move"), AskMode.Move, P)));
    }

    [Fact]
    public void The_other_modes_foot_actions()
    {
        Assert.Equal([CardActions.Confirm], Ids(CardActions.Of(AskOf(new Confirmation()), AskMode.Confirm, P)));
        Assert.Equal("End the round", CardActions.Of(AskOf(new Confirmation(), AskQuestions.EndRound), AskMode.Confirm, P)[0].Word);
        Assert.Equal([CardActions.Spend], Ids(CardActions.Of(AskOf(new TokenSpend(new Dictionary<Guid, int>())), AskMode.TokenSpend, P)));
        Assert.Equal([CardActions.Confirm, CardActions.Auto, CardActions.Clear], Ids(CardActions.Of(AskOf(new Allocation([], 1)), AskMode.Allocate, P)));
        Assert.Equal([CardActions.Done], Ids(CardActions.Of(AskOf(new PieceSelection([], 1, 2)), AskMode.PickPieces, P)));
        Assert.Empty(CardActions.Of(AskOf(new PieceSelection([], 1, 1)), AskMode.PickPieces, P));
        var activate = CardActions.Of(AskOf(new Options([]), AskQuestions.ActOrEndPhase, allowPass: true), AskMode.Choose, P);
        Assert.Equal([CardActions.Pass], Ids(activate));
        Assert.Equal("End phase", activate[0].Word);
    }

    [Fact]
    public void Keys_follow_the_tiles_primary_first()
    {
        var ask = AskOf(new Assignment([], [], 1), "declare_targets", allowPass: true);
        Assert.Equal([1, 2, 3], CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), 0).Select(a => a.Key));
        Assert.Equal([4, 5, 6], CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), 3).Select(a => a.Key));
    }

    [Fact]
    public void An_ask_with_only_foot_actions_has_its_primary_on_one()
        => Assert.Equal(1, CardActions.Numbered(CardActions.Of(AskOf(new Confirmation()), AskMode.Confirm, P), 0)[0].Key);

    [Fact]
    public void The_tenth_is_keyed_0_past_ten_no_key_and_never_a_duplicate()
    {
        var ask = AskOf(new Assignment([], [], 1), "declare_targets", allowPass: true);
        Assert.Equal([8, 9, 10], CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), 7).Select(a => a.Key));
        Assert.Equal([10, 0, 0], CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), 9).Select(a => a.Key));
        Assert.Equal([0, 0, 0], CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), 12).Select(a => a.Key));
        for (var tiles = 0; tiles <= 11; tiles++)
        {
            var keys = Enumerable.Range(1, Math.Min(tiles, 10)).Concat(CardActions.Numbered(CardActions.Of(ask, AskMode.Assign, P), tiles).Select(a => a.Key)).Where(k => k > 0).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void A_tip_is_the_word_and_its_key_or_the_reason_when_disabled()
    {
        Assert.Equal("Fire  1", new CardAction(CardActions.Fire, "Fire", "shoot", true, 1).Tip);
        Assert.Equal("Clear", new CardAction(CardActions.Clear, "Clear", "remove", false).Tip);
        Assert.Equal("Fire  1 · No target", new CardAction(CardActions.Fire, "Fire", "shoot", true, 1, false, "No target").Tip);
    }
}
