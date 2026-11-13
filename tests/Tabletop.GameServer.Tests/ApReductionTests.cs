using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Fortified, Guardian, Fortified Growth, Primeborn: "those hits count as having AP(-1), to a min. of AP(0)"; Piercing Debuff: "loses AP(1) on its weapons".
/// The reduction comes off the hit's AP once, however many rules gave it AP, and off AP a die or Impact adds after the hit is scored.
/// </summary>
public class ApReductionTests
{
    static LiveSet Settle(Unit a, Unit b, AttackKind kind, string? action, double range)
    {
        var s = State([a], [b]).Under(RuleSets.Gf360);
        var weapons = a.Models.SelectMany(m => m.Weapons).Select(w => w.Id).ToList();
        return LiveProperties.Settle(new Interaction(s, TestProperties.Core36, Party.Of(s, a.Id, a.Models.Select(m => m.Id), weapons), Party.Whole(s, b.Id), kind, action, range, []));
    }

    /// <summary>The AP the defense roll reads: the sum of the AP rows' modifiers, negated.</summary>
    static int Ap(LiveSet live) => -live.Effects(Side.Defender, EffectKind.ModifyRoll).Where(x => x.Source.Entry.Name == "AP").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void An_ap_1_weapon_with_piercing_charge_into_fortified_is_ap_1()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Piercing Charge")], Melee("CCW", 1, P("AP", 1)));
        Assert.Equal(1, Ap(Settle(a, Unit("B", "4+", "4+", [], 1, [P("Fortified")]), AttackKind.Melee, GfNames.Charge, 3)));
    }

    [Fact]
    public void An_ap_1_gun_on_a_havocbound_unit_into_fortified_from_12_inches_is_ap_1()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Havocbound")], Gun("Gun", "24\"", 1, P("AP", 1)));
        Assert.Equal(1, Ap(Settle(a, Unit("B", "4+", "4+", [], 1, [P("Fortified")]), AttackKind.Shooting, GfNames.Hold, 12)));
    }

    [Fact]
    public void A_rending_six_from_a_weapon_with_no_ap_into_primeborn_is_ap_3()
    {
        // Rending: "on unmodified results of 6 to hit, those hits get AP(+4)": a per-die grant, added once the hit is scored.
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1, P("Rending")));
        var live = Settle(a, Unit("B", "4+", "4+", [], 1, [P("Primeborn")]), AttackKind.Shooting, GfNames.Hold, 12);
        Assert.Equal(3, Ap(live.WithGranted(Side.Attacker, "AP", 4)));
    }

    [Fact]
    public void Two_reductions_hold_the_total_at_zero()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Piercing Charge")], Melee("CCW", 1, P("AP", 1)));
        Assert.Equal(0, Ap(Settle(a, Unit("B", "4+", "4+", [], 1, [P("Fortified"), P("Primeborn")]), AttackKind.Melee, GfNames.Charge, 3)));
    }

    [Fact]
    public void Heavy_impact_hits_into_fortified_are_ap_0()
    {
        var chargers = Unit("C", "4+", "4+", [], 1, [P("Heavy Impact", 1)], Melee("CCW", 1));
        var sim = Sim.Gf36(State([chargers], [Unit("T", "4+", "2+", [], 3, [P("Fortified")])], x1: -3, x2: 0), 6);
        sim.Start(GfNames.Melee, new SequenceContext(chargers.Id, [sim.State.Projection.Units.Keys.First(k => k != chargers.Id)], chargers.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        var impact = sim.Steps("roll-to-block").First().Rolls.Single();
        Assert.Equal(0, impact.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
    }
}
