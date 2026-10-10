using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class Batch1AurasTests
{
    static readonly PropertySet Core = TestProperties.Core;

    /// <summary>A three-model unit whose first model carries the Aura, attacking with every model or with the two that do not carry it.</summary>
    internal static LiveSet Holds(string aura, AttackKind kind, bool carrierAttacks = true, string? action = null)
    {
        var u = Unit("A", "4+", "4+", [], 3, [], Gun("Gun", "24\"", 1));
        var a = u with { Models = [u.Models[0] with { Properties = [P(aura)] }, .. u.Models.Skip(1)] };
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var models = carrierAttacks ? a.Models.Select(m => m.Id) : a.Models.Skip(1).Select(m => m.Id);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, models, []), Party.Whole(s, b.Id), kind, action, 12, []));
    }

    [Theory]
    [InlineData("Bane in Melee Aura", "Bane", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Bane when Shooting Aura", "Bane", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Cover Aura", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Cover when Shooting Aura", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Indirect when Shooting Aura", "Indirect", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Rending in Melee Aura", "Rending", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Rending when Shooting Aura", "Rending", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Shred in Melee Aura", "Shred", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred when Shooting Aura", "Shred", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Thrust in Melee Aura", "Thrust", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Unstoppable in Melee Aura", "Unstoppable", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Unstoppable when Shooting Aura", "Unstoppable", AttackKind.Shooting, AttackKind.Melee)]
    public void An_aura_gives_the_unit_its_rule_in_its_attack_kind_while_its_carrier_is_in_the_attack(string aura, string granted, AttackKind yes, AttackKind no)
    {
        Assert.True(Holds(aura, yes).Has(Side.Attacker, granted));
        Assert.False(Holds(aura, no).Has(Side.Attacker, granted));
        Assert.False(Holds(aura, yes, carrierAttacks: false).Has(Side.Attacker, granted));
    }
}
