using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class HeaderTilesTests
{
    [Fact]
    public void A_tile_per_team_in_ordinal_order_with_score_units_in_play_acted_the_turn_and_mine()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"), new ScoreSet(TableUx.T2, 3));
        var tiles = HeaderView.Tiles(p, TableUx.Game, new Names(TableUx.Game, p), [TableUx.T2]);
        Assert.Equal(new[] { 1, 2 }, tiles.Select(t => t.Ordinal));
        Assert.Equal(("Team 1", 0, 1, 1, false), (tiles[0].Name, tiles[0].Score, tiles[0].Acted, tiles[0].InPlay, tiles[0].Mine));   // Reserve is on the shelf: not in play
        Assert.Equal(("Team 2", 3, 0, 1, true), (tiles[1].Name, tiles[1].Score, tiles[1].Acted, tiles[1].InPlay, tiles[1].Mine));
        Assert.All(tiles, t => Assert.False(t.Acting));   // no turn started
        Assert.True(tiles[0].Dots);
        Assert.Equal("1/1", tiles[0].CountText);
    }

    [Fact]
    public void The_acting_team_is_the_scheduler_s_active_team()
    {
        var p = TableUx.After(TableUx.Deploy, new TurnStarted(TableUx.T2));
        var tiles = HeaderView.Tiles(p, TableUx.Game, new Names(TableUx.Game, p), []);
        Assert.Equal(new[] { false, true }, tiles.Select(t => t.Acting));
    }

    [Fact]
    public void Past_twelve_units_a_tile_draws_its_count_not_dots()
    {
        var t = new TeamTile(Guid.NewGuid(), 1, "Team 1", 0, 4, 13, false, false);
        Assert.False(t.Dots);
        Assert.Equal("4/13", t.CountText);
    }

    [Theory]
    [InlineData("Hold", "hold"), InlineData("Advance", "advance"), InlineData("rush", "rush"), InlineData("Charge", "charge"), InlineData("Shoot", "activation"), InlineData(null, "activation")]
    public void The_action_word_names_its_icon(string? action, string icon) => Assert.Equal(icon, HeaderView.ActionIcon(action));

    [Fact]
    public void The_round_reads_round_n_or_the_phase_before_round_one()
    {
        Assert.Equal("Round 2", HeaderView.RoundText(2, null));
        Assert.Equal("Deployment", HeaderView.RoundText(0, "deployment"));
        Assert.Equal("Before round 1", HeaderView.RoundText(0, null));
    }

    [Fact]
    public void A_tight_header_reads_the_round_as_its_number_and_keeps_the_phase()
    {
        Assert.Equal("4", HeaderView.RoundText(4, null, HeaderFit.Tight));
        Assert.Equal("Round 4", HeaderView.RoundText(4, null, HeaderFit.Compact));
        Assert.Equal("Deployment", HeaderView.RoundText(0, "deployment", HeaderFit.Tight));
    }

    [Fact]
    public void A_compact_step_strip_draws_only_the_mark_but_for_the_current_step()
    {
        OutlineItem done = new("Pick target", OutlineState.Done), current = new("Target moves", OutlineState.Current), next = new("Melee", OutlineState.Next);
        Assert.Equal(["✓ Pick target", "● Target moves", "○ Melee"], new[] { done, current, next }.Select(i => HeaderView.StepText(i, HeaderFit.Full)));
        Assert.Equal(["✓", "● Target moves", "○"], new[] { done, current, next }.Select(i => HeaderView.StepText(i, HeaderFit.Compact)));
        Assert.Equal(["✓", "● Target moves", "○"], new[] { done, current, next }.Select(i => HeaderView.StepText(i, HeaderFit.Tight)));
    }

    [Fact]
    public void Each_fit_is_tighter_than_the_one_before_until_the_last()
    {
        Assert.Equal(HeaderFit.Compact, HeaderView.Tighter(HeaderFit.Full));
        Assert.Equal(HeaderFit.Tight, HeaderView.Tighter(HeaderFit.Compact));
        Assert.Null(HeaderView.Tighter(HeaderFit.Tight));
    }

    static readonly Guid AskId = Guid.CreateVersion7();
    static Projection Waiting(params Guid[] teams) => TableUx.After(TableUx.Deploy,
        new AskOpened(new Ask(AskId, "remove_casualties", "Remove casualties", teams, new Confirmation(), false, false, null, null)));

    [Fact]
    public void Waiting_on_my_team_is_your_move_in_my_colour()
    {
        var p = Waiting(TableUx.T1, TableUx.T2);
        var w = HeaderView.Wait(p, TableUx.Game, new Names(TableUx.Game, p), [TableUx.T2]);
        Assert.Equal(new WaitLine(true, 2, "Your move"), w);
        var both = HeaderView.Wait(p, TableUx.Game, new Names(TableUx.Game, p), [TableUx.T1, TableUx.T2]);
        Assert.Equal(new WaitLine(true, 1, "Your move · Team 1"), both);
    }

    [Fact]
    public void Waiting_on_another_team_names_it_and_what_it_does_with_its_band()
    {
        var p = Waiting(TableUx.T2);
        Assert.Equal(new WaitLine(false, 2, "Waiting for Team 2 · removing casualties"), HeaderView.Wait(p, TableUx.Game, new Names(TableUx.Game, p), [TableUx.T1]));
        Assert.Equal(new WaitLine(false, 2, "Waiting for Team 2 · removing casualties"), HeaderView.Wait(p, TableUx.Game, new Names(TableUx.Game, p), []));
        Assert.Null(HeaderView.Wait(TableUx.After(TableUx.Deploy), TableUx.Game, new Names(TableUx.Game, TableUx.After()), [TableUx.T1]));
    }
}
