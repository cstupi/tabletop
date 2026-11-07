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

    /// <summary>A sealed close and my next Ask in one frame: one coalesced render still gets the reveal, as a per-batch render did.</summary>
    [Fact]
    public void A_sealed_close_and_a_new_ask_in_one_frame_keep_the_reveal()
    {
        var r = new SealedReveals();
        r.Read(Batch(1, new AskOpened(Sealed)), N);
        Assert.Empty(r.Drain());
        r.Read(Batch(2, new AskClosed(Sealed.Id, [new TeamAnswer(Samples.Team1, new Passed()), new TeamAnswer(Samples.Team2, new Passed())])), N);
        r.Read(Batch(3, new AskOpened(Next)), N);
        var reveal = Assert.Single(r.Drain());
        Assert.Equal([new RevealChip(1, "Team 1", "passed"), new RevealChip(2, "Team 2", "passed")], reveal);
        Assert.Empty(r.Drain());                                    // shown once, then gone
    }

    [Fact]
    public void Reveals_queue_in_order_and_an_unsealed_or_unanswered_close_reveals_nothing()
    {
        var r = new SealedReveals(); var second = Sealed with { Id = Guid.CreateVersion7() };
        r.Read(Batch(1, new AskOpened(Sealed), new AskOpened(second), new AskOpened(Next)), N);
        r.Read(Batch(4, new AskClosed(Next.Id, [new TeamAnswer(Samples.Team1, new Passed())]), new AskClosed(second.Id, [new TeamAnswer(Samples.Team2, new Passed())])), N);
        r.Read(Batch(6, new AskClosed(Sealed.Id, [new TeamAnswer(Samples.Team1, new Passed())])), N);
        Assert.Equal(["Team 2", "Team 1"], r.Drain().Select(x => Assert.Single(x).Team));
        var unanswered = Sealed with { Id = Guid.CreateVersion7() };
        r.Read(Batch(7, new AskOpened(unanswered), new AskClosed(unanswered.Id, [])), N);
        Assert.Empty(r.Drain());
    }
}
