using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// `rule-hits` (Retaliate, Deathstrike, Self-Destruct, Surprise Attack: hits no weapon makes) gather no attacker rule worded for its weapons or attacks:
/// 3.6 Mischievous "targets must re-roll unmodified Defense results of 6 when blocking hits from this model's weapons" does not act on a Retaliate's hits,
/// while Surprise Attack keeps its own "For each 2+ it takes one hit with AP(1)".
/// </summary>
public class ArmyRuleAuditFinishRuleHitsTests
{
    static Sim Hits(string rule, int n, PropertyInstance[] rules, params int[] dice)
    {
        var a = Unit("A", "4+", "4+", [], 1, rules, Melee("Claws", 1)); var b = Unit("B", "4+", "4+", [], 5, []);
        var sim = Sim.Gf36(State([a], [b], x1: -2, x2: 2), dice);
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(a.Id, [b.Id], [], GfRuleHits.Key(rule, n)));
        return sim;
    }

    static RollRecord Block(Sim sim) => Assert.Single(sim.Emitted.OfType<StepCompleted>().SelectMany(c => c.Rolls), r => r.Roll == GfNames.DefenseRoll);

    [Fact]
    public void Mischievous_does_not_reroll_the_blocks_of_hits_a_rule_dealt()
    {
        var sim = Hits(GfNames.Retaliate, 2, [P("Mischievous")], 6, 6, 1, 1);
        Assert.Equal([6, 6], Block(sim).Dice.Select(d => d.Raw));
        Assert.Empty(sim.Emitted.OfType<DiceRerolled>());
        Assert.DoesNotContain(sim.Emitted.OfType<StepCompleted>(), s => s.Applied?.Contains("Mischievous") == true);
    }

    [Fact]
    public void Surprise_attack_hits_keep_their_own_ap_and_take_no_weapon_rule_of_the_carrier()
    {
        var sim = Hits(GfAmbush.SurpriseAttack, 2, [P(GfAmbush.SurpriseAttack, 2), P("Mischievous")], 6, 6, 1, 1);
        var block = Block(sim);
        Assert.Equal(-1, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));   // "one hit with AP(1)"
        Assert.Empty(sim.Emitted.OfType<DiceRerolled>());
    }
}
