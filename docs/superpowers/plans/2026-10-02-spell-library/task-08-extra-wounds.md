# Task 8: Extra wounds

**Files:**
- Modify: `server/game/Effects/RollEffects.cs` (`DieEffects.ExtraWounds`; `PerDie` splits hits from wounds)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`RollToHitStep` reads every die for wounds; `RollToBlockStep` reads defense dice; `WoundsToDeal` adds them; a `self-wounds` step)
- Modify: `server/game/Rules/GrimdarkFuture/GfCasting.cs` (`SpellHits` gains the `self-wounds` step; `HitStep` records wounds)
- Modify: `server/game/Properties/PropertySetDocument.cs` (`target_quantity` on `add_hit` is `hits` or `wounds`)
- Test: `tests/Tabletop.GameServer.Tests/SpellShapeTests.cs`, `tests/Tabletop.GameServer.Tests/RollEffectsTests.cs`

**Interfaces:**
- Consumes: `GfAttack.KindOf(StepContext)`, `GrantedOf` (Task 7).
- Produces: `sealed record DieEffects(int ExtraHits, int ExtraWounds, IReadOnlyList<(string Entry, int? Parameter)> Grants)`, where `ExtraWounds` are wounds the rolling side's unit takes; `GfAttack.SelfWoundsStep` (step name `self-wounds`).

Spec → *Grammar additions*, `add_hit` takes `target_quantity`. `RollEffects.PerDie` today sums every `add_hit` as extra hits and ignores `target_quantity`, and no step reads a defense die for anything but success: the engine work is new. A die's subject names who takes the wound: subject `opponent` on a defense die is *Shred* (*"On unmodified results of 1 to block hits, this weapon deals 1 extra wound"*), subject `self` on a hit die is *Hazardous* (*"this model's unit takes one wound on unmodified rolls of 1 to hit"*). In both, the side the effect acts on is the side that rolled the die, so `PerDie`'s `roller` is who takes the wound.

- [ ] **Step 1: Write the failing tests**

Add to `SpellShapeTests` (the rules are named for this test so they never collide with the shipped part's *Shred* and *Hazardous*):

```csharp
    static ConditionDocument Raw(string roll, int face) => new(ConditionKind.RollResult, null, Comparison.Eq, face, [roll, "raw"]);
    static readonly EntryDocument TestShred = new("Test Shred", CatalogKind.Weapon, false, null, null, null, "Defense 1s take an extra wound.", null,
        [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.AddHit, null, null, null, null, TargetQuantity.Wounds, null, null, 1, false, null, null, null, EffectDuration.Interaction, [Raw("defense", 1)], [])]);
    static readonly EntryDocument TestHazard = new("Test Hazard", CatalogKind.Weapon, false, null, null, null, "Hit 1s wound this unit.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, TargetQuantity.Wounds, null, null, 1, false, null, null, null, EffectDuration.Interaction, [Raw("hit", 1)], [])]);
    static readonly PropertySet Weapons = TestProperties.WithSpells(TestShred, TestHazard);

    [Fact]
    public void A_defense_one_against_a_shred_weapon_is_one_more_wound()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Test Shred"))); var b = Unit("B", "4+", "4+", [], 5, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Weapons, new ScriptedDice(5, 5, 1, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);               // one failed block, plus one for its 1
    }

    [Fact]
    public void A_hit_one_with_a_hazardous_weapon_wounds_the_attackers_own_unit()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Gun("Pistol", "12\"", 1, P("Test Hazard"))); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Weapons, new ScriptedDice(1, 5, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Pistol"] = b.Id }));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["self_wounds"]);
        var removed = a.Models.Count(m => sim.Model(m.Id).Location is OnShelf);
        Assert.Equal(1, removed + (sim.AskForOrNull(Samples.Team1) is { Legal: Allocation } ? 1 : 0));   // removed, or awaiting its own allocation
    }
```

Add to `RollEffectsTests`:

```csharp
    [Fact]
    public void Per_die_wounds_are_counted_apart_from_extra_hits()
    {
        var effects = new DieEffects(1, 2, []);
        Assert.Equal((1, 2), (effects.ExtraHits, effects.ExtraWounds));
    }
```

Fix the two existing `RollEffectsTests` assertions on `DieEffects` (line 77 and 79) to read `six.ExtraHits` and `five.ExtraHits` as before; they keep compiling because the record's first member is still `ExtraHits`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~SpellShapeTests|FullyQualifiedName~RollEffectsTests"`
Expected: compile error on `DieEffects(1, 2, [])`, then the shred allocation reads 1 and `self_wounds` is missing.

- [ ] **Step 3: Split hits from wounds per die**

In `server/game/Effects/RollEffects.cs`:

```csharp
/// <summary>What one die does beyond its success, for the side that rolled it: extra hits it scores, extra wounds that side's unit takes, per-hit grants.</summary>
public sealed record DieEffects(int ExtraHits, int ExtraWounds, IReadOnlyList<(string Entry, int? Parameter)> Grants);
```

```csharp
    public static DieEffects PerDie(LiveSet live, Side roller, Die die, string roll)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified);
        var adds = live.Effects(roller, EffectKind.AddHit, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).ToList();
        var extra = adds.Where(x => x.Effect.TargetQuantity is null or TargetQuantity.Hits).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        var wounds = adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Wounds).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        foreach (var x in adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Wounds)) live.Read(x.Source);
        var grants = live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .Select(x => (x.Effect.TargetEntry!, x.Effect.Amount)).ToList();
        return new DieEffects(extra, wounds, grants);
    }
```

- [ ] **Step 4: Read every die in the attack**

In `RollToHitStep.Run`, the inner loop reads all dice; hits and grants still come only from successes:

```csharp
                foreach (var die in ctx.Roll(plan, attacks).Dice)
                {
                    var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                    values["self_wounds"] = values.GetValueOrDefault("self_wounds") + effects.ExtraWounds;
                    if (!die.Success) continue;
                    values["extra_hits"] += effects.ExtraHits;
                    foreach (var (entry, amount) in effects.Grants) values[$"granted_{entry}_{amount}"] = values.GetValueOrDefault($"granted_{entry}_{amount}") + 1;
                }
```

and before returning, `values["wounds"] = values.GetValueOrDefault("self_wounds");` so the `self-wounds` step's `deal-wounds` reads its count off this frame (`WoundsToDeal` falls back to the parent's `wounds` when the parent has no defense roll).

In `RollToBlockStep.Run`, after each `ctx.Roll(plan, ...)`, count the defense dice's wounds against the plan's live set (`granted` for a granted group, `live` for the plain one):

```csharp
                var record = ctx.Roll(plan, group);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(granted, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
```

with `var extraWounds = 0;` declared beside `wounds`, the same for the plain group with `live`, and `["extra_wounds"] = extraWounds` added to the step's values.

In `WoundsToDeal`, add the extra wounds:

```csharp
        return defense.Count > 0 ? defense.Sum(Dice.RollOutcome.Failures) + parent.Value("extra_wounds") : parent.Value("wounds");
```

- [ ] **Step 5: The attacker's own wounds**

In `GfAttack`, add the step and append it to `AttackSequence`:

```csharp
    /// <summary>Wounds the attacking unit dealt itself (Hazardous: "this model's unit takes one wound on unmodified rolls of 1 to hit"), after
    /// its hits resolved: a deal-wounds with no attacker, at its own unit, reading `wounds` off this frame.</summary>
    public sealed class SelfWoundsStep : IStep
    {
        public string Name => "self-wounds";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Value("self_wounds") <= 0 || ctx.Frame.Context.ActorUnitId is not { } self) return new Complete([]);
            if (ctx.History.Children.Any(c => c.Finished || c.Abandoned)) return new Complete([]);
            return new Push(GfNames.DealWounds, new SequenceContext(null, [self], [], null));
        }
    }
```

```csharp
    public static Sequence AttackSequence() => new(GfNames.AttackSequence, [new DetermineAttacksStep(), new PickTargetModelStep(), new RollToHitStep(), new PushStep("resolve", GfNames.ResolveHits), new SelfWoundsStep()]);
```

In `GfCasting`, `SpellHits()` becomes `new(GfNames.SpellHitsSeq, [new HitStep(), new GfAttack.PushStep("resolve", GfNames.ResolveHits), new GfAttack.SelfWoundsStep()])`, and `HitStep`'s dice loop records `values["self_wounds"]` and `values["wounds"]` from `PerDie(...).ExtraWounds` exactly as roll-to-hit does (a spell's dice are all successes). The spell-hits frame's context names the caster's unit as actor, so *Power Surge*'s *"on rolls of 1"* wounds the caster's own unit.

- [ ] **Step 6: Validate the quantity**

In `ValidateEffect`:

```csharp
        if (f.Kind == EffectKind.AddHit && f.TargetQuantity is TargetQuantity.Wounds && !f.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            problems.Add(At("add_hit wounds is per die: it needs a roll_result condition"));
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. Every existing attack has `self_wounds` 0, so `self-wounds` completes at once and no log line or Ask is added; tests that count an attack frame's steps by name (`sim.Steps("...")`) are unaffected.

- [ ] **Step 8: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests
git commit -m "Count extra wounds per die: on a defense 1 for the defender, on a hit 1 for the attacker's own unit"
```
