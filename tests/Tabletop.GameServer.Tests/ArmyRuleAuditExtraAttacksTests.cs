using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon": the extra attack is the roller's
/// choice (`extra_attacks`), as Predator's and Primal's are.</summary>
public class ArmyRuleAuditExtraAttacksTests
{
    [Theory]
    [InlineData(RuleSets.Gf360, new[] { "Bloodborn" }, "Bloodborn")]
    [InlineData(RuleSets.Gf360, new[] { "Clan Warrior" }, "Clan Warrior")]
    [InlineData(RuleSets.Gf360, new[] { "Bloodborn", "Enhanced Bloodborn" }, "Enhanced Bloodborn")]
    [InlineData(RuleSets.Gf360, new[] { "Clan Warrior", "Enhanced Clan Warrior" }, "Enhanced Clan Warrior")]
    [InlineData(RuleSets.Gf351, new[] { "Bloodborn" }, "Bloodborn")]
    [InlineData(RuleSets.Gf351, new[] { "Clan Warrior" }, "Clan Warrior")]
    [InlineData(RuleSets.Gf351, new[] { "Primal" }, "Primal")]
    [InlineData(RuleSets.Gf351, new[] { "Predator Shooter" }, "Predator Shooter")]
    public void The_extra_attack_is_asked_and_no_rolls_none(string rules, string[] held, string named)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, held.Select(h => P(h)).ToArray(), Gun("Rifle", "24\"", 2));
        var target = Unit("T", "4+", "6+", [], 1, [P(GfNames.Tough, 20)]);
        // A 5 and a 6: the 6 is the plain rule's, the 5 is only the Enhanced rule's.
        var sim = Sim.Gf(rules, State([shooter], [target], x1: -4, x2: 4), named.StartsWith("Enhanced") ? [5, 1, 1, 1, 1, 1, 1] : [6, 1, 1, 1, 1, 1, 1]);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfNames.ExtraAttacks, $"{named}: roll 1 extra attack?"), (ask.Question, ask.Prompt));
        sim.Answer(Samples.Team1, new Chosen("no"));
        Assert.Equal([2], sim.Steps("roll-to-hit").SelectMany(s => s.Rolls).Select(r => r.Dice.Count));
    }
}
