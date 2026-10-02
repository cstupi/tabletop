# Task 17: Casting — spell and tokens, the simultaneous spend, the roll, targets, effects, and once-grants

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfCasting.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`, `server/game/Rules/GrimdarkFuture/GfHooks.cs` (`ReactionsAt` reads `SpellOptions`), `server/game/Effects/RollEffects.cs` (`ConsumeGrants`), `server/game/Rules/GrimdarkFuture/GfAttack.cs` (roll steps consume grants; `assign` applies a unit-level `Regeneration` grant to every model)
- Test: `tests/Tabletop.GameServer.Tests/GfCastingTests.cs`, `tests/Tabletop.GameServer.Tests/GfRollsTests.cs` (one fact)

**Interfaces:**
- Consumes: Tasks 9, 11–13; `GrantAdded`, `GrantRemoved`, `Grant`, `SpellTokensSet`, `PieceSelection`, `SelectedPieces`, `TokenSpend`, `TokensSpent` (Protocol); `CatalogEntry.Targeting`, `EffectDuration.Once`.
- Produces: `GfCasting.Casting()` (`casting`: `pick-spell`, `spend-tokens`, `roll`, `targets`, `effects`) and `GfCasting.SpellHits()` (`spell-hits`: `hit`, `resolve`); `static IRL<Guid> Casters(GameState, Guid unit)` (models with `Caster(X)` and tokens); `static IRL<string> TriedThisActivation(GameState, Guid unit)` (spells picked by this unit's own caster models in a closed `pick-spell` since the activation's `sequence_started` — `OneTryPerSpellScope = "activation"`; p.13's *"only one try per spell"* is a caster's own, so the tried set is kept per `(spell, caster model)` and another unit's caster that started casting in the same span may still try the spell); `static IRL<AskOption> SpellOptions(GameState, PropertySet, Guid unit)` (what `pick-spell` offers: one option per caster model and spell it can afford and has not tried; `GfHooks.ReactionsAt` offers the casting window only when it is non-empty, so a caster with tokens and nothing castable is never offered a window that would finish empty and reopen); `static IRL<ModelInPlay> EligibleSpenders(GameState, Guid casterModel)` (p.13's per-model test: on the table, holding tokens, within 18" and in line of sight of the caster's unit, **the caster included**, admitted without measurement; sight is one `Sight.From(state, spendingUnit)` per spending unit, and the order is the caster, then by nearest distance to a model of the caster's unit) and `static IRD<Guid,IRD<Guid,int>> SpendCapacity(GameState, Guid casterModel)` (the same models grouped by team, each keyed to the tokens it holds, which is `TokenSpend.MaxByModel` per team); `static IRL<Guid> SpellTargets(StepContext, Guid casterModel, CatalogEntry spell)` (per candidate unit: `Battlefield.InsideReach` first — all its alive models when a container wall is no bar, none when it is — else its living models within the spell's range of the caster model and in its line of sight through one `Sight.From(state, casterUnit)`; a unit-grain spell offers the units with any such model, a model-grain spell those models); `RollEffects.ConsumeGrants(LiveSet live, Side side, RollPlan plan) : IRL<StateChange>` (a `GrantRemoved` for every granted instance whose entry contributed a modifier to the plan and has one use left).
- The option id of `pick-spell` is `"{spell}|{casterModelId}"`, split at the last `|` (a caster id never holds one; a spell name may), so the paying model is in the Answer. The answer is read through `GfAnswers.Option`: a pass, another type or an id the Ask did not offer `Finish`es with no spend. Otherwise its `Changes` deduct the cost (`SpellTokensSet`). It records nothing else: the spend's caps are built by the step that opens the spend and travel in the Ask. `spend-tokens` is `Interpreter.Windows.SimultaneousSpend` then `Interpreter.Windows.Spends`, over `SpendCapacity`: the player names which of its own models pays what and each named model is deducted its own count, rather than the server taking them nearest the caster first (ruling 14). **`SpendCapacity` runs in `spend-tokens`, after `pick-spell`'s `SpellTokensSet` is applied**, so the caster's cap is what it holds with the spell paid for and no step subtracts the cost twice; a caster whose tokens exactly paid the cost holds none and is not an entry. `net_tokens` = own team's counts summed − everyone else's. `roll` is `GfRolls.CastingPlan(net_tokens)`, one die; a failure `Finish`es. `effects` reads the chosen targets through `GfAnswers.Pieces(state, frame, "targets", "spell_targets")`. `targets`: the spell's four columns — `Side` relative to the caster's team, `Grain` (unit ids or model ids), `Count` as the selection maximum (minimum 1), `Range` from the caster model, line of sight from the caster model (p.13) — as a `PieceSelection`. `effects` runs once per chosen target (`Values["target_index"]`, `Repeat`): every effect of the spell entry whose conditions hold, in ordinal order (`EffectsStep.Holding`, which `effects` and `hit` both read, judges each effect's own conditions with the caster as holder and the target unit opposite) — `AddHit` with no `RollResult` condition → `Push(spell-hits, (casterUnit, [target unit], [], null))`, whose `hit` step (reading the spell through the parent `casting` frame's pick) rolls `amount` dice on threshold 1 (every die a hit, so `on rolls of 6` rules read a real die) and writes `granted_<entry>_<amount>` for the hits' nested grants, then pushes `resolve-hits`; any effect with `Duration = Once` → `GrantAdded(targetUnit, new Grant(id, effect.Id, effect.TargetEntry ?? spell.Name, casterUnit, 1))` (a model-grain target's grant lands on its unit — the grants store is the unit's). Nothing else can appear: `Validate` (Task 2) refuses a spell whose top-level effect is neither `add_hit` nor `duration = once`, which is what spell effects read in all forty-eight — *"always with one duration: once"*.
- Consumption: a granted instance (`LiveProperty.GrantId` set) whose entry contributed to a roll plan is consumed by that roll's step (`RollEffects.ConsumeGrants` in `roll-to-hit`/`roll-to-block`); a granted `Regeneration` is consumed by `assign` when it rolls. `Grant.RemainingUses` is 1 for every once-effect (spell effects → The grants store: "remaining uses (one)").

Spec: action sequences → Grimdark Future's sequences (Casting, The token economy), Windows (sealed simultaneous; invoked by the actor); spell effects → Duration, The grants store (written by the effect step's `step_completed`, consumed by the consuming step's, read by both passes and by name checks), Damage spells need nothing new (kind 6 with a nested grant; a hit roll that cannot fail), Target selection (one to `target_count`, each target independently); rulebook p.13 `Caster`.

- [ ] **Step 1: Write the test spell book and the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfCastingTests
{
    static EffectDocument Fx(EffectKind kind, string? targetEntry = null, int? amount = null, EffectDuration duration = EffectDuration.Interaction, params EffectDocument[] children) =>
        new(EffectSubject.Self, 10, kind, null, null, targetEntry, null, null, null, null, amount, false, null, null, null, duration, [], children);
    static readonly EntryDocument Fireball = new("Fireball", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits with AP(1).",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), [Fx(EffectKind.AddHit, amount: 3, children: [Fx(EffectKind.GrantProperty, "AP", 1)])]);
    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24), [Fx(EffectKind.GrantProperty, "Regeneration", null, EffectDuration.Once)]);
    static readonly PropertySet Book = TestProperties.WithSpells(Fireball, Ward);

    static Sim Casting(Tabletop.Content.Unit wizard, Tabletop.Content.Unit[] friends, Tabletop.Content.Unit[] enemies, params int[] dice)
    {
        var state = State([wizard, .. friends], enemies, x1: -4, x2: 4);
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Book, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        return sim;
    }

    [Fact]
    public void Fireball_spends_tokens_the_spend_is_sealed_and_the_hits_roll_a_hit_roll_that_cannot_fail()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var enemyCaster = Unit("Warlock", "4+", "4+", [], 1, [P("Caster", 1)]); var grunts = Unit("Grunts", "4+", "5+", [], 3, []);
        var sim = Casting(wizard, [], [grunts, enemyCaster], 5, 6, 1, 1, 2, 3, 4);
        sim.Append([new SpellTokensSet(enemyCaster.Models[0].Id, 2)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        var pick = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([$"Fireball|{wizard.Models[0].Id}", $"Ward|{wizard.Models[0].Id}"], pick.Choices.Select(c => c.Id).OrderBy(x => x));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        Assert.Equal(1, sim.Model(wizard.Models[0].Id).SpellTokens);
        var spend = sim.State.Projection.OpenAsks.Values.Single().Ask;
        var caps = Assert.IsType<TokenSpend>(spend.Legal).MaxByModel;
        Assert.True(spend.Sealed); Assert.Equal(2, spend.Teams.Count);                                    // the wizard's own remaining token, and the warlock's two
        Assert.Equal((1, 2), (caps[wizard.Models[0].Id], caps[enemyCaster.Models[0].Id]));                // the caster's cap is what the cost left it (p.13)
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team2, new TokensSpent(new Dictionary<Guid, int> { [enemyCaster.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Model(enemyCaster.Models[0].Id).SpellTokens);
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal((GfNames.Casting, 4, 5, 4, true), (roll.Roll, roll.Threshold, roll.Dice[0].Raw, roll.Dice[0].Modified, roll.Dice[0].Success));
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 1), (targets.Min, targets.Max)); Assert.Contains(grunts.Id, targets.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([grunts.Id]));
        var hit = sim.Steps("hit").Single();
        Assert.Equal((GfNames.Hit, 1, 3, 3), (hit.Rolls[0].Roll, hit.Rolls[0].Threshold, hit.Rolls[0].Dice.Count, hit.Rolls[0].Dice.Count(d => d.Success)));
        Assert.Equal(3, hit.Values!["granted_AP_1"]);
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Contains(block.Modifiers, m => m.Source == "AP" && m.Amount == -1);
        Assert.Equal(3, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);            // 2,3,4 −1 against 5+: three wounds
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [grunts.Models[0].Id] = 1, [grunts.Models[1].Id] = 1, [grunts.Models[2].Id] = 1 }));
        Assert.Equal(0, sim.Alive(grunts.Id)); Assert.Empty(sim.State.Projection.Stack);
        Assert.Contains("Fireball", GfCasting.TriedThisActivation(sim.State, wizard.Id));
    }

    [Fact]
    public void A_failed_roll_ends_casting_and_a_tried_spell_is_not_offered_again_this_activation()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Casting(wizard, [], [Unit("G", "4+", "4+", [], 2, [])], 2);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        Assert.Equal([Samples.Team1], sim.AskFor(Samples.Team1).Teams);                              // no enemy caster in range: only the caster itself may spend
        sim.Answer(Samples.Team1, new Passed());
        Assert.Equal(0, sim.Steps("roll").Single().Values!["passed"]);
        Assert.Equal(1, sim.Model(wizard.Models[0].Id).SpellTokens);                                  // a failed cast keeps its cost spent
        Assert.Empty(sim.State.Projection.Stack);                                                     // casting finished; no activation frame in this test, so the scan covers the whole log
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        Assert.Equal([$"Ward|{wizard.Models[0].Id}"], Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices.Select(c => c.Id));
    }

    [Fact]
    public void One_try_per_spell_is_per_caster_so_another_units_caster_may_still_try_it()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var other = Unit("Other", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Casting(wizard, [other], [Unit("G", "4+", "4+", [], 2, [])], 2);
        sim.Append([new SpellTokensSet(other.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());                                                      // the roll fails
        sim.Start(GfNames.CastingSeq, new SequenceContext(other.Id, [], [], null));
        Assert.Equal([$"Fireball|{other.Models[0].Id}", $"Ward|{other.Models[0].Id}"], Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices.Select(c => c.Id).OrderBy(x => x));
        Assert.Empty(GfCasting.TriedThisActivation(sim.State, other.Id));
        Assert.Equal(["Fireball"], GfCasting.TriedThisActivation(sim.State, wizard.Id));
    }

    [Fact]
    public void A_grant_key_splits_at_its_last_underscore_so_an_entry_name_may_hold_one()
    {
        Assert.Equal(("AP", 2), GfAttack.ParseGrantKey("granted_AP_2"));
        Assert.Equal(("Life_Drain", 1), GfAttack.ParseGrantKey("granted_Life_Drain_1"));
    }

    [Fact]
    public void A_tried_spell_is_dropped_from_the_next_pick_while_a_spell_still_affordable_stays()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Casting(wizard, [], [Unit("G", "4+", "4+", [], 2, [])], 2);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        Assert.Equal(0, sim.Steps("roll").Single().Values!["passed"]);
        Assert.Equal(4, sim.Model(wizard.Models[0].Id).SpellTokens);                                  // Fireball (2) is still affordable: only the tried list can hide it
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        Assert.Equal([$"Ward|{wizard.Models[0].Id}"], Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices.Select(c => c.Id));
    }

    static void Put(Sim sim, Tabletop.Content.Unit unit, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(unit.Models[0].Id, sim.Model(unit.Models[0].Id).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    [Fact]
    public void Only_token_holders_within_18_inches_and_in_sight_of_the_caster_reach_the_spend_window()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var near = Unit("Near", "4+", "4+", [], 1, [P("Caster", 1)]); var far = Unit("Far", "4+", "4+", [], 1, [P("Caster", 1)]);
        var hidden = Unit("Hidden", "4+", "4+", [], 1, [P("Caster", 1)]); var wall = Unit("Wall", "4+", "4+", [], 1, []);
        var sim = Casting(wizard, [near, far, hidden, wall], [Unit("G", "4+", "4+", [], 1, [])], 2);
        foreach (var m in new[] { near, far, hidden }) sim.Append([new SpellTokensSet(m.Models[0].Id, 1)]);
        Put(sim, near, -16, -20);                 // 12" west, clear
        Put(sim, far, -24, -20);                  // 20" west, clear: out of range only
        Put(sim, wall, -4, -14); Put(sim, hidden, -4, -8);      // 12" north, in range but behind the wall
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));
        var caps = Assert.IsType<TokenSpend>(sim.AskFor(Samples.Team1).Legal).MaxByModel;
        Assert.DoesNotContain(far.Models[0].Id, caps.Keys);
        Assert.DoesNotContain(hidden.Models[0].Id, caps.Keys);
        Assert.Equal(new HashSet<Guid> { wizard.Models[0].Id, near.Models[0].Id }, caps.Keys.ToHashSet());
    }

    [Fact]
    public void A_target_beyond_the_spells_range_or_hidden_behind_another_unit_is_not_offered()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var near = Unit("Near", "4+", "4+", [], 1, []); var far = Unit("Far", "4+", "4+", [], 1, []);
        var wall = Unit("Wall", "4+", "4+", [], 1, []); var hidden = Unit("Hidden", "4+", "4+", [], 1, []);
        var sim = Casting(wizard, [], [near, far, wall, hidden], 5);
        Put(sim, near, 4, -20);                   // 8" east, clear
        Put(sim, far, -24, -20);                  // 20" west, clear: out of Fireball's 12"
        Put(sim, wall, -4, -15); Put(sim, hidden, -4, -9);      // 11" north, in range but behind the wall
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.DoesNotContain(far.Id, targets.Eligible);
        Assert.DoesNotContain(hidden.Id, targets.Eligible);
        Assert.Equal(new HashSet<Guid> { near.Id, wall.Id }, targets.Eligible.ToHashSet());
    }

    [Fact]
    public void A_hit_effect_whose_own_condition_fails_rolls_no_hits()
    {
        var spark = new EntryDocument("Spark", CatalogKind.Spell, false, null, 1, "Test Book", "Target enemy unit within 12\" takes 1 hit, and 2 more in round 99.",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [Fx(EffectKind.AddHit, amount: 1), Fx(EffectKind.AddHit, amount: 2) with { Conditions = [new ConditionDocument(ConditionKind.RoundNumber, null, Comparison.Eq, 99, [])] }]);
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]); var target = Unit("T", "4+", "4+", [], 2, []);
        var state = State([wizard], [target], x1: -4, x2: 4);
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), TestProperties.WithSpells(spark), new ScriptedDice(5, 4, 4, 4));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Spark|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Single(sim.Steps("hit").Single().Rolls.Single().Dice);
    }

    [Fact]
    public void Ward_grants_regeneration_once_to_two_units_and_the_grant_is_consumed_when_it_applies()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var a = Unit("A", "4+", "4+", [], 2, []); var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Casting(wizard, [a, b], [Unit("G", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1))], 4, 6, 1);
        sim.Append([new ModelsMoved(b.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location,
            new OnTable(new Placement(((OnTable)sim.Model(m.Id).Location).Placement.Position with { X = 0 }, new Yaw(0))))).ToList())]);   // B off A's line of sight
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());                                                          // the caster declines to add its own two
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 2), (targets.Min, targets.Max)); Assert.Equal(new HashSet<Guid> { wizard.Id, a.Id, b.Id }, targets.Eligible.ToHashSet());
        sim.Answer(Samples.Team1, new SelectedPieces([a.Id, b.Id]));
        Assert.All(new[] { a.Id, b.Id }, u => Assert.Equal(("Regeneration", 1, wizard.Id), (sim.Unit(u).Grants.Single().EntryName, sim.Unit(u).Grants.Single().RemainingUses, sim.Unit(u).Grants.Single().CasterUnitId)));
        Assert.True(Lookup.UnitHas(sim.State, Book, a.Id, "Regeneration"));
        // A wound on A: the granted Regeneration rolls (6: ignored) and is consumed.
        var shooter = sim.State.Board.Armies[1].Snapshot.Units[0];
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(shooter.Id, [a.Id], [], GfNames.Hold)), new StepCompleted(parent, "w", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = 1 } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(shooter.Id, [a.Id], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [a.Models[0].Id] = 1 }));
        Assert.Equal("Regeneration", sim.Steps("assign").Single().Rolls.Single().Roll);
        Assert.Equal(2, sim.Alive(a.Id));
        Assert.Empty(sim.Unit(a.Id).Grants); Assert.Single(sim.Unit(b.Id).Grants);
    }

    [Fact]
    public void The_caster_adds_its_own_tokens_left_after_the_cost_to_its_own_roll()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Casting(wizard, [], [Unit("G", "4+", "4+", [], 1, [])], 2);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));                             // Ward costs 1 of 3
        var spend = sim.AskFor(Samples.Team1);
        Assert.Equal([Samples.Team1], spend.Teams);
        Assert.Equal(new Dictionary<Guid, int> { [wizard.Models[0].Id] = 2 }, Assert.IsType<TokenSpend>(spend.Legal).MaxByModel);
        sim.Answer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [wizard.Models[0].Id] = 2 }));
        Assert.Equal(0, sim.Model(wizard.Models[0].Id).SpellTokens);
        Assert.Equal(2, sim.Steps("spend-tokens").Single().Values!["net_tokens"]);
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal((2, 4, true), (roll.Dice[0].Raw, roll.Dice[0].Modified, roll.Dice[0].Success));      // +1 per token spent (p.13)
        sim.Answer(Samples.Team1, new SelectedPieces([wizard.Id]));
        Assert.Single(sim.Unit(wizard.Id).Grants);
    }

    [Fact]
    public void The_team_names_which_of_its_models_pays_and_the_others_keep_their_tokens()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var acolyte = Unit("Acolyte", "4+", "4+", [], 1, [P("Caster", 1)]);
        var sim = Casting(wizard, [acolyte], [Unit("G", "4+", "4+", [], 1, [])], 2);
        sim.Append([new SpellTokensSet(acolyte.Models[0].Id, 2)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));
        Assert.Equal([2, 2], Assert.IsType<TokenSpend>(sim.AskFor(Samples.Team1).Legal).MaxByModel.Values);
        sim.Answer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [acolyte.Models[0].Id] = 2 }));
        Assert.Equal(0, sim.Model(acolyte.Models[0].Id).SpellTokens);
        Assert.Equal(2, sim.Model(wizard.Models[0].Id).SpellTokens);                                      // kept for its own spell: no nearest-first payer takes them
        Assert.Equal(2, sim.Steps("spend-tokens").Single().Values!["net_tokens"]);
        sim.Answer(Samples.Team1, new SelectedPieces([wizard.Id]));
        Assert.Single(sim.Unit(wizard.Id).Grants);
    }

    [Fact]
    public void A_granted_rule_on_the_shooter_is_spent_once_by_the_block_step_however_many_pools_it_modified()
    {
        var sharpen = new EntryDocument("Sharpen", CatalogKind.Spell, false, null, 1, "Test Book", "Target friendly unit gets AP(1) once.",
            new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12), [Fx(EffectKind.GrantProperty, "AP", 1, EffectDuration.Once)]);
        var book = TestProperties.WithSpells(sharpen);
        var shooter = Unit("Shooter", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1, P("Rending")));
        var target = Unit("T", "4+", "2+", [], 1, []);
        var sim = new Sim(State([shooter], [target], x1: -4, x2: 4), GrimdarkFutureRules.Instance, book, new ScriptedDice(6, 4, 6, 6));
        var grantId = Guid.CreateVersion7();
        sim.Append([new GrantAdded(shooter.Id, new Grant(grantId, book.Entry("Sharpen")!.Effects[0].Id, "AP", shooter.Id, 1))]);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        var block = sim.Steps("roll-to-block").Single();
        Assert.Equal(2, block.Rolls.Count);                                                                // the Rending group and the plain hit: two pools, both AP(1)-modified
        Assert.Equal([grantId], block.Changes.OfType<GrantRemoved>().Select(c => c.GrantId));              // one removal for the one instance
        Assert.Empty(sim.Unit(shooter.Id).Grants);
    }
    [Fact]
    public void A_spell_whose_name_holds_the_option_separator_is_picked_spent_and_tried()
    {
        // The option id is "<spell>|<caster>", and a caster id never holds '|', so the split is at the last one.
        var odd = Ward with { Name = "Ward|Bolt" };
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var state = State([wizard], [Unit("G", "4+", "4+", [], 2, [])], x1: -4, x2: 4);
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), TestProperties.WithSpells(odd), new ScriptedDice(2));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|Bolt|{wizard.Models[0].Id}"));
        Assert.Equal(2, sim.Model(wizard.Models[0].Id).SpellTokens);
        sim.Answer(Samples.Team1, new Passed());                                                      // spend nothing; the 2 fails
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(["Ward|Bolt"], GfCasting.TriedThisActivation(sim.State, wizard.Id));
    }
}
```

