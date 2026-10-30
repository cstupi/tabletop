using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Hazardous by edition. 3.5.1: "Gets AP(4), but this model's unit takes one wound on unmodified rolls of 1 to hit." 3.6: "Gets AP(4), but on unmodified
/// results of 1 to hit this model takes one wound, or X wounds if this weapon has the Blast(X)/Deadly(X) rule."
/// </summary>
public class GfHazardous36Tests
{
    static (Sim Sim, Unit Shooters) Shoot(string rules, Weapon gun, params int[] dice)
    {
        var shooters = Unit("Shooters", "4+", "4+", [], 2, [P("Tough", 3)], gun);
        var target = Unit("Target", "4+", "6+", [], 1, [P("Tough", 6)]);
        var sim = Sim.Gf(rules, State([shooters], [target]), dice);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooters.Id, [target.Id], shooters.Models.Select(m => m.Weapons[0].Id).ToList(), GfNames.Hold));
        return (sim, shooters);
    }

    [Fact]
    public void Under_3_5_1_the_unit_takes_the_wound_and_assigns_it()
    {
        var (sim, _) = Shoot(RuleSets.Gf351, Gun("Drill", "36\"", 1, P("Hazardous")), 5, 1, 1);   // first model hits, second rolls a 1; the block fails
        Assert.Equal(1, sim.Steps("score-hits").Last().Values!["self_wounds"]);
        Assert.Equal("remove_casualties", sim.AskFor(Samples.Team1).Question);                        // two unwounded Tough models: the owner picks
    }

    [Fact]
    public void Under_3_6_the_model_that_rolled_the_one_takes_the_wound()
    {
        var (sim, shooters) = Shoot(RuleSets.Gf360, Gun("Drill", "36\"", 1, P("Hazardous")), 5, 1, 1);
        Assert.Equal(2, sim.Steps("roll-to-hit").Last().Rolls.Count);                                 // one pool per model while Hazardous is in the attack
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Equal((0, 1), (sim.Model(shooters.Models[0].Id).Wounds, sim.Model(shooters.Models[1].Id).Wounds));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 1)]
    [InlineData(RuleSets.Gf360, 3)]
    public void A_blast_weapons_one_deals_X_wounds_to_its_model_under_3_6(string rules, int wounds)
    {
        var (sim, shooters) = Shoot(rules, Gun("Bomb", "36\"", 1, P("Hazardous"), P("Blast", 3)), 1, 6, 6, 6, 6);
        Assert.Equal(wounds, sim.Steps("score-hits").Last().Values!["self_wounds"]);
        if (rules == RuleSets.Gf351) return;
        Assert.IsType<OnShelf>(sim.Model(shooters.Models[0].Id).Location);                            // Tough(3) takes 3
        Assert.Equal(0, sim.Model(shooters.Models[1].Id).Wounds);
    }

    [Fact]
    public void A_deadly_weapons_one_deals_X_wounds_to_its_model_under_3_6()
    {
        var (sim, shooters) = Shoot(RuleSets.Gf360, Gun("Lance", "36\"", 1, P("Hazardous"), P("Deadly", 2)), 6, 1, 6);
        Assert.Equal(2, sim.Steps("score-hits").Last().Values!["self_wounds"]);
        Assert.Equal((0, 2), (sim.Model(shooters.Models[0].Id).Wounds, sim.Model(shooters.Models[1].Id).Wounds));
    }

    [Fact]
    public void Under_3_6_an_attack_without_hazardous_keeps_one_pool_per_quality()
    {
        var (sim, _) = Shoot(RuleSets.Gf360, Gun("Rifle", "36\"", 1), 5, 1, 6);
        Assert.Single(sim.Steps("roll-to-hit").Last().Rolls);
    }
}
