# Task 7: Extra attacks: *Primal* and *Clan Warrior*

**Files:**
- Modify: `server/game/Effects/RollEffects.cs` (`DieEffects.ExtraAttacks`)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`RollToHitStep` rolls the added attacks)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (four entries' rows)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: `TargetQuantity.Attacks` (Task 1); `DieEffects(int ExtraHits, int ExtraWounds, IReadOnlyList<(string, int?)> Grants)` (library Task 8).
- Produces: `DieEffects(..., int ExtraAttacks = 0)`.

Spec → *Extra attacks*. *"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn't apply to newly generated attacks."* An added attack is one more die of the same pool, rolled in the same step as one more `hit` `RollRecord`; its die is read for every per-die effect except added attacks. The Boosts add the 5 (*"successful unmodified hit results of 5-6"*); per-die effects are read on successful dice only.

- [ ] **Step 1: Write the failing tests**

Add to `ArmyRuleHookTests`:

```csharp
    [Fact]
    public void Each_six_to_hit_adds_an_attack_whose_own_six_adds_none()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Primal")], Melee("Claws", 2)); var b = Unit("B", "4+", "2+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([6, 3, 6, .. Enumerable.Repeat(6, 10)]));
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [b.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        var hit = sim.Steps("roll-to-hit").First();
        Assert.Equal([2, 1], hit.Rolls.Select(r => r.Dice.Count));                                    // two attacks, one 6, one added attack (whose 6 adds nothing)
    }

    [Fact]
    public void The_boost_adds_an_attack_on_a_five_too()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Primal"), P("Primal Boost")], Melee("Claws", 2)); var b = Unit("B", "4+", "2+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([5, 6, 2, 2, .. Enumerable.Repeat(6, 10)]));
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [b.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Assert.Equal([2, 2], sim.Steps("roll-to-hit").First().Rolls.Select(r => r.Dice.Count));
    }
```

The defender's Defense 2+ blocks the hits so the melee ends quickly; only the hit roll is under test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Each_six_to_hit|FullyQualifiedName~The_boost_adds"`
Expected: FAIL: one hit roll only.

- [ ] **Step 3: The rows**

In the part file, replace each of these entries' `"effects": []`:

```json
"Primal": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]
"Primal Boost": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Primal"]}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}]
"Clan Warrior": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]
"Clan Warrior Boost": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Clan Warrior"]}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}]
```

- [ ] **Step 4: Count added attacks per die**

In `RollEffects.cs`, `DieEffects` gains a trailing member, and `PerDie` fills it:

```csharp
public sealed record DieEffects(int ExtraHits, int ExtraWounds, IReadOnlyList<(string Entry, int? Parameter)> Grants, int ExtraAttacks = 0);
```

```csharp
        var attacks = adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Attacks).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        return new DieEffects(extra, wounds, grants, attacks);
```

`extra` already sums only `TargetQuantity is null or Hits`, so an added attack is never also an extra hit.

- [ ] **Step 5: Roll them in the same step**

In `RollToHitStep.Run`, after each pool's loop over its dice, roll the pool's added attacks with the same plan; their dice are read for hits, wounds and grants, never for further attacks:

```csharp
                var added = 0;
                foreach (var die in ctx.Roll(plan, attacks).Dice)
                {
                    var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                    values["self_wounds"] = values.GetValueOrDefault("self_wounds") + effects.ExtraWounds;
                    if (!die.Success) continue;
                    added += effects.ExtraAttacks;
                    values["extra_hits"] += effects.ExtraHits;
                    foreach (var (entry, amount) in effects.Grants) values[$"granted_{entry}_{amount}"] = values.GetValueOrDefault($"granted_{entry}_{amount}") + 1;
                }
                // "This rule doesn't apply to newly generated attacks": the added dice add no attacks of their own.
                if (added > 0)
                    foreach (var die in ctx.Roll(plan, added).Dice)
                    {
                        var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                        values["self_wounds"] = values.GetValueOrDefault("self_wounds") + effects.ExtraWounds;
                        if (!die.Success) continue;
                        values["extra_hits"] += effects.ExtraHits;
                        foreach (var (entry, amount) in effects.Grants) values[$"granted_{entry}_{amount}"] = values.GetValueOrDefault($"granted_{entry}_{amount}") + 1;
                    }
```

`HitsFrom` sums the successes of every `hit` `RollRecord` on the frame, so the added dice's hits count with no further change.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Add an attack for each 6 to hit under Primal and Clan Warrior, a 5 too under their Boosts"
```
