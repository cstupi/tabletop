using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1DefenseTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Shot(string rule, double range, AttackKind kind = AttackKind.Shooting, string? action = null, IReadOnlyList<string>? terrain = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1, P("AP", 1)));
        var b = Unit("B", "4+", "4+", [], 1, [P(rule)]);
        var s = State([a], [b]);
        var weapons = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).Select(w => w.Id).ToList();
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, Lookup.AliveModels(s, a.Id), weapons), Party.Whole(s, b.Id), kind, action, range, terrain ?? []));
    }

    /// <summary>The rule rows only: the weapon's AP(1) is itself a -1 modifier on the defense roll.</summary>
    static int Mod(LiveSet l, Side side, string roll) => l.Effects(side, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll && x.Source.Entry.Name != "AP").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Fortified_takes_one_from_the_attackers_ap()
    {
        Assert.Equal(0, Shot("Fortified", 20).Parameter(Side.Attacker, "AP"));
    }

    /// <summary>Fortified and Guardian (3.5.1): "those hits count as having AP(-1), to a min. of AP(0)": both on one unit against AP(1) from over 9"
    /// take the AP to 0, not below it.</summary>
    [Fact]
    public void Fortified_and_guardian_together_hold_the_attackers_ap_at_zero()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1, P("AP", 1)));
        var b = Unit("B", "4+", "4+", [], 1, [P("Fortified"), P("Guardian")]);
        var s = State([a], [b]);
        var weapons = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).Select(w => w.Id).ToList();
        var live = LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, Lookup.AliveModels(s, a.Id), weapons), Party.Whole(s, b.Id), AttackKind.Shooting, null, 20, []));
        Assert.Equal(0, live.Parameter(Side.Attacker, "AP"));
    }

    [Fact]
    public void Grounded_stealth_is_minus_one_to_hit_near_terrain_only()
    {
        Assert.Equal(-1, Mod(Shot("Grounded Stealth", 20, terrain: ["near"]), Side.Attacker, "hit"));
        Assert.Equal(0, Mod(Shot("Grounded Stealth", 20), Side.Attacker, "hit"));
    }

    [Fact]
    public void Screened_and_guarded_act_on_shots_and_charges_from_over_nine_inches()
    {
        Assert.Equal((-1, 0), (Mod(Shot("Screened", 9.5), Side.Attacker, "hit"), Mod(Shot("Screened", 9), Side.Attacker, "hit")));
        Assert.Equal(-1, Mod(Shot("Screened", 10, AttackKind.Melee, GfNames.Charge), Side.Attacker, "hit"));
        Assert.Equal((1, 0), (Mod(Shot("Guarded", 9.5), Side.Defender, "defense"), Mod(Shot("Guarded", 9), Side.Defender, "defense")));
    }

    [Fact]
    public void Protected_rolls_a_six_plus_to_ignore_each_wound()
    {
        var roll = Assert.Single(RollEffects.OwnRolls(Shot("Protected", 20), Side.Defender, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }));
        Assert.Equal(("Protected", 6), (roll.Source.Entry.Name, roll.Roll.Threshold));
    }
}
