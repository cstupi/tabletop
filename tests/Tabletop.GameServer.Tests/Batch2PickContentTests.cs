using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 2's written-out Buffs, Marks and Debuffs as the shipped set holds them: each once row as a grant on the picked unit, read as the
/// books' spells of the same wording are.</summary>
public class Batch2PickContentTests
{
    static readonly PropertySet Core = TestProperties.Core;

    /// <summary>A (team 1, a 24" gun with AP(1) and one without) and B (team 2); the entry's once rows granted to `on`, as pick-rule writes them.</summary>
    static (GameState State, Guid A, Guid B, Guid Ap, Guid Plain) Granted(string entry, bool onA)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Ap Gun", "24\"", 1, P("AP", 1)), Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, [], Gun("Ap Gun", "24\"", 1, P("AP", 1)));
        var state = State([a], [b]);
        // A Buff's picker is a friend of the picked unit; a Mark's or a Debuff's is its enemy.
        var caster = entry.EndsWith("Buff") == onA ? a.Id : b.Id;
        foreach (var row in Core.Entry(entry)!.Effects.Where(e => e.Duration == EffectDuration.Once))
            state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new GrantAdded(onA ? a.Id : b.Id, new Grant(Guid.CreateVersion7(), row.Id, entry, caster, 1)), null));
        var weapons = state.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).ToList();
        return (state, a.Id, b.Id, weapons[0].Id, weapons[1].Id);
    }

    static LiveSet Attack(GameState s, Guid attacker, Guid defender, AttackKind kind, Guid? weapon = null) =>
        LiveProperties.Settle(new Interaction(s, Core, weapon is { } w ? Party.Of(s, attacker, Lookup.LivingModels(s, attacker), [w]) : Party.Whole(s, attacker), Party.Whole(s, defender), kind, null, 12, []));

    static int Mod(LiveSet l, Side side, string roll) => l.Effects(side, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Theory]
    [InlineData("Precision Attacks Buff", AttackKind.Shooting, 1)]
    [InlineData("Precision Attacks Buff", AttackKind.Melee, 1)]
    [InlineData("Precision Fighter Buff", AttackKind.Melee, 1)]
    [InlineData("Precision Fighter Buff", AttackKind.Shooting, 0)]
    [InlineData("Precision Shooter Buff", AttackKind.Shooting, 1)]
    [InlineData("Precision Shooter Buff", AttackKind.Melee, 0)]
    public void A_precision_buff_is_plus_one_to_hit_in_its_attack_kind(string entry, AttackKind kind, int expected)
    {
        var (s, a, b, _, _) = Granted(entry, onA: true);
        Assert.Equal(expected, Mod(Attack(s, a, b, kind), Side.Attacker, "hit"));
    }

    [Theory]
    [InlineData("Precision Fighting Mark", AttackKind.Melee, 1)]
    [InlineData("Precision Fighting Mark", AttackKind.Shooting, 0)]
    [InlineData("Precision Shooting Mark", AttackKind.Shooting, 1)]
    [InlineData("Precision Debuff", AttackKind.Melee, -1)]
    public void A_precision_mark_helps_its_attackers_and_a_precision_debuff_hinders_the_debuffed(string entry, AttackKind kind, int expected)
    {
        var mark = entry.EndsWith("Mark");
        var (s, a, b, _, _) = Granted(entry, onA: !mark);
        Assert.Equal(expected, mark ? Mod(Attack(s, a, b, kind), Side.Attacker, "hit") : Mod(Attack(s, a, b, kind), Side.Attacker, "hit"));
    }

    [Fact]
    public void Bane_and_shred_marks_give_their_attackers_the_rule_when_attacking()
    {
        foreach (var (entry, rule) in new[] { ("Bane Mark", "Bane"), ("Shred Mark", "Shred") })
        {
            var (s, a, b, _, _) = Granted(entry, onA: false);
            Assert.True(Attack(s, a, b, AttackKind.Melee).Has(Side.Attacker, rule));
            Assert.True(Attack(s, a, b, AttackKind.Shooting).Has(Side.Attacker, rule));
            Assert.False(Attack(s, a, b, AttackKind.Spell).Has(Side.Attacker, rule));
        }
    }

    [Fact]
    public void Piercing_debuff_takes_one_off_an_ap_weapon_and_gives_none_to_a_weapon_without()
    {
        var (s, a, b, ap, plain) = Granted("Piercing Debuff", onA: true);
        Assert.Equal(0, Attack(s, a, b, AttackKind.Shooting, ap).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack(s, a, b, AttackKind.Shooting, plain).Parameter(Side.Attacker, "AP"));
        var (s2, a2, b2, _, plain2) = Granted("Piercing Shooting Mark", onA: false);
        Assert.Equal(1, Attack(s2, a2, b2, AttackKind.Shooting, plain2).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack(s2, a2, b2, AttackKind.Melee, plain2).Parameter(Side.Attacker, "AP"));
    }

    [Fact]
    public void Range_and_speed_rows_read_as_their_stat()
    {
        var (s, a, b, _, plain) = Granted("Increased Shooting Range Buff", onA: true);
        Assert.Equal(30.0, StatReader.WeaponRange(Attack(s, a, b, AttackKind.Shooting), Side.Attacker, plain));
        var (s2, a2, b2, _, plain2) = Granted("Increased Shooting Range Mark", onA: false);
        Assert.Equal(30.0, StatReader.WeaponRange(Attack(s2, a2, b2, AttackKind.Shooting), Side.Attacker, plain2));
        var (s3, a3, b3, _, _) = Granted("Speed Buff", onA: true);
        var l3 = Attack(s3, a3, b3, AttackKind.Melee);
        Assert.Equal((8.0, 16.0, 16.0), (StatReader.ActionDistance(l3, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(l3, Side.Attacker, GfNames.Rush), StatReader.ActionDistance(l3, Side.Attacker, GfNames.Charge)));
        var (s4, a4, b4, _, _) = Granted("Speed Debuff", onA: true);
        Assert.Equal(4.0, StatReader.ActionDistance(Attack(s4, a4, b4, AttackKind.Melee), Side.Attacker, GfNames.Advance));
    }

    [Fact]
    public void Morale_and_casting_rows_modify_their_roll()
    {
        foreach (var (entry, roll, amount) in new[] { ("Courage Buff", "morale", 1), ("Morale Debuff", "morale", -1), ("Casting Buff", "casting", 1), ("Casting Debuff", "casting", -1) })
        {
            var (s, a, _, _, _) = Granted(entry, onA: true);
            Assert.Equal(amount, Mod(LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a), null, null, null, null, [])), Side.Attacker, roll));
        }
    }

    [Fact]
    public void An_aura_held_as_a_unit_property_gives_the_unit_its_rule_by_name()
    {
        var a = Unit("A", "4+", "4+", [P("Scout Aura")], 2, []);
        var state = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        Assert.True(Lookup.UnitHas(state, Core, a.Id, GfNames.Scout));
    }

    [Fact]
    public void Primeborn_reads_as_guardian()
    {
        Assert.Equal(Core.Entry("Guardian")!.Effects.Select(e => (e.Kind, e.Subject, e.Amount)), Core.Entry("Primeborn")!.Effects.Select(e => (e.Kind, e.Subject, e.Amount)));
    }
}
