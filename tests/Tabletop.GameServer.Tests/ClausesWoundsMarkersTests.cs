using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0 army rules (army-rules.part.json ruleText). Rout: 3.6.0 p.10 "the unit Routs and counts as destroyed".
///
/// Vengeance: "When this unit is fully destroyed, place as many Vengeance tokens on the unit that destroyed it as models with this rule in this unit at the
/// beginning of the game. Friendly units get +X to hit rolls when attacking that unit, where X is the number of tokens on it."
///   E1 fully destroyed: tokens on the unit that destroyed it, one per model with the rule at the beginning of the game (a carrier dead earlier counts)
///   E2 not fully destroyed: none
///   E3 friendly units get +X to hit when attacking that unit; nothing when attacking another unit
///   E4 a Rout counts as destroyed
///
/// Defensive Frenzy: "If most models in this unit have this rule, place one token on this unit when it fully destroys an enemy unit, up to a max. of two
/// tokens. For each token this unit gets +1 to Defense rolls until the end of the game."
/// Piercing Frenzy / Precision Frenzy: "Place one token on this unit when it fully destroys an enemy unit, up to a max. of two tokens. For each token all
/// models with this rule in this unit get AP(+1) on their weapons when attacking / +1 to hit rolls until the end of the game."
///   F1 one token per enemy unit fully destroyed, up to two
///   F2 "most models" (Defensive): a Hero with it in a squad without places none; a squad with it places one
///   F3 "an enemy unit": destroying a friendly unit places none
///   F4 +1 Defense per token, kept in later rounds
///   F5 AP(+1) / +1 to hit per token, for the models with the rule only (a joined Hero without it gets none)
///   F6 a Rout counts as destroyed
///   F7 "until the end of the game": the tokens stay after the unit attacks and is attacked
///
/// Defensive / Fortified Growth: "If most models in this unit have this rule, place one token on it at the beginning of each round if it's on the table and
/// isn't Shaken, up to a max. of four tokens. For every two tokens, this unit gets +1 to Defense rolls / enemies attacking this unit get AP(-1) to a min. of AP(0)."
/// Piercing / Precision Growth: "Place one token on this unit at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four
/// tokens. For every two tokens, models with this rule in this unit get AP(+1) on their weapons / +1 to hit rolls."
///   W1 one token at each round start, up to four
///   W2 "on the table": a unit set aside (Ambush) gets none; a unit inside a Transport gets none
///   W3 "isn't Shaken": a Shaken unit gets none (also proven by Batch6MarkersTests under 3.5.1)
///   W4 for every two tokens (0,1 → 0; 2,3 → 1; 4 → 2): Defense, enemy AP down to a min. of AP(0), AP, hit
///   W5 "most models" (Defensive, Fortified); "models with this rule" (Piercing, Precision): a joined Hero's rule reaches its own attacks only
///      (Piercing Growth's per-model reach: MixedUnitAttackTests.A_piercing_token_reaches_only_the_models_with_the_rule)
///   W6 the tokens are kept: nothing in the text removes them
/// </summary>
public class ClausesWoundsMarkersTests
{
    static void Drive(Sim sim)
    {
        for (var i = 0; i < 40; i++)
        {
            var team = sim.AskForOrNull(Samples.Team2) is not null ? Samples.Team2 : sim.AskForOrNull(Samples.Team1) is not null ? Samples.Team1 : (Guid?)null;
            if (team is not { } t) return;
            var ask = sim.AskFor(t);
            sim.Answer(t, ask.Question == "strike_back" ? new Chosen("strike") : ask.Legal is Allocation al ? new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = al.Amount }) : new Passed());
        }
    }

    /// <summary>`by` shoots every weapon at `at` through the shooting sequence (its fire groups split a mixed unit by carrier signature).</summary>
    static StepCompleted? Shoot(Sim sim, Tabletop.Content.Unit by, Tabletop.Content.Unit at)
    {
        var from = sim.Emitted.Count;
        var team = sim.State.Info.Participants.First(p => p.Id == sim.Unit(by.Id).ParticipantId).TeamId!.Value;
        sim.Start(GfNames.Shooting, new SequenceContext(by.Id, [], [], GfNames.Hold));
        sim.Answer(team, new Assigned(by.Models.SelectMany(m => m.Weapons).Select(w => w.Name).Distinct().ToDictionary(n => n, _ => at.Id)));
        Drive(sim);
        return sim.Emitted.Skip(from).OfType<StepCompleted>().FirstOrDefault(s => s.Step == "roll-to-block");
    }

    static IReadOnlyList<RollRecord> Rolled(Sim sim, int from, string step) => [.. sim.Emitted.Skip(from).OfType<StepCompleted>().Where(s => s.Step == step).SelectMany(s => s.Rolls)];
    static int Mod(RollRecord r, string source) => r.Modifiers.Where(m => m.Source == source).Sum(m => m.Amount);
    static int Tokens(Sim sim, Guid unit, string rule) => sim.Unit(unit).Grants.Count(g => g.EntryName == rule);

    static void NewRound(Sim sim, int round) =>
        sim.Append([new RoundStarted(round, [], GrimdarkFutureRules.V360.OnRoundStarted(sim.State, TestProperties.Core36, round))]);

    static Tabletop.Content.Unit Rifles(string name, int models = 1, params Tabletop.Content.PropertyInstance[] rules) => Unit(name, "2+", "6+", [], models, rules, Gun("Rifle", "36\"", 1));
    static Tabletop.Content.Unit Target(string name, int models = 1, params Tabletop.Content.PropertyInstance[] rules) => Unit(name, "6+", "6+", [], models, rules);

    // ---- Vengeance ----

    [Fact]
    public void E1_a_destroyed_unit_places_one_token_per_model_that_had_vengeance_at_the_start_on_its_destroyer()
    {
        var a = Unit("A", "2+", "6+", [], 1, [], Gun("Rifle", "36\"", 3));
        var v = Target("V", 3);
        v = v with { Models = [v.Models[0] with { Properties = [P("Vengeance")] }, v.Models[1] with { Properties = [P("Vengeance")] }, v.Models[2]] };
        var sim = Sim.Gf36(State([a], [v], x1: -6, x2: 6), 2);
        sim.Append([new ModelsMoved([new ModelRelocation(v.Models[0].Id, sim.Model(v.Models[0].Id).Location, new OnShelf())])]);   // a carrier killed earlier
        Shoot(sim, a, v);
        Assert.Equal(0, sim.Alive(v.Id));
        Assert.Equal(2, Tokens(sim, a.Id, "Vengeance"));
    }

    [Fact]
    public void E2_a_unit_not_fully_destroyed_places_none()
    {
        var a = Rifles("A");
        var v = Target("V", 3, P("Vengeance"));
        var sim = Sim.Gf36(State([a], [v], x1: -6, x2: 6), 2);
        Shoot(sim, a, v);
        Assert.Equal(2, sim.Alive(v.Id));
        Assert.Equal(0, Tokens(sim, a.Id, "Vengeance"));
    }

    [Fact]
    public void E3_friendly_units_get_plus_x_to_hit_against_the_destroyer_alone()
    {
        var a = Unit("A", "2+", "6+", [], 1, [P("Tough", 12)], Gun("Rifle", "36\"", 2));
        var other = Target("O", 1, P("Tough", 12));
        var v = Target("V", 2, P("Vengeance"));
        var friend = Unit("F", "4+", "6+", [], 1, [], Gun("Rifle", "36\"", 1), Melee("Knife", 1));
        var sim = Sim.Gf36(State([a, other], [v, friend], x1: -6, x2: 6), 2);
        Shoot(sim, a, v);
        Assert.Equal(2, Tokens(sim, a.Id, "Vengeance"));
        var from = sim.Emitted.Count;
        sim.Start(GfNames.AttackSequence, new SequenceContext(friend.Id, [a.Id], [friend.Models[0].Weapons[0].Id], GfNames.Hold));
        Drive(sim);
        Assert.Equal(2, Mod(Assert.Single(Rolled(sim, from, "roll-to-hit")), "Vengeance"));
        from = sim.Emitted.Count;
        sim.Start(GfNames.AttackSequence, new SequenceContext(friend.Id, [other.Id], [friend.Models[0].Weapons[0].Id], GfNames.Hold));
        Drive(sim);
        Assert.Equal(0, Mod(Assert.Single(Rolled(sim, from, "roll-to-hit")), "Vengeance"));
    }

    /// <summary>A charger kills one of the two models of `defender`, wins the melee, and the defender's failed test (a 2 against Quality 6+) at half
    /// strength routs it: p.10 "the unit Routs and counts as destroyed".</summary>
    static Sim Rout(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender)
    {
        var sim = Sim.Gf36(State([charger], [defender], x1: -1, x2: 1), 2);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        Drive(sim);
        Assert.Equal(0, sim.Alive(defender.Id));
        Assert.Contains(sim.Steps("consequence"), s => s.Values?.GetValueOrDefault("routed") == 1);
        return sim;
    }

    [Fact]
    public void E4_a_routed_vengeance_unit_counts_as_destroyed()
    {
        var charger = Unit("C", "2+", "2+", [], 1, [], Melee("Axe", 1));
        var sim = Rout(charger, Unit("V", "6+", "6+", [], 2, [P("Vengeance")], Melee("Club", 1)));
        Assert.Equal(2, Tokens(sim, charger.Id, "Vengeance"));
    }

    // ---- Frenzies ----

    /// <summary>`by` deals one hit to the one-model `target` (a hit no weapon makes, so `by`'s own rules act on nothing): it is fully destroyed by `by`.</summary>
    static void Destroy(Sim sim, Tabletop.Content.Unit by, Tabletop.Content.Unit target)
    {
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(by.Id, [target.Id], [], GfRuleHits.Key("Test", 1)));
        Drive(sim);
        Assert.Equal(0, sim.Alive(target.Id));
    }

    [Theory]
    [InlineData("Defensive Frenzy")]
    [InlineData("Piercing Frenzy")]
    [InlineData("Precision Frenzy")]
    public void F1_one_token_per_enemy_unit_fully_destroyed_up_to_two(string rule)
    {
        var a = Rifles("A", 1, P(rule));
        var targets = Enumerable.Range(1, 3).Select(i => Target($"T{i}")).ToArray();
        var sim = Sim.Gf36(State([a], targets, x1: -6, x2: 6), 2);
        var counts = new List<int>();
        foreach (var t in targets) { Destroy(sim, a, t); counts.Add(Tokens(sim, a.Id, rule)); }
        Assert.Equal([1, 2, 2], counts);
    }

    [Theory]
    [InlineData(false, 0)]      // a joined Hero carries it, the two squad models do not
    [InlineData(true, 1)]       // the two squad models carry it, the Hero does not
    public void F2_defensive_frenzy_needs_most_models_to_have_it(bool squadHasIt, int tokens)
    {
        var a = Rifles("A", 3, squadHasIt ? [P("Defensive Frenzy")] : []);
        a = a with { Models = [a.Models[0] with { Properties = squadHasIt ? [P("Hero")] : [P("Hero"), P("Defensive Frenzy")] }, .. a.Models.Skip(1)] };
        var t = Target("T");
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), 2);
        Shoot(sim, a, t);
        Assert.Equal(0, sim.Alive(t.Id));
        Assert.Equal(tokens, Tokens(sim, a.Id, "Defensive Frenzy"));
    }

    [Fact]
    public void F3_destroying_a_friendly_unit_places_none()
    {
        var a = Rifles("A", 1, P("Precision Frenzy"));
        var f = Target("F");
        var sim = Sim.Gf36(State([a, f], [Target("E")], x1: -6, x2: 6), 2);
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(a.Id, [f.Id], [], GfRuleHits.Key("Test", 1)));
        Drive(sim);
        Assert.Equal(0, sim.Alive(f.Id));
        Assert.Equal(0, Tokens(sim, a.Id, "Precision Frenzy"));
    }

    [Fact]
    public void F4_defensive_frenzy_gives_plus_one_defense_per_token_for_the_rest_of_the_game()
    {
        var a = Unit("A", "2+", "6+", [], 1, [P("Defensive Frenzy"), P("Tough", 12)], Gun("Rifle", "36\"", 1));
        var targets = new[] { Target("T1"), Target("T2") };
        var shooter = Unit("S", "2+", "6+", [], 1, [], Gun("Rifle", "36\"", 1));
        var sim = Sim.Gf36(State([a], [.. targets, shooter], x1: -6, x2: 6), 2);
        foreach (var t in targets) Shoot(sim, a, t);
        Assert.Equal(2, Mod(Shoot(sim, shooter, a)!.Rolls.Single(), "Defensive Frenzy"));
        NewRound(sim, 2); NewRound(sim, 3);
        Assert.Equal(2, Mod(Shoot(sim, shooter, a)!.Rolls.Single(), "Defensive Frenzy"));
    }

    [Theory]
    [InlineData("Piercing Frenzy", "roll-to-block", "AP", -2)]
    [InlineData("Precision Frenzy", "roll-to-hit", "Precision Frenzy", 2)]
    public void F5_piercing_and_precision_frenzy_act_per_token_for_the_models_with_the_rule(string rule, string step, string source, int amount)
    {
        var a = Unit("A", "2+", "6+", [], 3, [P(rule)], Gun("Rifle", "36\"", 1));
        a = a with { Models = [a.Models[0] with { Properties = [P("Hero")] }, .. a.Models.Skip(1)] };
        var targets = new[] { Target("T1"), Target("T2") };
        var big = Unit("B", "6+", "4+", [], 1, [P("Tough", 20)]);
        var sim = Sim.Gf36(State([a], [.. targets, big], x1: -6, x2: 6), 2);
        foreach (var t in targets) Destroy(sim, a, t);
        Assert.Equal(2, Tokens(sim, a.Id, rule));
        var from = sim.Emitted.Count;
        Shoot(sim, a, big);
        var rolls = Rolled(sim, from, step);
        Assert.Contains(rolls, r => r.Dice.Count == 2 && Mod(r, source) == amount);     // the two squad models
        Assert.Contains(rolls, r => r.Dice.Count == 1 && Mod(r, source) == 0);          // the Hero without the rule
    }

    /// <summary>"until the end of the game": the tokens stay after the unit attacks (or is attacked) and into later rounds.</summary>
    [Theory]
    [InlineData("Defensive Frenzy")]
    [InlineData("Precision Frenzy")]
    public void F7_frenzy_tokens_stay_after_attacks_and_rounds(string rule) => FrenzyTokensStay(rule);

    [Fact]
    public void F7_piercing_frenzy_tokens_stay_after_attacks_and_rounds() => FrenzyTokensStay("Piercing Frenzy");

    static void FrenzyTokensStay(string rule)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule), P("Tough", 20)], Gun("Rifle", "36\"", 1));
        var targets = new[] { Target("T1"), Target("T2") };
        var e = Unit("E", "4+", "4+", [], 1, [P("Tough", 20)], Gun("Rifle", "36\"", 1));
        var sim = Sim.Gf36(State([a], [.. targets, e], x1: -6, x2: 6), 4);
        foreach (var t in targets) Destroy(sim, a, t);
        Shoot(sim, a, e); Shoot(sim, e, a);
        NewRound(sim, 2);
        Shoot(sim, a, e); Shoot(sim, e, a);
        Assert.Equal(2, Tokens(sim, a.Id, rule));
    }

    [Fact]
    public void F6_a_rout_counts_as_fully_destroying_the_enemy_unit()
    {
        var charger = Unit("C", "2+", "2+", [], 1, [P("Precision Frenzy")], Melee("Axe", 1));
        var sim = Rout(charger, Unit("V", "6+", "6+", [], 2, [], Melee("Club", 1)));
        Assert.Equal(1, Tokens(sim, charger.Id, "Precision Frenzy"));
    }

    // ---- Growths ----

    [Theory]
    [InlineData("Defensive Growth")]
    [InlineData("Fortified Growth")]
    [InlineData("Piercing Growth")]
    [InlineData("Precision Growth")]
    public void W1_one_token_each_round_start_up_to_four(string rule)
    {
        var a = Rifles("A", 1, P(rule));
        var sim = Sim.Gf36(State([a], [Target("E")], x1: -6, x2: 6), 2);
        var counts = new List<int>();
        for (var round = 1; round <= 5; round++) { NewRound(sim, round); counts.Add(Tokens(sim, a.Id, rule)); }
        Assert.Equal([1, 2, 3, 4, 4], counts);
    }

    [Fact]
    public void W2_a_unit_set_aside_off_the_table_gets_no_token()
    {
        var a = Rifles("A", 1, P("Precision Growth"));
        var sim = Sim.Gf36(State([a], [Target("E")], x1: -6, x2: 6), 2);
        sim.Append([new ModelsMoved([new ModelRelocation(a.Models[0].Id, sim.Model(a.Models[0].Id).Location, new OnShelf())])]);
        NewRound(sim, 1);
        Assert.Equal(0, Tokens(sim, a.Id, "Precision Growth"));
    }

    [Fact]
    public void W2_a_unit_inside_a_transport_is_not_on_the_table_and_gets_no_token()
    {
        var a = Rifles("A", 1, P("Precision Growth"));
        var transport = Unit("T", "4+", "4+", [], 1, [P("Transport", 6), P("Tough", 6)]);
        var sim = Sim.Gf36(State([a, transport], [Target("E")], x1: -6, x2: 6), 2);
        sim.Append([new ModelsMoved([new ModelRelocation(a.Models[0].Id, sim.Model(a.Models[0].Id).Location, new Inside(transport.Models[0].Id))])]);
        NewRound(sim, 1);
        Assert.Equal(0, Tokens(sim, a.Id, "Precision Growth"));
    }

    [Fact]
    public void W3_a_shaken_unit_gets_no_token()
    {
        var a = Rifles("A", 1, P("Defensive Growth"));
        var sim = Sim.Gf36(State([a], [Target("E")], x1: -6, x2: 6), 2);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Shaken, true)]);
        NewRound(sim, 1);
        Assert.Equal(0, Tokens(sim, a.Id, "Defensive Growth"));
    }

    /// <summary>The modifier each round from one to four tokens: the unit attacked (Defensive, Fortified) or attacking (Piercing, Precision).</summary>
    [Theory]
    [InlineData("Defensive Growth", false, 0, "roll-to-block", "Defensive Growth", new[] { 0, 1, 1, 2 })]
    [InlineData("Fortified Growth", false, 2, "roll-to-block", "AP", new[] { -2, -1, -1, 0 })]
    [InlineData("Fortified Growth", false, 1, "roll-to-block", "AP", new[] { -1, 0, 0, 0 })]          // to a min. of AP(0)
    [InlineData("Precision Growth", true, 0, "roll-to-hit", "Precision Growth", new[] { 0, 1, 1, 2 })]
    public void W4_every_two_tokens_give_one_step(string rule, bool attacking, int enemyAp, string step, string source, int[] perRound)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule), P("Tough", 40)], Gun("Rifle", "36\"", 1));
        var e = Unit("E", "4+", "4+", [], 1, [P("Tough", 40)], Gun("Rifle", "36\"", 1, enemyAp > 0 ? [P("AP", enemyAp)] : []));
        var sim = Sim.Gf36(State([a], [e], x1: -6, x2: 6), 4);
        var seen = new List<int>();
        for (var round = 1; round <= 4; round++)
        {
            NewRound(sim, round);
            var from = sim.Emitted.Count;
            if (attacking) Shoot(sim, a, e); else Shoot(sim, e, a);
            seen.Add(Mod(Assert.Single(Rolled(sim, from, step)), source));
        }
        Assert.Equal(perRound, seen);
    }

    /// <summary>Piercing Growth's every-two step, read once after four round starts.</summary>
    [Fact]
    public void W4_four_piercing_growth_tokens_give_ap_plus_two()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Piercing Growth")], Gun("Rifle", "36\"", 1));
        var e = Unit("E", "4+", "4+", [], 1, [P("Tough", 40)]);
        var sim = Sim.Gf36(State([a], [e], x1: -6, x2: 6), 4);
        for (var round = 1; round <= 4; round++) NewRound(sim, round);
        Assert.Equal(-2, Mod(Shoot(sim, a, e)!.Rolls.Single(), "AP"));
    }

    [Theory]
    [InlineData("Defensive Growth", false)]
    [InlineData("Fortified Growth", false)]
    [InlineData("Precision Growth", true)]
    public void W6_growth_tokens_stay_after_attacks(string rule, bool attacking) => GrowthTokensStay(rule, attacking);

    [Fact]
    public void W6_piercing_growth_tokens_stay_after_attacks() => GrowthTokensStay("Piercing Growth", true);

    static void GrowthTokensStay(string rule, bool attacking)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule), P("Tough", 40)], Gun("Rifle", "36\"", 1));
        var e = Unit("E", "4+", "4+", [], 1, [P("Tough", 40)], Gun("Rifle", "36\"", 1));
        var sim = Sim.Gf36(State([a], [e], x1: -6, x2: 6), 4);
        NewRound(sim, 1); NewRound(sim, 2);
        if (attacking) Shoot(sim, a, e); else Shoot(sim, e, a);
        Assert.Equal(2, Tokens(sim, a.Id, rule));
    }

    [Theory]
    [InlineData("Defensive Growth", false, 0)]
    [InlineData("Defensive Growth", true, 1)]
    [InlineData("Fortified Growth", false, 0)]
    [InlineData("Fortified Growth", true, 1)]
    public void W5_defensive_and_fortified_growth_need_most_models_to_have_it(string rule, bool squadHasIt, int tokens)
    {
        var a = Rifles("A", 3, squadHasIt ? [P(rule)] : []);
        a = a with { Models = [a.Models[0] with { Properties = squadHasIt ? [P("Hero")] : [P("Hero"), P(rule)] }, .. a.Models.Skip(1)] };
        var sim = Sim.Gf36(State([a], [Target("E")], x1: -6, x2: 6), 2);
        NewRound(sim, 1);
        Assert.Equal(tokens, Tokens(sim, a.Id, rule));
    }

    [Fact]
    public void W5_a_heros_precision_growth_reaches_its_own_attacks_only()
    {
        var a = Unit("A", "4+", "6+", [], 3, [], Gun("Rifle", "36\"", 1));
        a = a with { Models = [a.Models[0] with { Properties = [P("Hero"), P("Precision Growth")] }, .. a.Models.Skip(1)] };
        var e = Unit("E", "6+", "4+", [], 1, [P("Tough", 20)]);
        var sim = Sim.Gf36(State([a], [e], x1: -6, x2: 6), 4);
        NewRound(sim, 1); NewRound(sim, 2);
        var from = sim.Emitted.Count;
        Shoot(sim, a, e);
        var rolls = Rolled(sim, from, "roll-to-hit");
        Assert.Contains(rolls, r => r.Dice.Count == 1 && Mod(r, "Precision Growth") == 1);
        Assert.Contains(rolls, r => r.Dice.Count == 2 && Mod(r, "Precision Growth") == 0);
    }
}
