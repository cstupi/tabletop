using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1HitAndApTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind kind, double range, string? action = null, int tough = 3)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 3, [P("Tough", tough)]);
        var s = State([a], [b]);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), kind, action, range, []));
    }

    static int Hit(LiveSet l) => l.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
    static int OnSix(LiveSet l, TargetQuantity quantity) => l.Effects(Side.Attacker, EffectKind.AddHit, new DieContext("hit", 6, 6, true))
        .Where(x => (x.Effect.TargetQuantity ?? TargetQuantity.Hits) == quantity).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Good_shot_is_plus_one_shooting_and_precise_plus_one_attacking()
    {
        Assert.Equal((1, 0), (Hit(Attack("Good Shot", AttackKind.Shooting, 20)), Hit(Attack("Good Shot", AttackKind.Melee, 1))));
        Assert.Equal((1, 1), (Hit(Attack("Precise", AttackKind.Shooting, 20)), Hit(Attack("Precise", AttackKind.Melee, 1))));
    }

    [Fact]
    public void Point_blank_surge_adds_a_hit_on_a_six_within_twelve_inches_and_sergeant_on_any_attack()
    {
        Assert.Equal((1, 0), (OnSix(Attack("Point-Blank Surge", AttackKind.Shooting, 12), TargetQuantity.Hits), OnSix(Attack("Point-Blank Surge", AttackKind.Shooting, 12.5), TargetQuantity.Hits)));
        Assert.Equal((1, 1), (OnSix(Attack("Sergeant", AttackKind.Shooting, 20), TargetQuantity.Hits), OnSix(Attack("Sergeant", AttackKind.Melee, 1), TargetQuantity.Hits)));
    }

    [Fact]
    public void Predator_rules_add_an_attack_on_a_six_in_their_attack_kind()
    {
        Assert.Equal((1, 0), (OnSix(Attack("Predator Fighter", AttackKind.Melee, 1), TargetQuantity.Attacks), OnSix(Attack("Predator Fighter", AttackKind.Shooting, 20), TargetQuantity.Attacks)));
        Assert.Equal((1, 0), (OnSix(Attack("Predator Shooter", AttackKind.Shooting, 20), TargetQuantity.Attacks), OnSix(Attack("Predator Shooter", AttackKind.Melee, 1), TargetQuantity.Attacks)));
    }

    [Fact]
    public void Slayers_and_point_blank_piercing_grant_ap_on_their_occasions_only()
    {
        Assert.Equal(2, Attack("Melee Slayer", AttackKind.Melee, 1, GfNames.Charge).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Melee Slayer", AttackKind.Melee, 1, GfNames.Charge, tough: 1).Parameter(Side.Attacker, "AP"));
        Assert.Equal(2, Attack("Ranged Slayer", AttackKind.Shooting, 9.5).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Ranged Slayer", AttackKind.Shooting, 9).Parameter(Side.Attacker, "AP"));
        Assert.Equal(1, Attack("Point-Blank Piercing", AttackKind.Shooting, 12).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Point-Blank Piercing", AttackKind.Shooting, 12.5).Parameter(Side.Attacker, "AP"));
    }
}
