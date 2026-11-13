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
/// Clause tests, Grimdark Future 3.6.0: Caster (rulebook 3.6.0 p.13) and the casting army rules (army-rules.part.json ruleText).
///
/// Caster(X): "Gets X spell tokens at the start of each round, but can't have more than 6 at once. Once per activation, before attacking, spend as many
/// tokens as the spell's value to try casting one or more spells (only one try per spell). Pick a target and roll one die, and on 4+ resolve the spell's
/// effect. Models within 18" of the Caster may spend any number of spell tokens at the same time before rolling to give the Caster +1/-1 to the roll per
/// token. Note that Casters get spell tokens each round even if they are not on the table …, and that the Caster's spells must be picked from their own faction."
///   C1 X tokens at each round start, never more than 6                      — new
///   C2 tokens even when not on the table                                    — new
///   C3 once per activation, before attacking                                — proven by GfCasting36Tests.The_cast_window_opens_once_per_activation_under_3_6
///   C4 the spell's value is spent                                           — new
///   C5 only one try per spell                                               — new (3.6); GfCastingTests under 3.5.1
///   C6 one die, 4+ resolves, below fails                                    — new
///   C7 models within 18" spend tokens: +1 per friendly token, -1 per enemy token — new
///   C8 within 18" of the Caster model, no sight                             — proven by GfCasting36Tests.A_token_holder_out_of_sight_within_18…
///   C9 spells from their own faction                                        — proven by GfCastingTests.A_caster_is_offered_only_its_own_books_spells
///
/// Caster Group: "Pick one model with this rule in this unit to have Caster(X), where X is the total number of models with this rule in this unit. If the
/// model is killed, pick another to be the new caster, and transfer all spell tokens to it. The caster loses all unspent spell tokens at the end of the round."
///   K1 the player picks which model is the caster                          — DEFECT
///   K2 X = the models with the rule (a joined Hero without it is not counted) — new
///   K3 killed: another becomes the caster and gets all its tokens           — new
///   K4 unspent tokens are lost at the end of the round; X follows the models left — new
///
/// Spell Accumulator(X): "Gets X accumulator tokens at the start of each round, but can't hold more than 6 tokens at once. Casters from other friendly
/// units within 12" may spend this model's accumulator tokens as if they were their own spell tokens. Friendly casters may only use this rule if this unit
/// isn't Shaken."
///   A1 X tokens at each round start, never more than 6                      — new
///   A2 a caster of another friendly unit within 12" pays a spell's value from them — new
///   A3 not beyond 12"; not a caster of its own unit; not an enemy caster     — new
///   A4 not while the accumulator's unit is Shaken                           — new (3.6); Batch7CastingTests under 3.5.1
///   A5 "as if they were their own": also spent for +1 in the spend           — new
///
/// Spell Conduit: "Casters within 12" that are from other friendly units may cast spells as if they were in this model's position, and get +1 to casting
/// rolls when doing so. Friendly casters may only use this rule if this unit isn't Shaken."
///   N1 cast from the conduit's position: range measured from it            — new
///   N2 +1 to the casting roll from the conduit, none from the caster         — new (3.6); Batch7CastingTests under 3.5.1
///   N3 not beyond 12"; not a conduit of the caster's unit; not an enemy conduit; not while Shaken — new
///
/// Extended Buff Range: "If this unit is within 24" of another friendly unit with this rule that has a Hero in it, then that Hero may use special rules that
/// allow it to pick friendly units within 12" (except for spells) on this unit as if it was in range."
///   X1 the Hero's 12" Buff reaches a unit with the rule within 24"          — new (3.6); Batch7CastingTests under 3.5.1
///   X2 not a unit without the rule; not from a unit without the rule; not beyond 24"; not a non-Hero picker — new
///   X3 only rules that pick within 12"                                      — not tested: every 3.6 friendly-unit pick is 12"
///   X4 except for spells                                                     — new
/// </summary>
public class ClausesWoundsCastingTests
{
    static EffectDocument Fx(EffectKind kind, string? targetEntry = null, int? amount = null, EffectDuration duration = EffectDuration.Interaction) =>
        new(EffectSubject.Self, 10, kind, null, null, targetEntry, null, null, null, null, amount, false, null, null, null, duration, [], []);
    static readonly EntryDocument Bolt = new("Clause Bolt", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), [Fx(EffectKind.AddHit, amount: 3)]);
    static readonly EntryDocument Ward = new("Clause Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target friendly unit within 12\" gets Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12), [Fx(EffectKind.GrantProperty, "Regeneration", null, EffectDuration.Once)]);
    static readonly PropertySet Book = PropertiesJson.ToSet(TestProperties.CoreDocument36 with
    {
        Entries = [.. TestProperties.CoreDocument36.Entries, Bolt, Ward],
        Books = [.. TestProperties.CoreDocument36.Books ?? [], new BookRecord(TestProperties.TestBook, "Test Book", null, [Bolt.Name, Ward.Name])],
    }, Guid.CreateVersion7);

    static Sim Game(IReadOnlyList<Unit> mine, IReadOnlyList<Unit> theirs, params int[] dice) =>
        new(State(mine, theirs, x1: -10, x2: 10).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Book, new ScriptedDice(dice.Length == 0 ? [4] : dice));

    static void Put(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);
    static void Tokens(Sim sim, Guid model, int n) => sim.Append([new SpellTokensSet(model, n)]);
    static void NewRound(Sim sim, int round) => sim.Append([new RoundStarted(round, [], GrimdarkFutureRules.V360.OnRoundStarted(sim.State, Book, round))]);
    static void Shake(Sim sim, Guid unit) => sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true)]);

    /// <summary>Opens a casting by `unit`; the pick-spell Ask's options, or none when the casting finished at once.</summary>
    static IReadOnlyList<string> Offered(Sim sim, Guid unit)
    {
        sim.Start(GfNames.CastingSeq, new SequenceContext(unit, [], [], null));
        return sim.AskForOrNull(Samples.Team1) is { Question: "cast", Legal: Options o } ? [.. o.Choices.Select(c => c.Id)] : [];
    }

    static string Pick(EntryDocument spell, Guid caster) => $"{spell.Name}|{caster}";
    static RollRecord CastRoll(Sim sim) => sim.Steps("roll").SelectMany(s => s.Rolls).Single(r => r.Roll == GfNames.Casting);

    // ---- Caster ----

    [Fact]
    public void C1_a_caster_gets_x_tokens_each_round_start_and_never_more_than_six()
    {
        var w = Unit("W", "4+", "4+", [], 1, [P("Caster", 2)]);
        var sim = Game([w], [Unit("E", "4+", "4+", [], 1, [])]);
        var seen = new List<int>();
        for (var r = 1; r <= 4; r++) { NewRound(sim, r); seen.Add(sim.Model(w.Models[0].Id).SpellTokens); }
        Assert.Equal([2, 4, 6, 6], seen);
    }

    [Fact]
    public void C2_a_caster_off_the_table_still_gets_its_tokens()
    {
        var w = Unit("W", "4+", "4+", [], 1, [P("Caster", 2)]);
        var sim = Game([w], [Unit("E", "4+", "4+", [], 1, [])]);
        sim.Append([new ModelsMoved([new ModelRelocation(w.Models[0].Id, sim.Model(w.Models[0].Id).Location, new OnShelf())])]);
        NewRound(sim, 1);
        Assert.Equal(2, sim.Model(w.Models[0].Id).SpellTokens);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void C4_C6_a_cast_spends_the_spells_value_and_resolves_on_four_plus(int face, bool resolves)
    {
        var w = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 3)]), TestProperties.TestBook);
        var e = Unit("E", "4+", "6+", [], 1, [P("Tough", 9)]);
        var sim = Game([w], [e], face);
        Put(sim, e.Models[0].Id, -2, -18.5);                                              // 8" from the caster
        Tokens(sim, w.Models[0].Id, 3);
        Assert.Contains(Pick(Bolt, w.Models[0].Id), Offered(sim, w.Id));
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        Assert.Equal(1, sim.Model(w.Models[0].Id).SpellTokens);                          // the value, 2, spent
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
            sim.Answer(Samples.Team1, ask.Legal is PieceSelection ps ? new SelectedPieces([ps.Eligible[0]]) : new Passed());
        var roll = CastRoll(sim);
        Assert.Equal((1, 4), (roll.Dice.Count, roll.Threshold));
        Assert.Equal(resolves, sim.Emitted.OfType<SequenceStarted>().Any(s => s.Sequence == GfNames.SpellHitsSeq));
    }

    [Fact]
    public void C5_a_spell_tried_is_not_offered_again_while_another_still_is()
    {
        var w = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 6)]), TestProperties.TestBook);
        var sim = Game([w], [Unit("E", "4+", "4+", [], 1, [])], 1);
        Tokens(sim, w.Models[0].Id, 6);
        Offered(sim, w.Id);
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { } ask; i++) sim.Answer(Samples.Team1, new Passed());   // the cast fails on a 1
        var again = Offered(sim, w.Id);
        Assert.DoesNotContain(Pick(Bolt, w.Models[0].Id), again);
        Assert.Contains(Pick(Ward, w.Models[0].Id), again);
    }

    [Fact]
    public void C7_friendly_tokens_give_plus_one_and_enemy_tokens_minus_one_each()
    {
        var w = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 3)]), TestProperties.TestBook);
        var friend = Unit("F", "4+", "4+", [], 1, [P("Caster", 3)]);
        var foe = Unit("G", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Game([w, friend], [foe], 1);
        Put(sim, w.Models[0].Id, 0, 0); Put(sim, friend.Models[0].Id, -15, 0); Put(sim, foe.Models[0].Id, 15, 0);
        Tokens(sim, w.Models[0].Id, 2); Tokens(sim, friend.Models[0].Id, 3); Tokens(sim, foe.Models[0].Id, 3);
        Offered(sim, w.Id);
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        sim.Answer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [friend.Models[0].Id] = 3 }));
        sim.Answer(Samples.Team2, new TokensSpent(new Dictionary<Guid, int> { [foe.Models[0].Id] = 1 }));
        Assert.Equal(2, CastRoll(sim).Modifiers.Where(m => m.Source == "tokens").Sum(m => m.Amount));
        Assert.Equal((0, 2), (sim.Model(friend.Models[0].Id).SpellTokens, sim.Model(foe.Models[0].Id).SpellTokens));
    }

    // ---- Caster Group ----

    /// <summary>K1: at the round start the owning team is asked which group model is the caster; the one picked holds the round's tokens and casts.</summary>
    [Fact]
    public void K1_the_player_may_pick_any_group_model_as_the_caster()
    {
        var g = InBook(Unit("G", "4+", "4+", [], 3, [P("Caster Group")]), TestProperties.TestBook);
        var sim = Game([g], [Unit("E", "4+", "4+", [], 1, [])]);
        NewRound(sim, 1);
        var start = Assert.Single(Assert.IsType<Tabletop.GameServer.Scheduler.HookStartSequences>(
            GrimdarkFutureRules.V360.RoundBoundaryStep(sim.State, Book, 1, [Samples.Team1, Samples.Team2])).Sequences);
        sim.Start(start.Sequence, start.Context);
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfSpellSupport.Question, ask.Question);
        Assert.Equal(g.Models.Select(m => m.Id), Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([g.Models[2].Id]));
        Assert.Equal([0, 0, 3], g.Models.Select(m => sim.Model(m.Id).SpellTokens));
        var offered = Offered(sim, g.Id);
        Assert.Contains(offered, o => o.EndsWith($"|{g.Models[2].Id}", StringComparison.Ordinal));
        Assert.DoesNotContain(offered, o => o.EndsWith($"|{g.Models[0].Id}", StringComparison.Ordinal));
        NewRound(sim, 2);
        Assert.Equal([0, 0, 3], g.Models.Select(m => sim.Model(m.Id).SpellTokens));                   // the pick stands
        Assert.IsType<Tabletop.GameServer.Scheduler.HookDone>(GrimdarkFutureRules.V360.RoundBoundaryStep(sim.State, Book, 2, [Samples.Team1, Samples.Team2]));
    }

    [Fact]
    public void K2_x_counts_the_models_with_the_rule_only()
    {
        var g = Unit("G", "4+", "4+", [], 4, [P("Caster Group")]);
        g = g with { Models = [g.Models[0] with { Properties = [P("Hero")] }, .. g.Models.Skip(1)] };
        var sim = Game([g], [Unit("E", "4+", "4+", [], 1, [])]);
        NewRound(sim, 1);
        Assert.Equal([0, 3, 0, 0], g.Models.Select(m => sim.Model(m.Id).SpellTokens));
    }

    [Fact]
    public void K3_K4_a_killed_casters_tokens_pass_to_the_next_and_unspent_tokens_are_lost_at_the_round_end()
    {
        var g = Unit("G", "4+", "6+", [], 3, [P("Caster Group")]);
        var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = Game([g], [e], 2);
        NewRound(sim, 1);
        Assert.Equal(3, sim.Model(g.Models[0].Id).SpellTokens);
        NewRound(sim, 2);
        Assert.Equal(3, sim.Model(g.Models[0].Id).SpellTokens);                          // the unspent 3 were lost, not added to
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(e.Id, [g.Id], [], GfRuleHits.Key("Test", 1)));
        if (sim.AskForOrNull(Samples.Team1) is { Legal: Allocation al }) sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [g.Models[0].Id] = al.Amount }));
        Assert.Equal(GfSpellSupport.Question, sim.AskFor(Samples.Team1).Question);                     // K3 "pick another to be the new caster"
        sim.Answer(Samples.Team1, new SelectedPieces([g.Models[1].Id]));
        Assert.IsType<OnShelf>(sim.Model(g.Models[0].Id).Location);
        Assert.Equal(3, sim.Model(g.Models[1].Id).SpellTokens);                          // all its tokens transferred
        NewRound(sim, 3);
        Assert.Equal(2, sim.Model(g.Models[1].Id).SpellTokens);                          // X is the two models left
    }

    // ---- Spell Accumulator ----

    [Fact]
    public void A1_an_accumulator_gets_x_tokens_each_round_start_and_never_more_than_six()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Spell Accumulator", 4)]);
        var sim = Game([a], [Unit("E", "4+", "4+", [], 1, [])]);
        var seen = new List<int>();
        for (var r = 1; r <= 3; r++) { NewRound(sim, r); seen.Add(sim.Model(a.Models[0].Id).SpellTokens); }
        Assert.Equal([4, 6, 6], seen);
    }

    /// <summary>A Caster(1) with no token of its own, and an accumulator holding 3.</summary>
    static (Sim Sim, Unit Wizard, Unit Acc, Unit Foe) Accumulating(bool sameUnit = false, double accX = -10, bool foeCaster = false)
    {
        var w = InBook(Unit("W", "4+", "4+", [], sameUnit ? 2 : 1, [P("Caster", 1)]), TestProperties.TestBook);
        var acc = Unit("A", "4+", "4+", [], 1, [P("Spell Accumulator", 3)]);
        if (sameUnit) w = w with { Models = [w.Models[0], w.Models[1] with { Properties = [P("Spell Accumulator", 3)] }] };
        var foe = InBook(Unit("F", "4+", "4+", [], 1, foeCaster ? [P("Caster", 1)] : []), TestProperties.TestBook);
        var sim = Game(sameUnit ? [w] : [w, acc], [foe], 1);
        Put(sim, w.Models[0].Id, 0, 0);
        var accModel = sameUnit ? w.Models[1].Id : acc.Models[0].Id;
        Put(sim, accModel, sameUnit ? -2 : accX, 0);
        Put(sim, foe.Models[0].Id, 6, 10);
        Tokens(sim, accModel, 3);
        return (sim, w, acc, foe);
    }

    [Fact]
    public void A2_a_caster_of_another_friendly_unit_within_12_pays_from_the_accumulator()
    {
        var (sim, w, acc, _) = Accumulating(accX: -12);
        Assert.Contains(Pick(Bolt, w.Models[0].Id), Offered(sim, w.Id));
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        Assert.Equal(1, sim.Model(acc.Models[0].Id).SpellTokens);
    }

    [Theory]
    [InlineData("beyond 12")]
    [InlineData("same unit")]
    [InlineData("enemy caster")]
    [InlineData("shaken")]
    public void A3_A4_no_caster_out_of_reach_of_its_own_unit_or_an_enemy_and_none_while_shaken(string situation)
    {
        var (sim, w, acc, foe) = Accumulating(sameUnit: situation == "same unit", accX: situation == "beyond 12" ? -14 : -10, foeCaster: situation == "enemy caster");
        if (situation == "shaken") Shake(sim, acc.Id);
        if (situation == "enemy caster")
        {
            Put(sim, foe.Models[0].Id, -10, 6);                                               // 6" from the accumulator
            sim.Start(GfNames.CastingSeq, new SequenceContext(foe.Id, [], [], null));
            Assert.Null(sim.AskForOrNull(Samples.Team2));
            return;
        }
        Assert.Empty(Offered(sim, w.Id));
    }

    [Fact]
    public void A5_accumulator_tokens_are_spent_for_plus_one_as_the_casters_own()
    {
        var (sim, w, acc, _) = Accumulating();
        Tokens(sim, w.Models[0].Id, 2);
        Offered(sim, w.Id);
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        Assert.Equal(3, sim.Model(acc.Models[0].Id).SpellTokens);                          // the caster's own two paid the value
        var spend = Assert.IsType<TokenSpend>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(3, spend.MaxByModel[acc.Models[0].Id]);
        sim.Answer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [acc.Models[0].Id] = 2 }));
        Assert.Equal(2, CastRoll(sim).Modifiers.Where(m => m.Source == "tokens").Sum(m => m.Amount));
    }

    // ---- Spell Conduit ----

    /// <summary>A caster at x 0, a conduit (its own unit, the caster's, or the enemy's), and an enemy 20" from the caster and 10" from the conduit at x 10.</summary>
    static (Sim Sim, Unit Wizard, Unit Conduit, Unit Foe) Conduit(string where = "friend", double conduitX = 10)
    {
        var w = InBook(Unit("W", "4+", "4+", [], where == "same unit" ? 2 : 1, [P("Caster", 3)]), TestProperties.TestBook);
        if (where == "same unit") w = w with { Models = [w.Models[0], w.Models[1] with { Properties = [P("Spell Conduit")] }] };
        var c = Unit("C", "4+", "4+", [], 1, [P("Spell Conduit")]);
        var foe = Unit("F", "4+", "6+", [], 1, [P("Tough", 9)]);
        var sim = Game(where == "friend" ? [w, c] : [w], where == "enemy" ? [foe, c] : [foe], 4);
        Put(sim, w.Models[0].Id, 0, 0);
        Put(sim, where == "same unit" ? w.Models[1].Id : c.Models[0].Id, conduitX, 0);
        Put(sim, foe.Models[0].Id, 20, 0);
        Tokens(sim, w.Models[0].Id, 3);
        sim.Start(GfNames.CastingSeq, new SequenceContext(w.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
        return (sim, w, c, foe);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void N1_N2_a_cast_from_the_conduit_measures_from_it_and_rolls_plus_one(bool fromConduit)
    {
        var (sim, _, c, foe) = Conduit();
        Assert.Equal("cast_from", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Chosen(fromConduit ? $"{c.Models[0].Id:N}" : "self"));
        if (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.SpendTokens }) sim.Answer(Samples.Team1, new Passed());
        Assert.Equal(fromConduit ? 1 : 0, CastRoll(sim).Modifiers.Where(m => m.Source == GfSpellSupport.SpellConduit).Sum(m => m.Amount));
        var targets = sim.AskForOrNull(Samples.Team1) is { Question: "spell_targets", Legal: PieceSelection ps } ? ps.Eligible : [];
        Assert.Equal(fromConduit, targets.Contains(foe.Id));                             // 10" from the conduit, 20" from the caster
    }

    [Theory]
    [InlineData("friend", 14, false)]                                                  // 12.74" base to base
    [InlineData("same unit", 10, false)]
    [InlineData("enemy", 10, false)]
    [InlineData("shaken", 10, false)]
    [InlineData("friend", 10, true)]
    public void N3_a_conduit_is_offered_only_within_12_from_another_friendly_unit_not_shaken(string where, double x, bool offered)
    {
        if (where == "shaken")
        {
            var w = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 3)]), TestProperties.TestBook);
            var c = Unit("C", "4+", "4+", [], 1, [P("Spell Conduit")]);
            var sim = Game([w, c], [Unit("F", "4+", "4+", [], 1, [])], 4);
            Put(sim, w.Models[0].Id, 0, 0); Put(sim, c.Models[0].Id, x, 0);
            Tokens(sim, w.Models[0].Id, 3); Shake(sim, c.Id);
            sim.Start(GfNames.CastingSeq, new SequenceContext(w.Id, [], [], null));
            sim.Answer(Samples.Team1, new Chosen(Pick(Bolt, w.Models[0].Id)));
            Assert.NotEqual("cast_from", sim.AskForOrNull(Samples.Team1)?.Question);
            return;
        }
        var (s, _, _, _) = Conduit(where, x);
        Assert.Equal(offered, s.AskForOrNull(Samples.Team1)?.Question == "cast_from");
    }

    // ---- Extended Buff Range ----

    /// <summary>Holds with the Hero's unit, picks its Bravery Buff and returns the units offered.</summary>
    static IReadOnlyList<Guid> BuffTargets(Sim sim, Unit hero)
    {
        sim.Append([new ActionRecorded(hero.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(hero.Id, [], [], GfNames.Hold));
        var key = GfPick.Key("Bravery Buff", hero.Models[0].Id);
        for (var i = 0; i < 6 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
        {
            if (ask.Question == "rule_target") return Assert.IsType<PieceSelection>(ask.Legal).Eligible;
            if (ask.Legal is Options o && o.Choices.Any(c => c.Id == key)) sim.Answer(Samples.Team1, new Chosen(key));
            else return [];
        }
        return [];
    }

    [Theory]
    [InlineData("in range", true)]
    [InlineData("target without the rule", false)]
    [InlineData("hero's unit without the rule", false)]
    [InlineData("beyond 24", false)]
    [InlineData("picker no hero", false)]
    public void X1_X2_a_heros_buff_reaches_a_unit_with_the_rule_within_24(string situation, bool offered)
    {
        var heroRules = new List<PropertyInstance> { P("Bravery Buff") };
        if (situation != "picker no hero") heroRules.Add(P("Hero"));
        if (situation != "hero's unit without the rule") heroRules.Add(P("Extended Buff Range"));
        var hero = Unit("H", "4+", "4+", [], 1, [.. heroRules]);
        var far = Unit("Far", "4+", "4+", [], 1, situation == "target without the rule" ? [] : [P("Extended Buff Range")]);
        var sim = Game([hero, far], [Unit("E", "4+", "4+", [], 1, [])]);
        Put(sim, hero.Models[0].Id, -20, -10);
        Put(sim, far.Models[0].Id, -20, situation == "beyond 24" ? 16 : 10);                // 20" (18.74" base to base), or 26"
        Assert.Equal(offered, BuffTargets(sim, hero).Contains(far.Id));
    }

    [Fact]
    public void X4_a_heros_spell_is_not_extended()
    {
        var hero = InBook(Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Extended Buff Range"), P("Caster", 3)]), TestProperties.TestBook);
        var near = Unit("Near", "4+", "4+", [], 1, [P("Extended Buff Range")]);
        var far = Unit("Far", "4+", "4+", [], 1, [P("Extended Buff Range")]);
        var sim = Game([hero, near, far], [Unit("E", "4+", "4+", [], 1, [])], 4);
        Put(sim, hero.Models[0].Id, -20, -10); Put(sim, near.Models[0].Id, -20, -4); Put(sim, far.Models[0].Id, -20, 10);
        Tokens(sim, hero.Models[0].Id, 3);
        Offered(sim, hero.Id);
        sim.Answer(Samples.Team1, new Chosen(Pick(Ward, hero.Models[0].Id)));
        if (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.SpendTokens }) sim.Answer(Samples.Team1, new Passed());
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal).Eligible;
        Assert.Contains(near.Id, targets);
        Assert.DoesNotContain(far.Id, targets);
    }
}
