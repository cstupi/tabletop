# Task 6: A die at the start of the attack: *Unpredictable*

**Files:**
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveProperty.Chosen`; a recorded face chooses a roll row's children)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`AttackStartStep`, `RollAttackStart`, faces in `AttackInteraction`)
- Modify: `server/game/Rules/GrimdarkFuture/GfShooting.cs` (`attack-start` first), `GfMelee.cs` (`melee-start` rolls for both sides)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (two entries' rows)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: `Interaction.Faces`, `Interaction.Point`, `LiveSet.AtPoint` (Task 1); the validator's own-roll admission (Task 1).
- Produces: `LiveProperty(..., Effect? GrantRow = null, IReadOnlyList<Effect>? Chosen = null)`; `GfAttack.AttackStartStep` (step `attack-start`); `GfAttack.RollAttackStart(StepContext, Guid unit, AttackKind kind, Dictionary<string, int> values)`; step values `face_<unit:N>_<entry>`; `shooting` = `[attack-start, declare-targets, fire-groups, after-shooting]`.

Spec → *A die at the start of the attack*. *"When in melee, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead."* The die is the entry's own `RollRecord`, rolled once per shooting and once per side per melee; its face picks the children for every interaction of that attack.

- [ ] **Step 1: Write the failing tests**

Add to `ArmyRuleHookTests`:

```csharp
    [Theory]
    [InlineData(3, true, false)]
    [InlineData(4, false, true)]
    public void Unpredictable_shooter_rolls_once_and_its_face_picks_ap_or_plus_one_to_hit_for_every_group(int face, bool ap, bool toHit)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Unpredictable Shooter")], Gun("Rifle", "24\"", 1), Gun("Pistol", "12\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -4, x2: 4), GrimdarkFutureRules.Instance, Core, new ScriptedDice([face, .. Enumerable.Repeat(5, 10)]));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        var start = sim.Steps("attack-start").Single();
        Assert.Equal(("Unpredictable Shooter", face), (start.Rolls.Single().Roll, start.Values![$"face_{a.Id:N}_Unpredictable Shooter"]));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id, ["Pistol"] = b.Id }));
        Assert.All(sim.Steps("roll-to-hit"), s => Assert.Equal(toHit, s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == "Unpredictable Shooter")));
        Assert.All(sim.Steps("roll-to-block"), s => Assert.Equal(ap, s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == "AP")));
        Assert.Single(sim.Steps("attack-start"));
    }

    [Fact]
    public void Unpredictable_fighter_rolls_once_for_each_side_of_a_melee_that_holds_it()
    {
        var c = Unit("C", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Claws", 1)); var d = Unit("D", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([c], [d], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([2, 5, .. Enumerable.Repeat(1, 20)]));
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        var start = sim.Steps("melee-start").Single();
        Assert.Equal((2, 5), (start.Values![$"face_{c.Id:N}_Unpredictable Fighter"], start.Values[$"face_{d.Id:N}_Unpredictable Fighter"]));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~Unpredictable`
Expected: FAIL: no `attack-start` step; `melee-start` records no face.

- [ ] **Step 3: The rows**

In the part file, replace each of these entries' `"effects": []`:

```json
"Unpredictable Fighter": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 1, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["attack-start"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}], "children": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "lte", "number": 3, "values": ["Unpredictable Fighter", "raw"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "gte", "number": 4, "values": ["Unpredictable Fighter", "raw"]}]}]}]
"Unpredictable Shooter": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 1, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["attack-start"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}], "children": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "lte", "number": 3, "values": ["Unpredictable Shooter", "raw"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "gte", "number": 4, "values": ["Unpredictable Shooter", "raw"]}]}]}]
```

- [ ] **Step 4: A face chooses a roll row's children**

In `LiveProperties.cs`, extend `LiveProperty`:

```csharp
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null, IReadOnlyList<Effect>? Chosen = null)
{
    public IReadOnlyList<Effect> Effects => Chosen ?? (GrantRow is { } row ? [row] : Entry.Effects);
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}
```

Add to `LiveProperties`:

```csharp
    /// <summary>
    /// A property whose roll row is asked at the attack-start point and whose face this attack recorded (Interaction.Faces) exposes, instead of its
    /// rows, that roll's children whose roll_result on its own roll holds against the face, with that condition dropped (army-rule hooks → A die
    /// at the start of the attack). Any other property is returned as it is.
    /// </summary>
    static LiveProperty WithFace(Interaction ix, LiveProperty p)
    {
        if (ix.Faces is null || !ix.Faces.TryGetValue(p.Holder, out var faces) || !faces.TryGetValue(p.Entry.Name, out var raw)) return p;
        bool Own(EffectCondition c) => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == p.Entry.Name;
        var rolls = p.Effects.Where(e => e.Kind == EffectKind.Roll && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains("attack-start"))).ToList();
        if (rolls.Count == 0) return p;
        return p with { Chosen = rolls.SelectMany(r => r.Children).Where(ch => ch.Conditions.Where(Own).All(c => c.Compare(raw)))
            .Select(ch => ch with { Conditions = ch.Conditions.Where(c => !Own(c)).ToList() }).ToList() };
    }
```

In `Gather`, return `result.GroupBy(...).Select(g => WithFace(ix, g.First()))`. In `Settle`'s `GrantProperty` case, add the granted instance as `WithFace(ix, new LiveProperty(side, granted, effect.Amount, source.ModelId, source.WeaponId, source.GrantId))`, so a granted *Unpredictable* reads the face too.

- [ ] **Step 5: Roll at the start, read the faces in every interaction**

In `GfAttack`:

```csharp
    /// <summary>Each live roll row of the unit asked at attack-start (its other conditions holding for this attack kind) rolls once, as a RollRecord named
    /// for its entry, and its face is recorded as face_&lt;unit&gt;_&lt;entry&gt;.</summary>
    public static void RollAttackStart(StepContext ctx, Guid unit, AttackKind kind, Dictionary<string, int> values)
    {
        if (Lookup.LivingModels(ctx.State, unit).Count == 0) return;
        var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, kind, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit), GfNames.AttackStart));
        foreach (var (source, row) in live.AtPoint(Side.Attacker, GfNames.AttackStart).Where(x => x.Effect.Kind == EffectKind.Roll).DistinctBy(x => x.Source.Entry.Name))
        {
            var record = ctx.Roll(new Dice.RollPlan(source.Entry.Name, row.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), 1);
            values[$"face_{unit:N}_{source.Entry.Name}"] = record.Dice[0].Raw;
        }
    }

    public sealed class AttackStartStep : IStep
    {
        public string Name => "attack-start";
        public StepOutcome Run(StepContext ctx)
        {
            var values = new Dictionary<string, int>();
            RollAttackStart(ctx, ctx.Frame.Context.ActorUnitId!.Value, AttackKind.Shooting, values);
            return new Complete([], values);
        }
    }

    /// <summary>The faces the nearest shooting or melee frame recorded, by side of this interaction.</summary>
    static IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? FacesFor(FrameView frame, Guid attacker, Guid target)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence is GfNames.Shooting or GfNames.Melee)
            {
                IReadOnlyDictionary<string, int> Of(Guid unit) => f.Values.Where(kv => kv.Key.StartsWith($"face_{unit:N}_")).ToDictionary(kv => kv.Key[$"face_{unit:N}_".Length..], kv => kv.Value);
                return new Dictionary<Side, IReadOnlyDictionary<string, int>> { [Side.Attacker] = Of(attacker), [Side.Defender] = Of(target) };
            }
        return null;
    }
```

In `AttackInteraction`, pass `Faces: FacesFor(ctx.Frame, a, target)` to the attacker-and-target `Interaction`.

`GfShooting.Shooting()` becomes `[new GfAttack.AttackStartStep(), new DeclareTargetsStep(), new FireGroupsStep(), new GfMoves.PointWindowStep(GfNames.AfterShooting, "after-shooting")]`. `GfMelee.MeleeStartStep.Run` rolls for both sides into its values:

```csharp
        public StepOutcome Run(StepContext ctx)
        {
            var values = new Dictionary<string, int> { ["charger_before"] = ctx.Projection.Units[Charger(ctx)].WoundsThisActivation, ["defender_before"] = ctx.Projection.Units[Defender(ctx)].WoundsThisActivation };
            GfAttack.RollAttackStart(ctx, Charger(ctx), AttackKind.Melee, values);
            GfAttack.RollAttackStart(ctx, Defender(ctx), AttackKind.Melee, values);
            return new Complete([], values);
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. The simulator's replay invariant re-derives every frame from the log, so a face is read back from its step's values on replay.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Roll Unpredictable once at the start of an attack and let its face pick AP or +1 to hit"
```
