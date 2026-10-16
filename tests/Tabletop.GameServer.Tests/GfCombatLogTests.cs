using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Combat-log spec → What the server adds: the rule behind a modifier (RollModifier.Via) and what a die set off (Die.Triggered).</summary>
public class GfCombatLogTests
{
    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t, string action) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    [Fact]
    public void A_rending_six_blocks_at_ap_one_plus_rending_ap_four_and_the_weapons_own_ap_has_no_grantor()
    {
        var render = Unit("Render", "4+", "4+", [], 2, [], Gun("Saw", "12\"", 1, P("AP", 1), P("Rending")));
        var target = Unit("T", "4+", "2+", [], 3, []);
        var sim = Sim.Gf(State([render], [target], x1: -4, x2: 4), 6, 4, 5, 5);
        sim.Start(GfNames.AttackSequence, Attack(render, target, GfNames.Hold));
        var blocks = sim.Steps("roll-to-block").Single().Rolls;
        Assert.Contains(blocks, r => r.Modifiers.SequenceEqual([new RollModifier("AP", -1), new RollModifier("AP", -4, "Rending")]));
        Assert.Contains(blocks, r => r.Modifiers.SequenceEqual([new RollModifier("AP", -1)]));
    }

    [Fact]
    public void A_rending_six_to_hit_is_marked_with_rending_and_a_four_with_nothing()
    {
        var render = Unit("Render", "4+", "4+", [], 2, [], Gun("Saw", "12\"", 1, P("Rending")));
        var target = Unit("T", "4+", "2+", [], 3, []);
        var sim = Sim.Gf(State([render], [target], x1: -4, x2: 4), 6, 4, 5, 5);
        sim.Start(GfNames.AttackSequence, Attack(render, target, GfNames.Hold));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal(["Rending"], hit.Dice[0].Triggered!);
        Assert.Null(hit.Dice[1].Triggered);
        Assert.All(sim.Steps("roll-to-block").Single().Rolls.SelectMany(r => r.Dice), d => Assert.Null(d.Triggered));
    }

    [Fact]
    public void A_furious_chargers_six_is_marked_with_its_extra_hit_and_a_failed_six_with_nothing()
    {
        var berserkers = Unit("Berserkers", "4+", "4+", [], 2, [P("Furious")], Melee("Axe", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Charge));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal(["Furious +1 hit"], hit.Dice[0].Triggered!);
        Assert.Null(hit.Dice[1].Triggered);
    }
}
