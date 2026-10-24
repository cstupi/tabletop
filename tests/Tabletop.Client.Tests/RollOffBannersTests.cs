using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>Roll-off result spec → The message: "<team> goes first" for 4 s, each team's roll under it, best first.</summary>
public class RollOffBannersTests
{
    static readonly Names N = new(TableUx.Game, null);
    static IReadOnlyList<GameEvent> B(params EventPayload[] e) => TableUx.Events(e);
    static StepCompleted Ranked(params (string Key, int Value)[] values) =>
        new(Guid.CreateVersion7(), "order", null, [], []) { Values = values.ToDictionary(v => v.Key, v => v.Value) };

    [Fact]
    public void The_ranking_step_names_the_first_team_and_each_teams_roll_best_first()
    {
        var batch = B(new StepCompleted(Guid.CreateVersion7(), "roll-off", null, [], []),
            Ranked(("winner_ordinal", 2), ("place_1", 2), ("place_2", 1), ("rolled_1_1", 3), ("rolled_2_1", 5)));
        Assert.Equal(new Banner("Team 2 goes first", 4.0, "Team 2 rolled 5 · Team 1 rolled 3"), RollOffBanners.Of(batch, N, true, false));
    }

    [Fact]
    public void A_tie_and_its_re_roll_show_as_the_roll_off_resolved_them()
    {
        var batch = B(Ranked(("place_1", 1), ("place_2", 2), ("rolled_1_1", 4), ("rolled_1_2", 6), ("rolled_2_1", 4), ("rolled_2_2", 2)));
        Assert.Equal("Team 1 rolled 4, 6 · Team 2 rolled 4, 2", RollOffBanners.Of(batch, N, true, false)!.Detail);
    }

    [Fact]
    public void Nothing_while_catching_up_switched_off_after_an_undo_or_without_a_ranking()
    {
        var ranked = Ranked(("place_1", 1), ("place_2", 2), ("rolled_1_1", 6), ("rolled_2_1", 1));
        Assert.Null(RollOffBanners.Of(B(ranked), N, true, true));                       // a reconnect's catch-up
        Assert.Null(RollOffBanners.Of(B(ranked), N, false, false));                     // the Turn banner setting, Animate, or a replay
        Assert.Null(RollOffBanners.Of(B(ranked, new Undone(0)), N, true, false));
        Assert.Null(RollOffBanners.Of(B(new StepCompleted(Guid.CreateVersion7(), "hit", null, [], []) { Values = new Dictionary<string, int> { ["x"] = 1 } }), N, true, false));
    }

    [Fact]
    public void A_ranking_from_before_the_faces_were_named_shows_the_first_team_alone()
    {
        Assert.Equal(new Banner("Team 1 goes first", 4.0, null), RollOffBanners.Of(B(Ranked(("place_1", 1), ("place_2", 2))), N, true, false));
    }
}
