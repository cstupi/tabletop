using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class TurnBannersTests
{
    static readonly Names N = new(TableUx.Game, null);
    static IReadOnlyList<GameEvent> B(params EventPayload[] e) => TableUx.Events(e);

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void A_turn_banner_only_when_the_team_changes()
    {
        var t = new TurnBanners();
        Assert.Equal(new Banner[] { new("Round 2", 1.5), new("Team 1's turn", 1.0, Team: 1) }, t.OnBatch(B(new RoundStarted(2, [TableUx.T1, TableUx.T2], []), new TurnStarted(TableUx.T1)), N, true, false));
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1)), N, true, false));
        Assert.Equal(("Team 2's turn", 2), t.OnBatch(B(new TurnStarted(TableUx.T2)), N, true, false).Single() is var b ? (b.Text, b.Team) : default);
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1)), N, false, false));                     // off: nothing shown, the team still tracked
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T1), new RoundStarted(0, [], [])), N, true, false));   // round 0 is deployment
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void The_catch_up_batch_shows_nothing()
    {
        var t = new TurnBanners(); t.OnBoard();
        Assert.Empty(t.OnBatch(B(new RoundStarted(1, [], []), new TurnStarted(TableUx.T1)), N, true, true));
        Assert.Empty(t.OnBatch(B(new TurnStarted(TableUx.T2)), N, true, true));
        Assert.Equal("Round 2", t.OnBatch(B(new RoundStarted(2, [], []), new TurnStarted(TableUx.T2)), N, true, false).Single().Text);
    }
}
