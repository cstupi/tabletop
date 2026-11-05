using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Tests;

/// <summary>GfEdition: what differs between Grimdark Future 3.5.1 and 3.6.0, read from GameInfo.GameRules (rule-set versions spec → Where the rules live).</summary>
public class GfEditionTests
{
    static GameState StateWithRules(string id)
    {
        var state = Infrastructure.Armies.State([], []);
        state.Info = state.Info with { GameRules = id };
        return state;
    }

    [Fact]
    public void Of_reads_the_games_rules_id()
    {
        Assert.Same(GfEdition.V360, GfEdition.Of(StateWithRules("grimdark-future-3.6")));
        Assert.Same(GfEdition.V351, GfEdition.Of(StateWithRules("grimdark-future")));
        Assert.Throws<UnknownGameRulesException>(() => GfEdition.Of("grimdark-future-3.7"));
    }

    [Fact]
    public void The_3_5_1_values_are_todays_constants()
    {
        var e = GfEdition.V351;
        Assert.Equal((GfNames.MeleeHorizontal, GfNames.MeleeVertical, GfNames.ScoutReach), (e.MeleeHorizontal, e.MeleeVertical, e.ScoutReach));
        Assert.True(e.ArtilleryHoldOnly && e.FatiguedSixesTrigger && e.CounterReducesImpact);
        Assert.Equal("Grimdark Future v3.5.1", e.Name);
    }

    [Fact]
    public void The_3_6_0_values_are_the_rules_diffs()
    {
        var e = GfEdition.V360;
        Assert.Equal((3.0, 3.0, 9.0, 9.0), (e.MeleeHorizontal, e.MeleeVertical, e.ScoutReach, e.ScoutClearance));
        Assert.Equal(9, e.HeroAuraTough);
        Assert.Equal((2.0 / 3, 0.75), (e.SideBattleLong, e.SideBattleShort));
        Assert.Equal("Grimdark Future v3.6.0", e.Name);
    }

    [Fact]
    public void The_option_catalog_cites_the_editions_page()
    {
        Assert.Contains("p.56", GrimdarkFutureRules.V351.OptionCatalog.Options.Single(o => o.Key == "line_of_sight").Page);
        Assert.Contains("3.6.0 p.58", GrimdarkFutureRules.V360.OptionCatalog.Options.Single(o => o.Key == "line_of_sight").Page);
    }

    /// <summary>Welcome carries IGameRules.Zones: Side Battle's legs, 3.5.1 p.22 (54", 30") and 3.6.0 p.21 (48", 36") on 72×48.</summary>
    [Fact]
    public void The_zone_legs_are_the_editions()
    {
        Assert.Equal(Tabletop.Geometry.ZoneLegs.Default, GrimdarkFutureRules.V351.Zones);
        Assert.Equal(new Tabletop.Geometry.ZoneLegs(2.0 / 3, 0.75), GrimdarkFutureRules.V360.Zones);
    }
}
