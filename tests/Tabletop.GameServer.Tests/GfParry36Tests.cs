using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Parry (Army Forge's common rules): "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits
/// taken. For each 6+ one hit is ignored (defender picks which)." A `parry` step opens resolve-hits under a melee; the ignored hits are those with the
/// most per-die grants.
/// </summary>
public class GfParry36Tests
{
    static readonly EntryDocument Parry = new(GfNames.Parry, CatalogKind.Weapon, true, null, null, null,
        "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which).", null, []);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with
        { Entries = [.. TestProperties.CoreDocument36.Entries.Where(e => e.Name != GfNames.Parry), Parry] }, Guid.CreateVersion7);

    static List<Guid> Weapons(Tabletop.Content.Unit u) => [.. u.Models.SelectMany(m => m.Weapons.Select(w => w.Id))];

    /// <summary>Three models: Hand Weapon Parry(1) each, and the third also a Master Hand Weapon Parry(3): 1 + 1 + 3 dice.</summary>
    static Tabletop.Content.Unit Duelists()
    {
        var d = Unit("Duelists", "4+", "4+", [], 3, [], Melee("Hand Weapon", 1, P(GfNames.Parry, 1)));
        return d with { Models = [d.Models[0], d.Models[1], d.Models[2] with { Weapons = [.. d.Models[2].Weapons, Melee("Master Hand Weapon", 3, P("AP", 1), P(GfNames.Parry, 3))] }] };
    }

    static Sim Fight(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit defender)
    {
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], Weapons(charger), GfNames.Charge));
        for (var i = 0; i < 8; i++)
        {
            if (sim.AskForOrNull(Samples.Team2) is { } ask) sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("pass"));
            else if (sim.AskForOrNull(Samples.Team1) is { }) sim.Answer(Samples.Team1, new Passed());
            else break;
        }
        return sim;
    }

    static IReadOnlyList<RollRecord> Rolls(Sim sim, string roll) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == roll)];

    [Theory]
    [InlineData(4, 4)]      // four hits: four dice
    [InlineData(8, 5)]      // eight hits: 1 + 1 + 3
    public void Each_model_rolls_its_highest_parry_up_to_the_hits(int attacks, int dice)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Claws", attacks));
        var defender = Duelists();
        var sim = Fight(new Sim(State([charger], [defender], x1: -3, x2: 0).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props,
            new ScriptedDice([.. Enumerable.Repeat(4, attacks), .. Enumerable.Repeat(1, 40)])), charger, defender);
        var parry = Assert.Single(Rolls(sim, GfNames.Parry));
        Assert.Equal(dice, parry.Dice.Count);
        Assert.Equal(attacks, Rolls(sim, GfNames.DefenseRoll).Sum(r => r.Dice.Count));     // no 6: nothing ignored
        Assert.Contains(sim.Steps("parry"), s => s.Applied?.Contains(GfNames.Parry) ?? false);
    }

    /// <summary>Rending: hits 6, 6, 4, 4, two of them with AP(+4). Parry rolls 6, 6, 1, 1: the two Rending hits go; the block rolls the two plain hits.</summary>
    [Fact]
    public void Two_sixes_ignore_the_rending_hits_first()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Claws", 4, P("Rending")));
        var defender = Duelists();
        var sim = Fight(new Sim(State([charger], [defender], x1: -3, x2: 0).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props,
            new ScriptedDice([6, 6, 4, 4, 6, 6, 1, 1, .. Enumerable.Repeat(6, 20)])), charger, defender);
        Assert.Equal(4, Assert.Single(Rolls(sim, GfNames.Parry)).Dice.Count);
        var block = Assert.Single(Rolls(sim, GfNames.DefenseRoll));
        Assert.Equal(2, block.Dice.Count);
        Assert.DoesNotContain(block.Modifiers, m => m.Source == "AP");
        Assert.Equal(2, sim.Steps("parry").Single().Values!["parried"]);
    }

    /// <summary>Impact's hits are taken in melee: two Impact hits, two Parry dice.</summary>
    [Fact]
    public void Impact_hits_are_parried()
    {
        var charger = Unit("Riders", "4+", "4+", [], 1, [P("Impact", 2), P("Tough", 6)], Melee("Claws", 1));
        var defender = Duelists();
        var sim = Fight(new Sim(State([charger], [defender], x1: -3, x2: 0).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props,
            new ScriptedDice([6, 6, 6, 6, .. Enumerable.Repeat(1, 40)])), charger, defender);
        var parries = Rolls(sim, GfNames.Parry);
        Assert.Equal(2, parries[0].Dice.Count);
        Assert.Equal(2, parries[0].Dice.Count(d => d.Raw == 6));
    }

    [Fact]
    public void A_shooting_attack_parries_nothing()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 3));
        var defender = Duelists();
        var sim = new Sim(State([shooter], [defender], x1: -12, x2: 0).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props, new ScriptedDice([.. Enumerable.Repeat(4, 3), .. Enumerable.Repeat(1, 20)]));
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        Assert.Empty(Rolls(sim, GfNames.Parry));
        Assert.Equal(3, Rolls(sim, GfNames.DefenseRoll).Sum(r => r.Dice.Count));
    }

    /// <summary>3.5.1 has no Parry entry: a weapon carrying the name rolls nothing.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, 1)]
    public void Only_an_entry_parries(string rules, int parryRolls)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Claws", 2));
        var defender = Duelists();
        var state = State([charger], [defender], x1: -3, x2: 0).Under(rules);
        var dice = new ScriptedDice([4, 4, .. Enumerable.Repeat(1, 40)]);
        var sim = Fight(rules == RuleSets.Gf360 ? new Sim(state, GrimdarkFutureRules.V360, Props, dice) : new Sim(state, GrimdarkFutureRules.Instance, TestProperties.Core, dice), charger, defender);
        Assert.Equal(parryRolls, Rolls(sim, GfNames.Parry).Count);
        Assert.Equal(2, Rolls(sim, GfNames.DefenseRoll).Sum(r => r.Dice.Count));
    }
}
