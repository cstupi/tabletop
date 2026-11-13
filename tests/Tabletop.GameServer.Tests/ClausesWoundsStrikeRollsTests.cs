using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0 army rules (army-rules.part.json ruleText).
///
/// Ravage(X): "Once per round, when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued. For each 6+
/// the target takes one wound."
///   V1 once per round                                   — proven by GfStrikeRolls36Tests.Ravage_rolls_once_per_round
///   V2 at this model's turn to strike (charger; defender only when it strikes back; Counter) — GfStrikeRolls36Tests (order, no strike back, counter)
///   V3 before attacking                                 — GfStrikeRolls36Tests.The_defenders_ravage_rolls_before_its_strike_back_under_3_6
///   V4 unless Fatigued (a Shaken striker counts as Fatigued) — GfStrikeRolls36Tests.A_fatigued_charger…, A_shaken_defender_rolls_no_ravage
///   V5 X dice, each 6+ one wound on the target          — GfStrikeRolls36Tests.Ravage_wounds_on_six
///   V6 "takes one wound": no Defense roll blocks it     — new
///   V7 "this model": each carrier's own X; a joined Hero without it adds none, a Hero with it adds its own — new
///
/// Regenerative Strength: "Place one token on this model when it ignores a wound. Once per round, when it's this model's turn to strike in melee, roll as
/// many dice as tokens on it before attacking, unless this unit is Fatigued. For each 4+ the target takes one wound."
///   G1 a token per wound this model ignores             — proven by GfStrikeRolls36Tests.Regenerative_strength_rolls_a_die_per_token… (two ignored, two dice)
///   G2 "on this model": a wound another model of the unit ignores places none — new
///   G3 once per round, tokens kept for the next round   — new
///   G4 unless Fatigued; a Shaken defender striking back counts as Fatigued — new
///   G5 as many dice as tokens; none with no token       — new (none); two dice proven by GfStrikeRolls36Tests
///   G6 each 4+ one wound, no block                      — GfStrikeRolls36Tests (the 4 wounds, the 3 does not); no block new
/// </summary>
public class ClausesWoundsStrikeRollsTests
{
    static SequenceContext Charge(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    /// <summary>Answers the open Asks: the defender strikes back (or not), allocations go to `allocate`'s pick (else the first model), the rest pass.</summary>
    static void Drive(Sim sim, bool strikeBack = true, Func<Allocation, Guid>? allocate = null)
    {
        for (var i = 0; i < 40; i++)
        {
            var team = sim.AskForOrNull(Samples.Team2) is not null ? Samples.Team2 : sim.AskForOrNull(Samples.Team1) is not null ? Samples.Team1 : (Guid?)null;
            if (team is not { } t) return;
            var ask = sim.AskFor(t);
            sim.Answer(t, ask.Question == "strike_back" ? new Chosen(strikeBack ? "strike" : "pass")
                : ask.Legal is Allocation al ? new Allocated(new Dictionary<Guid, int> { [allocate?.Invoke(al) ?? al.Targets[0]] = al.Amount })
                : new Passed());
        }
    }

    static void Fight(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target, bool strikeBack = true)
    {
        sim.Start(GfNames.Melee, Charge(charger, target));
        Drive(sim, strikeBack);
    }

    static IReadOnlyList<RollRecord> Rolls(Sim sim, string rule) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == rule)];

    static void Unfatigue(Sim sim, params Guid[] units) => sim.Append(units.Select(u => (EventPayload)new ConditionSet(new Subject(SubjectKind.Unit, u), GfNames.Fatigued, false)));

    // ---- Ravage ----

    [Fact]
    public void V6_a_ravage_wound_is_not_blocked()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "2+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), 6);           // every attack hits and every block holds; both Ravage dice wound
        Fight(sim, charger, defender);
        Assert.Equal(2, sim.Model(defender.Models[0].Id).Wounds);
        var ravage = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.RuleWoundsSeq);
        Assert.DoesNotContain(sim.Emitted.OfType<StepCompleted>().Where(c => c.FrameId == ravage.FrameId).SelectMany(c => c.Rolls), r => r.Roll == GfNames.DefenseRoll);
    }

    [Theory]
    [InlineData(false, 2)]      // two squad models with Ravage(1), a joined Hero without it
    [InlineData(true, 3)]       // and the Hero with Ravage(1) of its own
    public void V7_ravage_rolls_each_carriers_own_x(bool heroHasIt, int dice)
    {
        var squad = Unit("C", "6+", "4+", [], 3, [P("Ravage", 1)], Melee("Axe", 1));
        squad = squad with { Models = [squad.Models[0] with { Properties = heroHasIt ? [P("Hero"), P("Ravage", 1)] : [P("Hero")] }, .. squad.Models.Skip(1)] };
        var defender = Unit("D", "6+", "4+", [], 1, [P("Tough", 12)], Melee("Club", 1));
        var sim = Sim.Gf36(State([squad], [defender], x1: -3, x2: 0), 1);
        Fight(sim, squad, defender);
        Assert.Equal(dice, Assert.Single(Rolls(sim, GfNames.Ravage)).Dice.Count);
    }

    // ---- Regenerative Strength ----

    /// <summary>Team2's `hitter` deals `n` hits to `unit` (no hit roll); the block and Regeneration roll the sim's dice.</summary>
    static void Hit(Sim sim, Tabletop.Content.Unit hitter, Tabletop.Content.Unit unit, int n, Func<Allocation, Guid>? allocate = null)
    {
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(hitter.Id, [unit.Id], [], GfRuleHits.Key("Test", n)));
        Drive(sim, allocate: allocate);
    }

    /// <summary>A holder of Regenerative Strength with Regeneration and Tough(6), Quality 6+ (its attacks miss on a 5), Defense 6+. Dice 5: blocks fail,
    /// Regeneration ignores, Regenerative Strength's 4+ wounds.</summary>
    static Tabletop.Content.Unit Holder() => Unit("A", "6+", "6+", [], 1, [P("Regenerative Strength"), P("Regeneration"), P("Tough", 6)], Melee("Claw", 1));
    static Tabletop.Content.Unit Enemy() => Unit("B", "6+", "6+", [], 1, [P("Tough", 12)], Melee("Club", 1));

    [Theory]
    [InlineData(0, 0)]          // the wound is ignored by the model without the rule
    [InlineData(1, 1)]          // by the holder
    public void G2_a_token_is_placed_only_on_the_model_with_the_rule_that_ignored_the_wound(int ignoredBy, int dice)
    {
        var a = Unit("A", "6+", "6+", [], 3, [P("Regeneration"), P("Tough", 6)], Melee("Claw", 1));
        a = a with { Models = [a.Models[0], a.Models[1] with { Properties = [.. a.Models[1].Properties, P("Regenerative Strength")] }, a.Models[2]] };
        var b = Enemy();
        var sim = Sim.Gf36(State([a], [b], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 1, _ => a.Models[ignoredBy].Id);
        Fight(sim, a, b);
        Assert.Equal(dice, Rolls(sim, GfNames.RegenerativeStrength).Sum(r => r.Dice.Count));
    }

    [Fact]
    public void G3_regenerative_strength_rolls_once_per_round_and_keeps_its_tokens()
    {
        var a = Holder(); var b = Enemy();
        var sim = Sim.Gf36(State([a], [b], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 2);
        Fight(sim, a, b);
        Assert.Equal([2], Rolls(sim, GfNames.RegenerativeStrength).Select(r => r.Dice.Count));
        Unfatigue(sim, a.Id, b.Id);
        Fight(sim, a, b);                                                                // a second melee, same round
        Assert.Single(Rolls(sim, GfNames.RegenerativeStrength));
        sim.Append([new RoundStarted(2, [], [])]);
        Unfatigue(sim, a.Id, b.Id);
        Fight(sim, a, b);                                                                // the next round: the two tokens are still on it
        Assert.Equal([2, 2], Rolls(sim, GfNames.RegenerativeStrength).Select(r => r.Dice.Count));
    }

    [Fact]
    public void G4_a_fatigued_holder_rolls_nothing()
    {
        var a = Holder(); var b = Enemy();
        var sim = Sim.Gf36(State([a], [b], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 2);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true)]);
        Fight(sim, a, b);
        Assert.Empty(Rolls(sim, GfNames.RegenerativeStrength));
    }

    [Fact]
    public void G4_a_shaken_holder_striking_back_counts_as_fatigued_and_rolls_nothing()
    {
        var a = Holder(); var b = Enemy();
        var sim = Sim.Gf36(State([b], [a], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 2);
        Assert.Equal(2, sim.Steps("assign").Sum(s => s.Values?.GetValueOrDefault($"ignored_{a.Models[0].Id:N}") ?? 0));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Shaken, true)]);
        Fight(sim, b, a);
        Assert.Empty(Rolls(sim, GfNames.RegenerativeStrength));
    }

    [Fact]
    public void G4_a_holder_striking_back_rolls_before_its_attacks()
    {
        var a = Holder(); var b = Enemy();
        var sim = Sim.Gf36(State([b], [a], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 1);
        Fight(sim, b, a);
        Assert.Single(Rolls(sim, GfNames.RegenerativeStrength));
        var starts = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Context.ActorUnitId == a.Id && s.Sequence is GfNames.RuleWoundsSeq or GfNames.AttackSequence).Select(s => s.Sequence).ToList();
        Assert.Equal([GfNames.RuleWoundsSeq, GfNames.AttackSequence], starts);
    }

    [Fact]
    public void G5_a_holder_with_no_token_rolls_nothing()
    {
        var a = Holder(); var b = Enemy();
        var sim = Sim.Gf36(State([a], [b], x1: -3, x2: 0), 5);
        Fight(sim, a, b);
        Assert.Empty(Rolls(sim, GfNames.RegenerativeStrength));
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.StrikeRollsSeq);
    }

    [Fact]
    public void G6_a_regenerative_strength_wound_is_not_blocked()
    {
        var a = Holder();
        var b = Unit("B", "6+", "2+", [], 1, [P("Tough", 12)], Melee("Club", 1));     // blocks on 2+: a 5 would block
        var sim = Sim.Gf36(State([a], [b], x1: -3, x2: 0), 5);
        Hit(sim, b, a, 2);
        Fight(sim, a, b);
        Assert.Equal(2, sim.Model(b.Models[0].Id).Wounds);
    }
}
