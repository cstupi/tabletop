using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    /// <summary>The move Ask shows the cap ApplyMove enforces: Strider and Flying are not held to the difficult-terrain cap (final review M5).</summary>
    [Theory]
    [InlineData("Strider", true)]
    [InlineData("Flying", true)]
    [InlineData(null, false)]
    public void A_hit_and_run_move_Ask_carries_the_difficult_cap_the_move_will_be_held_to(string? rule, bool exempt)
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var defender = Unit("D", "4+", "4+", [P("Hit & Run Fighter"), .. rule is null ? Array.Empty<Tabletop.Content.PropertyInstance>() : [P(rule)]], 1, [], Melee("Spear", 1));
        var sim = MeleeSim(charger, defender);
        sim.Answer(Samples.Team2, new Chosen($"Hit & Run Fighter|{defender.Id:N}"));
        Assert.Equal(exempt ? null : GfDifficultCap, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).DifficultCap);
    }

    static double? GfDifficultCap => Tabletop.GameServer.Rules.GrimdarkFuture.GfNames.DifficultCap;
}
