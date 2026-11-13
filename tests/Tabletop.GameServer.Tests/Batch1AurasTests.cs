using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class Batch1AurasTests
{
    static readonly PropertySet Core = TestProperties.Core;

    /// <summary>A three-model unit whose first model carries the Aura, attacking with every model or with the two that do not carry it ("This model and its unit").</summary>
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
    public void An_aura_gives_the_unit_its_rule_in_its_attack_kind_whichever_of_its_models_attack(string aura, string granted, AttackKind yes, AttackKind no)
    {
        Assert.True(Holds(aura, yes).Has(Side.Attacker, granted));
        Assert.False(Holds(aura, no).Has(Side.Attacker, granted));
        Assert.True(Holds(aura, yes, carrierAttacks: false).Has(Side.Attacker, granted));
    }

    static int Mod(LiveSet l, string roll) => l.Effects(roll == "defense" ? Side.Defender : Side.Attacker, EffectKind.ModifyRoll)
        .Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Courage_aura_is_plus_one_to_morale_and_the_move_auras_lengthen_moves()
    {
        Assert.Equal(1, Mod(Holds("Courage Aura", AttackKind.Shooting), "morale"));
        var fast = Holds("Fast Aura", AttackKind.Shooting);
        Assert.Equal((8.0, 16.0, 16.0), (StatReader.ActionDistance(fast, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(fast, Side.Attacker, GfNames.Rush), StatReader.ActionDistance(fast, Side.Attacker, GfNames.Charge)));
        Assert.Equal(16.0, StatReader.ActionDistance(Holds("Rapid Charge Aura", AttackKind.Melee), Side.Attacker, GfNames.Charge));
        Assert.Equal(8.0, StatReader.ActionDistance(Holds("Fast Aura", AttackKind.Shooting, carrierAttacks: false), Side.Attacker, GfNames.Advance));
    }

    [Fact]
    public void Increased_shooting_range_aura_adds_six_inches_when_shooting()
    {
        var u = Unit("A", "4+", "4+", [], 3, [], Gun("Gun", "24\"", 1));
        var a = u with { Models = [u.Models[0] with { Properties = [P("Increased Shooting Range Aura")] }, .. u.Models.Skip(1)] };
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var gun = s.Projection.Models.Values.First(m => m.UnitId == a.Id && m.Slot.Ordinal == 2).Slot.Weapons.Single().Id;   // a model without the Aura
        var live = LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), AttackKind.Shooting, null, 20, []));
        Assert.Equal(30.0, StatReader.WeaponRange(live, Side.Attacker, gun));
    }

    [Fact]
    public void Piercing_and_precision_auras_act_in_their_attack_kind()
    {
        Assert.Equal(((int?)1, (int?)null), (Holds("Piercing Fighter Aura", AttackKind.Melee).Parameter(Side.Attacker, "AP"), Holds("Piercing Fighter Aura", AttackKind.Shooting).Parameter(Side.Attacker, "AP")));
        Assert.Equal(((int?)1, (int?)null), (Holds("Piercing Shooter Aura", AttackKind.Shooting).Parameter(Side.Attacker, "AP"), Holds("Piercing Shooter Aura", AttackKind.Melee).Parameter(Side.Attacker, "AP")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Charge Aura", AttackKind.Melee, action: GfNames.Charge), "hit"), Mod(Holds("Precision Charge Aura", AttackKind.Melee), "hit")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Fighter Aura", AttackKind.Melee), "hit"), Mod(Holds("Precision Fighter Aura", AttackKind.Shooting), "hit")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Shooter Aura", AttackKind.Shooting), "hit"), Mod(Holds("Precision Shooter Aura", AttackKind.Melee), "hit")));
        Assert.Equal(1, Mod(Holds("Precision Shooter Aura", AttackKind.Shooting, carrierAttacks: false), "hit"));
    }
}
