using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The 3.6 hand part's weapon and attack rules that the engine reads by name.</summary>
public class ArmyRules36ATests
{
    static readonly PropertySet Core36 = TestProperties.Core36;

    /// <summary>"Ravage" (3.6.0): "Once per round, ... roll X dice before attacking, unless this unit is Fatigued." No step plays the 3.6 text, so its
    /// 3.6 entry is text-only and a 3.6 charger with Ravage rolls no Ravage dice.</summary>
    [Fact]
    public void Text_only_ravage_rolls_no_dice_under_3_6()
    {
        Assert.False(Core36.Entry("Ravage")!.Played);
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), [6, 6, .. Enumerable.Repeat(1, 20)]);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        Assert.DoesNotContain(sim.Emitted.OfType<StepCompleted>(), s => s.Rolls.Any(r => r.Roll == "Ravage"));
    }
}
