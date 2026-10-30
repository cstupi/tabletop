using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 Ravage: "Once per round, when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued. For each 6+
/// the target takes one wound." Regenerative Strength: "Place one token on this model when it ignores a wound. Once per round, when it's this model's turn
/// to strike in melee, roll as many dice as tokens on it before attacking, unless this unit is Fatigued. For each 4+ the target takes one wound."
/// 3.5.1 Ravage: "When it's this model's turn to attack in melee, roll X dice"; 3.5.1 Regenerative Strength: "+X attacks".
/// </summary>
public class GfStrikeRolls36Tests
{
    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    static Sim Fight(string rules, Tabletop.Content.Unit charger, Tabletop.Content.Unit defender, int[] dice, Func<Sim, IEnumerable<EventPayload>>? setup = null)
    {
        var sim = Sim.Gf(rules, State([charger], [defender], x1: -3, x2: 0), dice);
        if (setup is not null) sim.Append(setup(sim));
        return Settle(sim, charger, defender);
    }

    /// <summary>Starts the melee and answers its Asks: the defender strikes, every move passes.</summary>
    static Sim Settle(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit defender)
    {
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        for (var i = 0; i < 8; i++)
        {
            if (sim.AskForOrNull(Samples.Team2) is { } ask) sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("strike"));
            else if (sim.AskForOrNull(Samples.Team1) is { }) sim.Answer(Samples.Team1, new Passed());
            else break;
        }
        return sim;
    }

    /// <summary>The starts of the melee's rule-wounds and attack-sequence frames, as (actor's name, sequence) in order.</summary>
    static IReadOnlyList<(Guid Actor, string Sequence)> Order(Sim sim) =>
        [.. sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence is GfNames.RuleWoundsSeq or GfNames.AttackSequence).Select(s => (s.Context.ActorUnitId!.Value, s.Sequence))];

    static IReadOnlyList<RollRecord> Rolls(Sim sim, string rule) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == rule)];

    static readonly int[] Ones = [.. Enumerable.Repeat(1, 30)];

    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void The_defenders_ravage_rolls_before_its_strike_back_under_3_6(string rules, bool before)
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2), P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Ravage", 1), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(rules, charger, defender, Ones);
        Assert.Equal([2, 1], Rolls(sim, "Ravage").Select(r => r.Dice.Count));
        var order = Order(sim);
        Assert.Equal((charger.Id, GfNames.RuleWoundsSeq), order[0]);                      // the charger's before its attacks, in both
        var ravage = order.ToList().FindIndex(o => o == (defender.Id, GfNames.RuleWoundsSeq));
        var strikes = order.ToList().FindIndex(o => o == (defender.Id, GfNames.AttackSequence));
        Assert.Equal(before, ravage < strikes);
    }

    [Fact]
    public void Ravage_wounds_on_six()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 3)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = Fight(RuleSets.Gf360, charger, defender, [6, 5, 4, .. Ones]);
        Assert.Equal(1, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Contains(sim.Steps("roll"), s => s.Applied?.Contains("Ravage") ?? false);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 2)]
    [InlineData(RuleSets.Gf360, 0)]
    public void A_fatigued_charger_rolls_no_ravage_under_3_6(string rules, int dice)
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = Fight(rules, charger, defender, Ones, s => [new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfNames.Fatigued, true)]);
        Assert.Equal(dice, Rolls(sim, "Ravage").Sum(r => r.Dice.Count));
    }

    /// <summary>"a Shaken unit striking back counts as Fatigued" (3.6.0 p.10).</summary>
    [Fact]
    public void A_shaken_defender_rolls_no_ravage()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Ravage", 2), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(RuleSets.Gf360, charger, defender, Ones, s => [new ConditionSet(new Subject(SubjectKind.Unit, defender.Id), GfNames.Shaken, true)]);
        Assert.Empty(Rolls(sim, "Ravage"));
    }

    [Fact]
    public void Ravage_rolls_once_per_round()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2), P("Tough", 6)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = Fight(RuleSets.Gf360, charger, defender, Ones);
        Assert.Single(Rolls(sim, "Ravage"));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfNames.Fatigued, false)]);
        Settle(sim, charger, defender);                                                   // a second melee, same round
        Assert.Single(Rolls(sim, "Ravage"));
        sim.Append([new RoundStarted(2, [], []), new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfNames.Fatigued, false)]);
        Settle(sim, charger, defender);                                                   // the next round
        Assert.Equal(2, Rolls(sim, "Ravage").Count);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, 3, 0)]
    [InlineData(RuleSets.Gf360, 1, 2)]
    public void Regenerative_strength_rolls_a_die_per_token_on_four_plus_under_3_6(string rules, int attackDice, int strengthDice)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Regenerative Strength"), P("Regeneration"), P("Tough", 6)], Melee("Claw", 1));
        var b = Unit("B", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = Sim.Gf(rules, State([a], [b], x1: -3, x2: 0), [1, 1, 6, 6, 4, 3, .. Ones]);
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(b.Id, [a.Id], [], "Test|2"));   // two hits, both blocks fail, both wounds ignored: two tokens
        sim.Start(GfNames.Melee, MeleeOf(a, b));
        Assert.Equal(attackDice, sim.Steps("roll-to-hit").First().Rolls.Sum(r => r.Dice.Count));
        var strength = Rolls(sim, "Regenerative Strength");
        Assert.Equal(strengthDice, strength.Sum(r => r.Dice.Count));
        if (strengthDice > 0)
        {
            Assert.Equal(4, strength.Single().Threshold);
            Assert.Equal(1, sim.Model(b.Models[0].Id).Wounds);                                 // the 4 wounds, the 3 does not
        }
    }
}
