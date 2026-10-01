using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Weapon groups: one attack-sequence per weapon type in melee as in shooting (p.8, p.9), ordered Takedown, then Deadly, then the rest (p.14, p.13).</summary>
public class GfGroupsTests
{
    static SequenceContext MeleeOf(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target) =>
        new(attacker.Id, [target.Id], attacker.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Fact]
    public void A_melee_weapons_ap_and_deadly_apply_only_to_its_own_group()
    {
        // p.13 AP applies "when blocking hits from weapons with this special rule"; Deadly multiplies "each wound" of the weapon that has it.
        var attacker = Unit("A", "4+", "4+", [], 1, [], Melee("CCW", 1), Melee("Fist", 1, P("AP", 2), P("Deadly", 3)));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)]);
        // Deadly resolves first: Fist hits 6, blocks 5 against 6+ (AP 2) and fails; CCW hits 6, blocks 4 against 4+ and holds. Then morale dice.
        var sim = Sim.Gf(State([attacker], [defender], x1: -3, x2: 0), 6, 5, 6, 4, 6, 6);
        sim.Start(GfNames.Melee, MeleeOf(attacker, defender));
        var blocks = sim.Steps("roll-to-block").Select(s => s.Rolls.Single()).ToList();
        Assert.Contains(blocks[0].Modifiers, m => m.Source == "AP" && m.Amount == -2);
        Assert.DoesNotContain(blocks[1].Modifiers, m => m.Source == "AP");                          // the plain weapon's hit blocks with no AP
        Assert.Equal(3, sim.Model(defender.Models[0].Id).Wounds);                                   // only the Fist's wound is multiplied
        Assert.Equal(2, sim.Emitted.OfType<SequenceStarted>().Count(e => e.Sequence == GfNames.AttackSequence));
        Assert.Contains(GfNames.Fatigued, sim.Unit(attacker.Id).Conditions);                        // once, after the last group
    }

    [Fact]
    public void Melee_groups_go_takedown_then_deadly_then_the_rest_and_a_takedown_group_picks_its_model()
    {
        var attacker = Unit("A", "4+", "4+", [], 1, [], Melee("Aardvark", 1), Melee("Zed", 1, P("Deadly", 3)), Melee("Yak", 1, P("Takedown")));
        var defender = Unit("D", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([attacker], [defender], x1: -3, x2: 0), 1);
        sim.Start(GfNames.Melee, MeleeOf(attacker, defender));
        var ask = sim.AskFor(Samples.Team1);
        Assert.IsType<PieceSelection>(ask.Legal);
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Models[1].Id]));
        var names = sim.Emitted.OfType<SequenceStarted>().Where(e => e.Sequence == GfNames.AttackSequence)
            .Select(e => attacker.Models[0].Weapons.Single(w => w.Id == e.Context.WeaponIds.Single()).Name).ToList();
        Assert.Equal(["Yak", "Zed", "Aardvark"], names);
    }

    [Fact]
    public void Shooting_groups_go_takedown_then_deadly_then_the_rest()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Aardvark", "24\"", 1), Gun("Zed", "24\"", 1, P("Deadly", 3)));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Aardvark"] = t.Id, ["Zed"] = t.Id }));
        var first = sim.Emitted.OfType<SequenceStarted>().First(e => e.Sequence == GfNames.AttackSequence);
        Assert.Equal("Zed", shooter.Models[0].Weapons.Single(w => w.Id == first.Context.WeaponIds.Single()).Name);
    }

    [Fact]
    public void A_limited_group_does_not_drop_the_groups_after_it()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("A Missile", "24\"", 1, P("Limited")), Gun("B Rifle", "24\"", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["A Missile"] = t.Id, ["B Rifle"] = t.Id }));
        Assert.Equal(2, sim.Emitted.OfType<SequenceStarted>().Count(e => e.Sequence == GfNames.AttackSequence));
        // A second shooting excludes the Missile, which the first one used.
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        Assert.Equal(["B Rifle"], GfShooting.Groups(sim.State, TestProperties.Core, shooter.Id, null).Select(g => g.Name));
    }
    [Fact]
    public void A_unit_whose_only_ranged_weapon_is_a_spent_limited_one_is_not_asked_to_shoot()
    {
        // p.14 Limited: "may only be used once per game". The previous shooting fired it, so a later Hold has nothing to fire and asks nothing.
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Missile", "24\"", 1, P("Limited")));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Missile"] = t.Id }));
        Assert.Empty(sim.State.Projection.Stack);
        sim.Start(GfNames.HoldSeq, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
