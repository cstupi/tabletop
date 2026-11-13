using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>A Shooter Mob with a joined Boss Champ, as Army Forge list nsYPyI5HHwNM imports: which rules are live for whose weapons (3.6).</summary>
public class GoblinReclaimersListTests
{
    static (GameState State, Unit Mob, Unit Enemy) Table()
    {
        var mob = Unit("Shooter Mob", "5+", "6+", [], 5, [P("Mischievous"), P("Good Shot"), P("Instinctive")], Gun("Scrap Gun", "24\"", 1), Melee("Strike", 1));
        var models = mob.Models.ToList();
        models[0] = models[0] with { Properties = [.. models[0].Properties, P("Regeneration Aura")] };
        models[1] = models[1] with { Properties = [.. models[1].Properties, P("Bravery Aura")] };
        models[4] = models[4] with
        {
            Properties = [P("Hero"), P("Mischievous"), P("Tough", 3), P("Enhanced Mischievous Aura")],
            Weapons = [Gun("Boss Pistol", "9\"", 1, P("Anti-Cover"), P("Assault"), P("Surge")), Melee("Hand Weapon", 3)],
            Stats = new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" },
        };
        var a = mob with { Models = models };
        var e = Unit("E", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        return (State([a], [e], x1: -4, x2: 4).Under(RuleSets.Gf360), a, e);
    }

    static LiveSet Shot(GameState s, Unit from, Unit at, Weapon w) =>
        LiveProperties.Settle(GfAttack.AttackInteraction(s, TestProperties.Core36, null, from.Id, at.Id, [w.Id], AttackKind.Shooting, GfNames.Hold));

    [Fact]
    public void The_mobs_guns_get_good_shot_mischievous_and_the_boss_aura_and_the_boss_pistol_gets_no_good_shot()
    {
        var (s, a, e) = Table();
        var gun = Shot(s, a, e, a.Models[0].Weapons[0]);
        Assert.True(gun.Has(Side.Attacker, "Good Shot"));
        Assert.True(gun.Has(Side.Attacker, "Mischievous"));
        Assert.True(gun.Has(Side.Attacker, "Enhanced Mischievous"));
        var pistol = Shot(s, a, e, a.Models[4].Weapons[0]);
        Assert.False(pistol.Has(Side.Attacker, "Good Shot"));
        Assert.True(pistol.Has(Side.Attacker, "Mischievous"));
        Assert.True(pistol.Has(Side.Attacker, "Enhanced Mischievous"));
    }

    [Fact]
    public void One_models_auras_cover_the_whole_unit()
    {
        var (s, a, e) = Table();
        var shotAt = Shot(s, e, a, e.Models[0].Weapons[0]);
        Assert.True(shotAt.Has(Side.Defender, "Regeneration"));
        Assert.True(Lookup.UnitHas(s, TestProperties.Core36, a.Id, "Bravery Aura"));
    }

    [Fact]
    public void An_aura_goes_when_its_carrier_is_removed()
    {
        var (s, a, e) = Table();
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(a.Models[4].Id, s.Projection.Models[a.Models[4].Id].Location, new OnShelf())]), null));   // the Boss Champ is a casualty
        Assert.False(Shot(s, a, e, a.Models[0].Weapons[0]).Has(Side.Attacker, "Enhanced Mischievous"));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(a.Models[0].Id, s.Projection.Models[a.Models[0].Id].Location, new OnShelf())]), null));   // the Regeneration Aura's carrier
        Assert.False(Shot(s, e, a, e.Models[0].Weapons[0]).Has(Side.Defender, "Regeneration"));
    }
}
