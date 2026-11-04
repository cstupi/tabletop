using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Versatile Defense: "When a unit where most models have this rule is shot/charged from over 9"/6" away, pick one effect: the unit either gets +1 to
/// Defense rolls, or enemy units get -1 to hit rolls against it. Note that you may pick a different effect each time the unit is attacked."
/// Its set_condition rows at `attacked` are asked of the defender's team by `defender-choice`, before the roll to hit; `attack-end` clears them.
/// </summary>
public class GfVersatileDefense36Tests
{
    const string Name = "Test Versatile Defense", Defense = "Test Versatile Defense: +1 to Defense", ToHit = "Test Versatile Defense: -1 to hit";

    static ConditionDocument C(ConditionKind kind, Comparison cmp, int? number, params string[] values) => new(kind, null, cmp, number, values);
    static ConditionDocument Attacked => C(ConditionKind.AtPoint, Comparison.Eq, null, GfNames.Attacked);
    static ConditionDocument Shot => C(ConditionKind.AttackKind, Comparison.Eq, null, "shooting");
    static ConditionDocument Melee => C(ConditionKind.AttackKind, Comparison.Eq, null, "melee");
    static ConditionDocument Charged => new(ConditionKind.ActionTaken, EffectSubject.Opponent, Comparison.Eq, null, [GfNames.Charge]);
    static ConditionDocument Holding(string condition) => new(ConditionKind.ActorState, EffectSubject.Self, Comparison.Eq, null, [condition]);

    static EffectDocument Row(EffectSubject subject, int ordinal, EffectKind kind, string? roll, int? amount, string? condition, params ConditionDocument[] conditions) =>
        new(subject, ordinal, kind, roll, null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, conditions, [], TargetCondition: condition);

    static readonly EntryDocument Entry = new(Name, CatalogKind.Model, false, Aggregation.Majority, null, null, "test", null,
    [
        Row(EffectSubject.Self, 5, EffectKind.SetCondition, null, null, Defense, Attacked, Shot, C(ConditionKind.Range, Comparison.Gt, 9)),
        Row(EffectSubject.Self, 5, EffectKind.SetCondition, null, null, Defense, Attacked, Charged, Melee, C(ConditionKind.Range, Comparison.Gt, 6)),
        Row(EffectSubject.Self, 5, EffectKind.SetCondition, null, null, ToHit, Attacked, Shot, C(ConditionKind.Range, Comparison.Gt, 9)),
        Row(EffectSubject.Self, 5, EffectKind.SetCondition, null, null, ToHit, Attacked, Charged, Melee, C(ConditionKind.Range, Comparison.Gt, 6)),
        Row(EffectSubject.Self, 10, EffectKind.ModifyRoll, GfNames.DefenseRoll, 1, null, Holding(Defense)),
        Row(EffectSubject.Opponent, 10, EffectKind.ModifyRoll, GfNames.Hit, -1, null, Holding(ToHit)),
    ]);

    static PropertySet Props(string rules) => PropertiesJson.ToSet(rules == RuleSets.Gf360
        ? TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries, Entry] }
        : TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, Entry] }, Guid.CreateVersion7);

    static Sim Game(string rules, GameState state, params int[] dice) =>
        new(state.Under(rules), rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351, Props(rules), new ScriptedDice(dice));

    static Tabletop.Content.Unit Shooter(params Weapon[] guns) => Unit("Shooter", "4+", "4+", [], 1, [], guns.Length == 0 ? [Gun("Rifle", "24\"", 1)] : guns);
    static Tabletop.Content.Unit Defender() => Unit("Defender", "4+", "4+", [], 1, [P(Name), P("Tough", 6)]);
    static IReadOnlyList<Guid> Weapons(Tabletop.Content.Unit u) => u.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList();

    /// <summary>Bases are 1.26" across: centres `gap + 1.26` apart are `gap` edge to edge.</summary>
    static GameState At(Tabletop.Content.Unit shooter, Tabletop.Content.Unit defender, double gap) => State([shooter], [defender], x1: 0, x2: gap + 1.26);

    static IEnumerable<RollModifier> Mods(Sim sim, string step, string roll) => sim.Steps(step).SelectMany(s => s.Rolls).Where(r => r.Roll == roll).SelectMany(r => r.Modifiers).Where(m => m.Source == Name);
    static IEnumerable<ConditionSet> Set(Sim sim, string step) => sim.Steps(step).SelectMany(s => s.Changes.OfType<ConditionSet>());

    [Fact]
    public void A_shot_from_over_nine_inches_asks_the_defenders_team_and_plus_one_defense_reaches_the_block()
    {
        var (shooter, defender) = (Shooter(), Defender());
        var sim = Game(RuleSets.Gf360, At(shooter, defender, 10), 6, 3);                         // hit; a 3 blocks at +1
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal("choose_effect", ask.Question);
        Assert.Equal([Defense, ToHit], ((Options)ask.Legal).Choices.Select(c => c.Id));
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        sim.Answer(Samples.Team2, new Chosen(Defense));
        Assert.Contains(Set(sim, "defender-choice"), c => c.Condition == Defense && c.Present && c.Subject.Id == defender.Id);
        Assert.Equal([1], Mods(sim, "roll-to-block", GfNames.DefenseRoll).Select(m => m.Amount));
        Assert.Empty(Mods(sim, "roll-to-hit", GfNames.Hit));
        Assert.True(sim.Steps("roll-to-block").Single().Rolls.Single().Dice[0].Success);
        Assert.Contains(sim.Steps("defender-choice"), s => s.Applied?.Contains(Name) ?? false);
    }

    [Fact]
    public void The_minus_one_to_hit_choice_reaches_the_hit_roll()
    {
        var (shooter, defender) = (Shooter(), Defender());
        var sim = Game(RuleSets.Gf360, At(shooter, defender, 10), 4, 3);                         // a 4 misses at -1
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        sim.Answer(Samples.Team2, new Chosen(ToHit));
        Assert.Equal([-1], Mods(sim, "roll-to-hit", GfNames.Hit).Select(m => m.Amount));
        Assert.False(sim.Steps("roll-to-hit").Single().Rolls.Single().Dice[0].Success);
        Assert.Empty(Mods(sim, "roll-to-block", GfNames.DefenseRoll));
    }

    [Fact]
    public void A_shot_from_eight_inches_asks_nothing()
    {
        var (shooter, defender) = (Shooter(), Defender());
        var sim = Game(RuleSets.Gf360, At(shooter, defender, 8), 6, 3);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Single(sim.Steps("defender-choice"));
        Assert.Empty(Set(sim, "defender-choice"));
        Assert.Empty(Mods(sim, "roll-to-block", GfNames.DefenseRoll));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_pass_sets_nothing()
    {
        var (shooter, defender) = (Shooter(), Defender());
        var sim = Game(RuleSets.Gf360, At(shooter, defender, 10), 4, 3);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        sim.Answer(Samples.Team2, new Passed());
        Assert.Empty(Set(sim, "defender-choice"));
        Assert.Empty(Mods(sim, "roll-to-hit", GfNames.Hit));
        Assert.Empty(Mods(sim, "roll-to-block", GfNames.DefenseRoll));
        Assert.Empty(sim.Unit(defender.Id).Conditions);
    }

    /// <summary>"You may pick a different effect each time the unit is attacked": the condition goes at attack-end, and the second group's attack asks again.</summary>
    [Fact]
    public void The_condition_goes_at_attack_end_and_the_next_attack_sequence_of_the_activation_asks_again()
    {
        var shooter = Shooter(Gun("Rifle", "24\"", 1), Gun("Pistol", "18\"", 1)); var defender = Defender();
        var sim = Game(RuleSets.Gf360, At(shooter, defender, 10), 6, 3, 4, 3);                   // rifle: hit, +1 blocks; pistol: a 4 misses at -1
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = defender.Id, ["Pistol"] = defender.Id }));
        sim.Answer(Samples.Team2, new Chosen(Defense));
        Assert.Contains(Set(sim, "attack-end"), c => c.Condition == Defense && !c.Present && c.Subject.Id == defender.Id);
        Assert.DoesNotContain(Defense, sim.Unit(defender.Id).Conditions);
        var again = sim.AskFor(Samples.Team2);
        Assert.Equal("choose_effect", again.Question);
        sim.Answer(Samples.Team2, new Chosen(ToHit));
        Assert.Equal([1], Mods(sim, "roll-to-block", GfNames.DefenseRoll).Select(m => m.Amount));   // only the rifle's block
        Assert.Equal([-1], Mods(sim, "roll-to-hit", GfNames.Hit).Select(m => m.Amount));            // only the pistol's hit
        Assert.Equal(2, sim.Steps("attack-end").Count);
        Assert.Empty(sim.Unit(defender.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Charged_from_over_six_inches_the_defender_picks_for_the_chargers_attacks()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1)); var defender = Defender();
        var sim = Game(RuleSets.Gf360, State([charger], [defender], x1: -12, x2: 0), 4, 3, 1, 1, 1, 1);  // a 4 misses at -1
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Id]));
        var (from, to) = (sim.Model(charger.Models[0].Id).Location, (OnTable)sim.Model(defender.Models[0].Id).Location);
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(charger.Models[0].Id, from, new OnTable(new Placement(to.Placement.Position with { X = to.Placement.Position.X - 1.3 }, new Yaw(0))))]));
        var asked = new List<string>();
        for (var i = 0; i < 6 && sim.AskForOrNull(Samples.Team2) is { } ask; i++)
        {
            asked.Add(ask.Question);
            sim.Answer(Samples.Team2, ask.Question switch { "choose_effect" => new Chosen(ToHit), "move" => new Passed(), _ => new Chosen("strike") });
        }
        Assert.Equal(["choose_effect"], asked.Where(q => q == "choose_effect"));
        Assert.Equal([-1], Mods(sim, "roll-to-hit", GfNames.Hit).Select(m => m.Amount));
        Assert.Empty(sim.Unit(defender.Id).Conditions);
    }

    /// <summary>3.5.1 has no `defender-choice` step: its attack-sequence and its logs are unchanged.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, false)]
    [InlineData(RuleSets.Gf360, true)]
    public void Only_3_6_asks(string rules, bool asks)
    {
        var (shooter, defender) = (Shooter(), Defender());
        var sim = Game(rules, At(shooter, defender, 10), 6, 3, 3);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [defender.Id], Weapons(shooter), GfNames.Hold));
        Assert.Equal(asks, sim.AskForOrNull(Samples.Team2) is { Question: "choose_effect" });
        Assert.Equal(asks ? 0 : 1, sim.Steps("attack-end").Count);
        Assert.Empty(sim.Steps("defender-choice"));                                              // 3.5.1 has none; 3.6 is waiting on the answer
    }
}
