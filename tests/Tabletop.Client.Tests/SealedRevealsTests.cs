using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SealedRevealsTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Ask Sealed = Samples.Ask with { Id = Guid.CreateVersion7(), Teams = [Samples.Team1, Samples.Team2], Sealed = true };
    static readonly Ask Next = Samples.Ask with { Id = Guid.CreateVersion7() };
    static readonly Names N = new(Game, null);
    static IReadOnlyList<GameEvent> Batch(long seq, params EventPayload[] p) => p.Select((x, i) => new GameEvent(seq + i, null, x, null)).ToList();

    static IReadOnlyList<TeamAnswer> BothPassed => [new TeamAnswer(Samples.Team1, new Passed()), new TeamAnswer(Samples.Team2, new Passed())];

    /// <summary>A token spend closes and my next Ask (the Advance after the cast) opens in one frame: the spend is a note about the
    /// Ask that closed, so the next Ask's card never carries it, then or later.</summary>
    [Fact]
    public void A_sealed_close_and_my_next_card_in_one_frame_show_no_reveal_on_the_card()
    {
        var r = new SealedReveals();
        r.Read(Batch(1, new AskOpened(Sealed)), N);
        Assert.Null(r.Shown(card: false));
        r.Read(Batch(2, new AskClosed(Sealed.Id, BothPassed)), N);
        r.Read(Batch(3, new AskOpened(Next)), N);
        Assert.Null(r.Shown(card: true));
        Assert.Null(r.Shown(card: true));
        Assert.Null(r.Shown(card: false));                          // the card answered: the old reveal does not come back
    }

    /// <summary>With no card of mine up (the other team acts next), the reveal shows over the quiet bands until my next card.</summary>
    [Fact]
    public void A_reveal_with_no_card_stays_until_my_next_card_clears_it()
    {
        var r = new SealedReveals();
        r.Read(Batch(1, new AskOpened(Sealed), new AskClosed(Sealed.Id, BothPassed)), N);
        var reveal = Assert.Single(r.Shown(card: false)!);
        Assert.Equal([new RevealChip(1, "Team 1", "passed"), new RevealChip(2, "Team 2", "passed")], reveal);
        Assert.Same(r.Shown(card: false), r.Shown(card: false));
        Assert.Null(r.Shown(card: true));
        Assert.Null(r.Shown(card: false));
    }

    [Fact]
    public void Reveals_queue_in_order_and_an_unsealed_or_unanswered_close_reveals_nothing()
    {
        var r = new SealedReveals(); var second = Sealed with { Id = Guid.CreateVersion7() };
        r.Read(Batch(1, new AskOpened(Sealed), new AskOpened(second), new AskOpened(Next)), N);
        r.Read(Batch(4, new AskClosed(Next.Id, [new TeamAnswer(Samples.Team1, new Passed())]), new AskClosed(second.Id, [new TeamAnswer(Samples.Team2, new Passed())])), N);
        r.Read(Batch(6, new AskClosed(Sealed.Id, [new TeamAnswer(Samples.Team1, new Passed())])), N);
        Assert.Equal(["Team 2", "Team 1"], r.Shown(card: false)!.Select(x => Assert.Single(x).Team));
        var unanswered = Sealed with { Id = Guid.CreateVersion7() };
        Assert.Null(r.Shown(card: true));
        r.Read(Batch(7, new AskOpened(unanswered), new AskClosed(unanswered.Id, [])), N);
        Assert.Null(r.Shown(card: false));
    }
}
