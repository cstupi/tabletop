using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1GrantsTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind kind, PropertyInstance[]? weaponRules = null, PropertyInstance[]? defender = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, weaponRules is null ? [P(rule)] : [], Gun("Gun", "24\"", 1, weaponRules ?? []));
        var b = Unit("B", "4+", "4+", [], 1, defender ?? []);
        var s = State([a], [b]);
        var weapons = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).Select(w => w.Id).ToList();
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, Lookup.AliveModels(s, a.Id), weapons), Party.Whole(s, b.Id), kind, null, 12, []));
    }

    [Theory]
    [InlineData("Bane in Melee", "Bane", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Rending in Melee", "Rending", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred in Melee", "Shred", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred when Shooting", "Shred", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Surge when Shooting", "Surge", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Unstoppable in Melee", "Unstoppable", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Ignores Cover when Shooting", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Regeneration in Melee", "Ignores Regeneration", AttackKind.Melee, AttackKind.Shooting)]
    public void A_model_rule_grants_its_weapon_rule_in_its_attack_kind_only(string rule, string granted, AttackKind yes, AttackKind no)
    {
        Assert.True(Attack(rule, yes).Has(Side.Attacker, granted));
        Assert.False(Attack(rule, no).Has(Side.Attacker, granted));
    }

    [Fact]
    public void Ignores_cover_suppresses_cover_and_ignores_regeneration_removes_the_targets_regeneration()
    {
        Assert.Contains(TargetModifier.Cover, Attack("", AttackKind.Shooting, [P("Ignores Cover")]).SuppressedModifiers);
        Assert.False(Attack("", AttackKind.Shooting, [P("Ignores Regeneration")], [P("Regeneration")]).Has(Side.Defender, "Regeneration"));
        Assert.Equal(CatalogKind.Weapon, Core.Entry("Ignores Cover")!.Kind);
        Assert.Equal(CatalogKind.Weapon, Core.Entry("Ignores Regeneration")!.Kind);
    }
}
