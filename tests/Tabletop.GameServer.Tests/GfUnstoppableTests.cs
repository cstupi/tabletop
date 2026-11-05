using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.15 Unstoppable: "Ignores Regeneration, and ignores all negative modifiers to this weapon." The shooter's own penalties are negative
/// modifiers to its weapon too (owner ruling: an Unstoppable weapon ignores Bad Shot).
/// </summary>
public class GfUnstoppableTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality");

    static RollPlan HitPlan(Tabletop.Content.Unit shooter, string action = "Hold")
    {
        var target = Unit("T", "4+", "4+", [], 2, []);
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons),
            Party.Whole(state, target.Id), AttackKind.Shooting, action, 12, []));
        return RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality).Value, [], D6, null, "hit", "defense");
    }

    [Fact]
    public void An_unstoppable_weapon_ignores_bad_shot()
    {
        Assert.Equal(-1, HitPlan(Unit("S", "4+", "4+", [], 1, [P("Bad Shot")], Gun("Rifle", "24\"", 1))).NetModifier);
        var plan = HitPlan(Unit("S", "4+", "4+", [], 1, [P("Bad Shot")], Gun("Beam", "24\"", 1, P("Unstoppable"))));
        Assert.Equal((4, 0), (plan.Threshold, plan.NetModifier));
        var aura = HitPlan(Unit("S", "4+", "4+", [], 2, [P("Bad Shot"), P("Unstoppable when Shooting Aura")], Gun("Rifle", "24\"", 1)));
        Assert.Equal(0, aura.NetModifier);                                                       // Unstoppable granted to the unit's shooting
        Assert.Equal(-1, HitPlan(Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "24\"", 1, P("Indirect"))), "Advance").NetModifier);
        Assert.Equal(-1, HitPlan(Unit("S", "4+", "4+", [], 2, [P("Bad Shot")], Gun("Rifle", "24\"", 1))).NetModifier);
        var indirect = HitPlan(Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "24\"", 1, P("Indirect"), P("Unstoppable"))), "Advance");
        Assert.Equal(0, indirect.NetModifier);                                                   // p.14 Indirect's -1 after moving is a negative modifier to the weapon too
    }

    [Fact]
    public void A_unit_with_bad_shot_firing_a_railgun_and_a_rifle_takes_minus_one_only_on_the_rifle()
    {
        // The imported Specialists carry Bad Shot on the unit and on each model; their Railgun is AP(2), Unstoppable.
        var shooter = Unit("Specialists", "5+", "5+", [P("Bad Shot")], 2, [P("Bad Shot")], Gun("Railgun", "24\"", 1, P("AP", 2), P("Unstoppable")), Gun("Rifle", "24\"", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), Enumerable.Repeat(1, 40).ToArray());
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Railgun"] = t.Id, ["Rifle"] = t.Id }));
        var groups = sim.Emitted.OfType<SequenceStarted>().Where(e => e.Sequence == GfNames.AttackSequence)
            .Select(e => shooter.Models[0].Weapons.Concat(shooter.Models[1].Weapons).First(w => e.Context.WeaponIds.Contains(w.Id)).Name).ToList();
        var hits = sim.Steps("roll-to-hit").Select(s => s.Rolls.Single()).ToList();
        Assert.Equal(2, hits.Count);
        var byWeapon = groups.Zip(hits).ToDictionary(x => x.First, x => x.Second);
        Assert.DoesNotContain(byWeapon["Railgun"].Modifiers, m => m.Amount < 0);
        Assert.Contains(byWeapon["Rifle"].Modifiers, m => m.Source == "Bad Shot" && m.Amount == -1);
    }
}
