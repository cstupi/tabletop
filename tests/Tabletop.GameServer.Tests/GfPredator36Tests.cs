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
/// Predator (3.6.0): "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon." An `optional` add_hit
/// attacks row: `roll-to-hit` rolls the pool, asks the roller's team `extra_attacks`, and rolls the added dice only on Yes. A row without
/// `optional` adds its dice as it always has.
/// </summary>
public class GfPredator36Tests
{
    static readonly ConditionDocument RawSix = new(ConditionKind.RollResult, null, Comparison.Eq, 6, [GfNames.Hit, "raw"]);
    static EntryDocument ExtraAttack(string name, bool optional) => new(name, CatalogKind.Weapon, false, null, null, null, $"{name}.", null,
        [new(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, TargetQuantity.Attacks, null, null, 1, false, null, null, null, EffectDuration.Interaction, [RawSix], [], Optional: optional)]);

    const string Predator = "Test Predator", Primal = "Test Primal";

    static PropertySet Props(string rules)
    {
        var core = rules == RuleSets.Gf360 ? TestProperties.CoreDocument36 : TestProperties.CoreDocument;
        return PropertiesJson.ToSet(core with { Entries = [.. core.Entries, ExtraAttack(Predator, true), ExtraAttack(Primal, false)] }, Guid.CreateVersion7);
    }

    static Sim Game(string rules, GameState state, params int[] dice) =>
        new(state.Under(rules), rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351, Props(rules), new ScriptedDice(dice));

    static (Sim Sim, Tabletop.Content.Unit Shooter) Shoot(string rules, string rule, params int[] dice)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P(rule))); var target = Unit("T", "4+", "6+", [], 1, [P(GfNames.Tough, 20)]);
        var sim = Game(rules, State([shooter], [target], x1: -4, x2: 4), dice);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        return (sim, shooter);
    }

    static IReadOnlyList<RollRecord> HitRolls(Sim sim) => sim.Steps("roll-to-hit").SelectMany(s => s.Rolls).ToList();

    [Fact]
    public void Two_sixes_ask_the_rollers_team_to_roll_two_extra_attacks()
    {
        var (sim, _) = Shoot(RuleSets.Gf360, Predator, 6, 6, 6, 6, 1, 1, 1, 1);
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfNames.ExtraAttacks, $"{Predator}: roll 2 extra attacks?", true), (ask.Question, ask.Prompt, ask.AllowPass));
        Assert.Equal(["Yes", "No"], ((Options)ask.Legal).Choices.Select(c => c.Label));
        Assert.Equal(2, HitRolls(sim).Single().Dice.Count);                                                 // the pool is rolled and recorded before the Ask
    }

    [Fact]
    public void Yes_rolls_the_extra_dice_once_and_they_add_no_attacks_of_their_own()
    {
        var (sim, _) = Shoot(RuleSets.Gf360, Predator, 6, 6, 6, 6, 1, 1, 1, 1);
        sim.Answer(Samples.Team1, new Chosen("yes"));
        var rolls = HitRolls(sim);
        Assert.Equal([2, 2], rolls.Select(r => r.Dice.Count));                                               // the pool once, the two added dice once
        Assert.All(rolls[0].Dice, d => Assert.Contains($"{Predator} +1 attack", d.Triggered ?? []));
        Assert.All(rolls[1].Dice, d => Assert.Null(d.Triggered));                                          // "doesn't apply to newly generated attacks"
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Contains(Predator, sim.Steps("roll-to-hit").Last().Applied ?? []);
        Assert.Equal(4, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);                     // four hits
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_or_a_pass_rolls_none(bool pass)
    {
        var (sim, _) = Shoot(RuleSets.Gf360, Predator, 6, 6, 1, 1, 1, 1);
        sim.Answer(Samples.Team1, pass ? new Passed() : new Chosen("no"));
        Assert.Equal([2], HitRolls(sim).Select(r => r.Dice.Count));
        Assert.DoesNotContain(Predator, sim.Steps("roll-to-hit").SelectMany(s => s.Applied ?? []));
        Assert.DoesNotContain(Predator, sim.Steps("score-hits").SelectMany(s => s.Applied ?? []));
        Assert.Equal(2, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
    }

    /// <summary>A row without `optional` (Primal's shape today) adds its dice with no Ask, under either edition.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_mandatory_row_asks_nothing(string rules)
    {
        var (sim, _) = Shoot(rules, Primal, 6, 6, 1, 1, 1, 1, 1, 1);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Equal([2, 2], HitRolls(sim).Select(r => r.Dice.Count));
        Assert.Single(sim.Steps("roll-to-hit"));
    }

    /// <summary>
    /// The shipped Primal and Enhanced Primal (Saurian Starhosts, 3.6.0): "this model may roll +1 attack"; Enhanced Primal "gets extra attacks on
    /// successful unmodified hit results of 5-6 from Primal (instead of only on 6)". Its attacks are Primal's, so Primal's "may" covers them: one
    /// `extra_attacks` Ask for the 5s and the 6s, and each rule is recorded only when its dice are taken.
    /// </summary>
    static Sim ShootPrimal(params int[] dice)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [P("Primal"), P("Enhanced Primal")], Gun("Rifle", "24\"", 2));
        var target = Unit("T", "4+", "6+", [], 1, [P(GfNames.Tough, 20)]);
        var sim = new Sim(State([shooter], [target], x1: -4, x2: 4).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, TestProperties.Core36, new ScriptedDice(dice));
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        return sim;
    }

    [Fact]
    public void Enhanced_primal_five_and_primal_six_are_one_ask_and_taken_record_both()
    {
        var sim = ShootPrimal(5, 6, 1, 1, 1, 1, 1, 1);
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfNames.ExtraAttacks, "Enhanced Primal, Primal: roll 2 extra attacks?"), (ask.Question, ask.Prompt));
        Assert.Equal([2], HitRolls(sim).Select(r => r.Dice.Count));                                        // the 5's die is not rolled before the answer
        sim.Answer(Samples.Team1, new Chosen("yes"));
        Assert.Equal([2, 2], HitRolls(sim).Select(r => r.Dice.Count));
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        var applied = sim.Steps("roll-to-hit").SelectMany(s => s.Applied ?? []).ToList();
        Assert.Contains("Enhanced Primal", applied);
        Assert.Contains("Primal", applied);
    }

    [Fact]
    public void A_taken_enhanced_primal_five_records_enhanced_primal()
    {
        var sim = ShootPrimal(5, 1, 1, 1, 1, 1);
        Assert.Equal("Enhanced Primal: roll 1 extra attack?", sim.AskFor(Samples.Team1).Prompt);
        sim.Answer(Samples.Team1, new Chosen("yes"));
        Assert.Equal([2, 1], HitRolls(sim).Select(r => r.Dice.Count));
        Assert.Contains("Enhanced Primal", sim.Steps("roll-to-hit").Last().Applied ?? []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_declined_enhanced_primal_five_records_neither(bool pass)
    {
        var sim = ShootPrimal(5, 1, 1, 1, 1, 1);
        sim.Answer(Samples.Team1, pass ? new Passed() : new Chosen("no"));
        Assert.Equal([2], HitRolls(sim).Select(r => r.Dice.Count));
        var applied = sim.Steps("roll-to-hit").Concat(sim.Steps("score-hits")).SelectMany(s => s.Applied ?? []).ToList();
        Assert.DoesNotContain("Enhanced Primal", applied);
        Assert.DoesNotContain("Primal", applied);
    }

    /// <summary>3.6.0 p.9: a fatigued hit is no roll of 6 for special rules, so nothing is offered.</summary>
    [Fact]
    public void A_fatigued_six_in_melee_asks_nothing()
    {
        var charger = Unit("Charger", "4+", "6+", [], 1, [], Melee("Sword", 1));
        var tired = Unit("Tired", "4+", "4+", [], 1, [], Melee("Claw", 1, P(Predator)));
        var sim = Game(RuleSets.Gf360, State([charger], [tired], x1: -3, x2: 0), 1, 6, 1, 1, 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, tired.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [tired.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.True(sim.Steps("roll-to-hit").Last().Rolls.Single().Dice.Single().Success);
        Assert.DoesNotContain(sim.Emitted.OfType<AskOpened>(), a => a.Ask.Question == GfNames.ExtraAttacks);
    }

    [Fact]
    public void Optional_is_refused_off_target_quantity_attacks()
    {
        var hits = ExtraAttack("Optional Hits", true);
        hits = hits with { Effects = [hits.Effects[0] with { TargetQuantity = TargetQuantity.Hits }] };
        var problems = PropertiesJson.Validate(TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries, hits, ExtraAttack(Predator, true)] });
        Assert.Contains(problems, p => p.StartsWith("Optional Hits:") && p.Contains("optional is add_hit attacks only"));
        Assert.DoesNotContain(problems, p => p.StartsWith($"{Predator}:"));
    }
}
