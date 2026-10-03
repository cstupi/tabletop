# Task 7: Spell hits: the roll sentence, the spell attack kind, a model target, the casting roll

**Files:**
- Modify: `server/game/Effects/Interaction.cs` (`AttackKind.Spell`)
- Modify: `server/game/Effects/Conditions.cs` (`attack_kind` reads `spell`)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`KindOf(StepContext)`, `Settle`, `TakedownModel`, `AssignStep`'s interaction and wound multiplier)
- Modify: `server/game/Rules/GrimdarkFuture/GfCasting.cs` (`HitStep`, `RollStep`)
- Modify: `server/game/Rules/GrimdarkFuture/GfRolls.cs` (`CastingPlan`)
- Modify: `server/game/Properties/PropertySetDocument.cs` (validation: `target_roll` on a spell's `add_hit` is `hit`)
- Create: `tests/Tabletop.GameServer.Tests/SpellShapeTests.cs`

**Interfaces:**
- Consumes: `RollEffects.Consume`, `LiveSet.Read` (Task 6); `TestProperties.WithSpells` taking rule entries (Task 1).
- Produces: `AttackKind { Shooting, Melee, Spell }`; `GfAttack.KindOf(StepContext ctx)`; `GfRolls.CastingPlan(StepContext ctx, LiveSet live, int netTokens)`; `GfAttack.GrantedOf(FrameView frame) → IReadOnlyList<(string Entry, int Amount, int Count)>` (the `granted_*` keys of the nearest attacking frame).

Spec → *Translation* and *Grammar additions*. A spell rolls its hits only when it prints the roll sentence (`target_roll = hit`). Spell hits resolve with attack kind `spell`; today `KindOf` answers `Melee` for a weapon-less list. A model target is a Takedown target. The casting roll reads the caster unit's live set. Spell hits must also carry their weapon rules' multipliers, which today they do not: `HitStep` writes `hit_multiplier = 1` whatever the spell's `Blast`, and the assign step's wound multiplier reads a live set that holds none of the spell's granted rules, so `Blast(3)` and `Deadly(3)` on a spell multiply nothing.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/SpellShapeTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class SpellShapeTests
{
    static EffectDocument Grant(string entry, int? amount = null) => new(EffectSubject.Self, 20, EffectKind.GrantProperty, null, null, entry, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, [], []);
    static EffectDocument Hits(int n, string? roll, params EffectDocument[] rules) => new(EffectSubject.Self, 10, EffectKind.AddHit, roll, null, null, null, null, null, null, n, false, null, null, null, EffectDuration.Interaction, [], rules);
    static EntryDocument Spell(string name, TargetGrain grain, EffectDocument effect) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Enemy, grain, 1, 24), [effect]);
    static readonly EntryDocument MeleeGuard = new("Melee Guard", CatalogKind.Model, false, Aggregation.All, null, null, "+3 to defense rolls in melee.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "defense", null, null, null, null, null, null, 3, false, null, null, null, EffectDuration.Interaction,
            [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["melee"])], [])]);
    static readonly EffectDocument CastingMinus3 = new(EffectSubject.Self, 10, EffectKind.ModifyRoll, "casting", null, null, null, null, null, null, -3, false, null, null, null, EffectDuration.Once, [], []);

    static readonly PropertySet Props = TestProperties.WithSpells(MeleeGuard,
        Spell("Quiet Bolt", TargetGrain.Unit, Hits(3, null)),
        Spell("Loud Bolt", TargetGrain.Unit, Hits(2, "hit", Grant("Surge"))),
        Spell("Lance", TargetGrain.Model, Hits(2, null, Grant("AP", 4))),
        Spell("Boom", TargetGrain.Unit, Hits(2, null, Grant("Blast", 3))),
        Spell("Pin Boom", TargetGrain.Model, Hits(2, null, Grant("Blast", 3))),
        new EntryDocument("Hex", CatalogKind.Spell, false, null, 1, null, "Hex.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24), [CastingMinus3]));

    static (Sim Sim, Tabletop.Content.Unit Wizard, Tabletop.Content.Unit Target) Cast(string spell, Tabletop.Content.Unit target, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard], [target], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        return (sim, wizard, target);
    }

    [Fact]
    public void A_spell_that_prints_no_roll_sentence_rolls_no_hit_dice()
    {
        var (sim, _, t) = Cast("Quiet Bolt", Unit("T", "4+", "6+", [], 5, []), 5, 1, 1, 1);   // cast 5; three blocks of 1
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Empty(sim.Steps("hit").Single().Rolls);
        Assert.Equal(3, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
    }

    [Fact]
    public void A_spell_that_prints_the_roll_sentence_rolls_one_die_per_hit_and_surge_triggers()
    {
        var (sim, _, t) = Cast("Loud Bolt", Unit("T", "4+", "6+", [], 5, []), 5, 6, 2, 1, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Equal(2, sim.Steps("hit").Single().Rolls.Single().Dice.Count);
        Assert.Equal(3, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);                  // 2 hits, a 6 surges one more
    }

    [Fact]
    public void Spell_hits_are_not_melee_so_an_in_melee_rule_does_not_reach_them()
    {
        var (sim, _, t) = Cast("Quiet Bolt", Unit("T", "4+", "6+", [], 5, [P("Melee Guard")]), 5, 1, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.DoesNotContain(sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers, m => m.Source == "Melee Guard");
    }

    [Fact]
    public void A_model_target_takes_its_wounds_as_a_unit_of_one_with_no_allocation()
    {
        var (sim, _, t) = Cast("Lance", Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]), 5, 1, 1);
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[1].Id]));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal((0, 2, 0), (sim.Model(t.Models[0].Id).Wounds, sim.Model(t.Models[1].Id).Wounds, sim.Model(t.Models[2].Id).Wounds));
    }

    [Fact]
    public void Blast_on_a_spell_multiplies_its_hits_and_a_model_target_caps_it_at_one()
    {
        var (unit, _, t1) = Cast("Boom", Unit("T", "4+", "6+", [], 5, []), [5, .. Enumerable.Repeat(6, 6)]);
        unit.Answer(Samples.Team1, new SelectedPieces([t1.Id]));
        Assert.Equal(6, unit.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
        var (pin, _, t2) = Cast("Pin Boom", Unit("T", "4+", "6+", [], 5, []), 5, 6, 6);
        pin.Answer(Samples.Team1, new SelectedPieces([t2.Models[0].Id]));
        Assert.Equal(2, pin.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
    }

    [Fact]
    public void A_casting_modifier_grant_applies_to_the_units_next_casting_roll_and_is_spent()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard], [Unit("T", "4+", "4+", [], 1, [])], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new GrantAdded(wizard.Id, new Grant(Guid.CreateVersion7(), Props.Entry("Hex")!.Effects[0].Id, "Hex", wizard.Id, 1))]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Quiet Bolt|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Contains(roll.Modifiers, m => m.Source == "Hex" && m.Amount == -3);
        Assert.Equal(0, sim.Steps("roll").Single().Values!["passed"]);                                   // 6 − 3 = 3, short of 4
        Assert.Empty(sim.Unit(wizard.Id).Grants);
    }
}
```

The `Hex` grant is applied by hand because the effects step never writes a grant on the caster's own unit for an enemy-side spell; what is under test is the casting roll's reading.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~SpellShapeTests`
Expected: FAIL: hit dice are rolled for `Quiet Bolt`; `Melee Guard` modifies the block; the model target asks for an allocation; `Boom` blocks 2 not 6; the casting roll has no `Hex` modifier.

- [ ] **Step 3: The `spell` attack kind**

In `server/game/Effects/Interaction.cs`: `public enum AttackKind { Shooting, Melee, Spell }`.

In `server/game/Effects/Conditions.cs`, the `AttackKind` case:

```csharp
            case ConditionKind.AttackKind: return ix.Attack is { } k && Membership(c, k switch { AttackKind.Shooting => "shooting", AttackKind.Melee => "melee", _ => "spell" });
```

In `GfAttack`, add beside `KindOf(GameState, ...)`, and use it in `Settle`:

```csharp
    /// <summary>The attack kind of the frame's group: a weapon-less group under a spell-hits frame is a spell's hits (spell library → `spell`); a
    /// weapon-less group elsewhere is Impact's, which is melee; otherwise the weapons decide.</summary>
    public static AttackKind KindOf(StepContext ctx)
    {
        if (ctx.Frame.Context.WeaponIds.Count == 0)
            for (var f = ctx.Frame; f is not null; f = f.Parent)
                if (f.Frame.Sequence == GfNames.SpellHitsSeq) return AttackKind.Spell;
        return KindOf(ctx.State, ctx.Frame.Context.WeaponIds);
    }
```

```csharp
        var kind = KindOf(ctx);
```

- [ ] **Step 4: A spell's granted rules reach the multipliers and the model target**

In `GfAttack`, add:

```csharp
    /// <summary>The `granted_&lt;entry&gt;_&lt;amount&gt;` keys of the nearest attack-sequence or spell-hits frame above this one: the rules the group's
    /// hits carry (a weapon's per-die grants, a spell's weapon rules), with how many hits carry each.</summary>
    public static IReadOnlyList<(string Entry, int Amount, int Count)> GrantedOf(FrameView frame)
    {
        for (var f = frame.Parent; f is not null; f = f.Parent)
            if (f.Frame.Sequence is GfNames.AttackSequence or GfNames.SpellHitsSeq)
                return f.Values.Where(kv => kv.Key.StartsWith("granted_")).Select(kv => { var (e, a) = ParseGrantKey(kv.Key); return (e, a, kv.Value); }).ToList();
        return [];
    }
```

Extend `TakedownModel` to read a casting frame's current target:

```csharp
    public static Guid? TakedownModel(FrameView frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
        {
            if (f.Frame.Sequence == GfNames.AttackSequence)
                return f.Frame.Steps.FirstOrDefault(s => s.Step == "pick-target-model")?.Answer is SelectedPieces { PieceIds.Count: 1 } p ? p.PieceIds[0] : null;
            // A model-grain spell is "resolved as if the target was a unit of [1]" (p.14's Takedown wording): its current target, by the
            // effects step's target_index. A unit id here is not one of the target's models, and the assign step's check discards it.
            if (f.Frame.Sequence == GfNames.CastingSeq)
                return f.Frame.Steps.FirstOrDefault(s => s.Step == "targets" && s.Answer is SelectedPieces)?.Answer is SelectedPieces t && f.Value("target_index") < t.PieceIds.Count
                    ? t.PieceIds[f.Value("target_index")] : null;
        }
        return null;
    }
```

In `AssignStep.Run`, the interaction carries the kind and the wound multiplier sees the group's granted rules:

```csharp
            var ix = AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, actor is null ? null : KindOf(ctx), ctx.Frame.Context.Action);
            var live = LiveProperties.Settle(ix);
            var targetSide = actor is null ? Side.Attacker : Side.Defender;
            var withGranted = GrantedOf(ctx.Frame).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(withGranted, Side.Attacker);
```

- [ ] **Step 5: The spell-hits step**

Replace `GfCasting.HitStep.Run`:

```csharp
        public StepOutcome Run(StepContext ctx)
        {
            var spell = ctx.Properties.Entry(Picked(ctx.Frame.Parent!).Spell)!;
            var casterUnit = ctx.Frame.Context.ActorUnitId!.Value;
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var modelTarget = GfAttack.TakedownModel(ctx.Frame) is { } m && Lookup.AliveModels(ctx.State, target).Contains(m);
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = 1, ["hits"] = 0 };
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, casterUnit, target, [], AttackKind.Spell, null));
            var total = 0; var multiplier = 1;
            foreach (var hit in EffectsStep.Holding(ctx, casterUnit, target, spell).Where(e => e.Kind == EffectKind.AddHit))
            {
                var n = hit.EffectiveAmount(null);
                var rules = hit.Children.Where(c => c.Kind == EffectKind.GrantProperty).ToList();
                var granted = rules.Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.TargetEntry!, g.Amount));
                if (hit.TargetRoll is null) { total += n; values["hits"] += n; }
                else
                {
                    // "Roll as many dice as hits to see if "on rolls of 6" effects trigger": a hit roll that cannot fail (threshold 1).
                    var record = ctx.Roll(new Dice.RollPlan(GfNames.Hit, 1, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
                    total += Dice.RollOutcome.Successes(record);
                    foreach (var die in record.Dice) values["extra_hits"] += RollEffects.PerDie(granted, Side.Attacker, die, GfNames.Hit).ExtraHits;
                }
                // Blast's cap is "as many hits as models in the target unit"; a model target is a unit of one.
                multiplier = Math.Max(multiplier, RollEffects.HitMultiplier(granted, Side.Attacker, modelTarget ? 1 : Lookup.LivingModels(ctx.State, target).Count));
                foreach (var g in rules) { var key = $"granted_{g.TargetEntry}_{g.Amount}"; values[key] = values.GetValueOrDefault(key) + n; }
            }
            values["hit_multiplier"] = multiplier;
            return total == 0 ? new Finish([], values) : new Complete([], values);
        }
```

`GfAttack.HitsFrom` already reads `hits` when the frame holds no hit roll, and sums `extra_hits` and `hit_multiplier` when it does. For an unrolled spell, `HitsFrom` returns `hits` without the multiplier; make it apply the multiplier on both paths:

```csharp
        if (hitRolls.Count == 0) return parent.Value("hits") * Math.Max(1, parent.Value("hit_multiplier", 1));
```

Every existing frame without hit rolls (Impact) writes no `hit_multiplier`, so its count is unchanged.

- [ ] **Step 6: The casting roll reads the live set**

In `GfRolls`:

```csharp
    /// <summary>p.13's casting roll at 4+, the tokens spent for and against it, and the caster unit's own casting modifiers (a grant such as
    /// "-3 to casting rolls once").</summary>
    public static RollPlan CastingPlan(StepContext ctx, LiveSet live, int netTokens) =>
        ctx.Plan(live, Side.Attacker, GfNames.Casting, netTokens == 0 ? [] : [new RollModifier("tokens", netTokens)], threshold: 4);
```

In `GfCasting.RollStep.Run`:

```csharp
            var (_, caster) = Picked(ctx.Frame);
            var casterUnit = ctx.Projection.Models[caster].UnitId;
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, casterUnit), null, null, null, null, Battlefield.TerrainUnder(ctx.State, casterUnit)));
            var passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.CastingPlan(ctx, live, ctx.Frame.Value("net_tokens")), 1)) == 1;
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0 };
            var spent = RollEffects.Consume(live);
            return passed ? new Complete(spent, values) : new Finish(spent, values);
```

`Spec(GfNames.Casting)` keeps its D6 convention; `ctx.Plan` reads it.

- [ ] **Step 7: Validate a spell's roll**

In `PropertiesJson.Validate`, inside the `if (spell)` block:

```csharp
            if (spell) foreach (var f in e.Effects.Where(f => f.Kind == EffectKind.AddHit && f.TargetRoll is not null && f.TargetRoll != "hit"))
                problems.Add($"{e.Name}: a spell's hits roll as 'hit' or not at all; target_roll is '{f.TargetRoll}'");
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. `Fireball` in `GfCastingTests` declares no `target_roll`, so it now rolls no hit dice: update its assertion `Assert.Equal((GfNames.Hit, 1, 3, 3), ...)` on the `hit` step to `Assert.Empty(hit.Rolls)` with `Assert.Equal(3, hit.Values!["hits"])`, and drop the hit dice from its `ScriptedDice` list (the casting roll's 5 stays first).

- [ ] **Step 9: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests
git commit -m "Resolve spell hits as spells: the roll sentence, Blast and Deadly, a model target, the casting roll's modifiers"
```
