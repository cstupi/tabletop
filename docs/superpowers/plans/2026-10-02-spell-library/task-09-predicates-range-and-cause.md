# Task 9: Two predicates, the charge's range, the morale test's cause

**Files:**
- Modify: `server/game/Effects/Conditions.cs` (`HoldsProperty`, `MostModels`)
- Modify: `server/game/Properties/PropertySetDocument.cs` (validation of the two predicates)
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs` (`ChargeTargetStep` records `charge_range`)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`AttackInteraction` reads it)
- Modify: `server/game/Rules/GrimdarkFuture/GfMorale.cs` (`TestStep`'s interaction carries the cause; `CauseOf`)
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (`ChargeRangeKey`)
- Create: `tests/Tabletop.GameServer.Tests/GrammarAdditionTests.cs`

**Interfaces:**
- Consumes: `ConditionKind.HoldsProperty`, `MostModels` (Task 3); `AttackKind.Spell` (Task 7); `Lookup.UnitHas`, `Lookup.ModelParameter`, `StatValue.Read`.
- Produces: `GfNames.ChargeRangeKey = "charge_range"`; `GfMorale.CauseOf(GameState state, Guid unit) → AttackKind?`.

Spec → *Grammar additions*: `holds_property` (*"If this model has Warbound"*; a suppression does not unhold), `most_models` (*"most models have Tough(3) or higher"*, *"Defense 2+ to Defense 3+"*), the charge measured at its start (*"when it shoots or charges enemies over 9" away"*), and a morale test carrying what caused it (*"+1 to morale test rolls triggered by shooting/melee"*).

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/GrammarAdditionTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GrammarAdditionTests
{
    static EffectDocument HitMod(int amount, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, when, []);
    static EntryDocument Rule(string name, params EffectDocument[] rows) => new(name, CatalogKind.Model, false, Aggregation.All, null, null, $"{name}.", null, rows);

    static readonly PropertySet Props = TestProperties.WithSpells(
        Rule("Base Rule"),
        Rule("Boosted", HitMod(1, new ConditionDocument(ConditionKind.HoldsProperty, EffectSubject.Self, Comparison.Eq, null, ["Base Rule"]))),
        Rule("Giant Slayer", HitMod(2, new ConditionDocument(ConditionKind.MostModels, EffectSubject.Opponent, Comparison.Gte, 3, ["Tough"]))),
        Rule("Armour Breaker", HitMod(2, new ConditionDocument(ConditionKind.MostModels, EffectSubject.Opponent, Comparison.Lte, 3, ["Defense"]))),
        Rule("Long Charger", HitMod(1, new ConditionDocument(ConditionKind.ActionTaken, null, Comparison.Eq, null, ["Charge"]), new ConditionDocument(ConditionKind.Range, null, Comparison.Gt, 9, []))),
        Rule("Steady", new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "morale", null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction,
            [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting", "melee"])], [])));

    static LiveSet Shot(GameState s, Guid a, Guid b) => LiveProperties.Settle(new Interaction(s, Props, Party.Whole(s, a), Party.Whole(s, b), AttackKind.Shooting, null, 12, []));
    static int HitBonus(LiveSet live) => live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Holds_property_reads_the_base_rule_on_the_holders_side()
    {
        var with = Unit("A", "4+", "4+", [], 2, [P("Boosted"), P("Base Rule")]); var without = Unit("C", "4+", "4+", [], 2, [P("Boosted")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([with, without], [b]);
        Assert.Equal(1, HitBonus(Shot(s, with.Id, b.Id)));
        Assert.Equal(0, HitBonus(Shot(s, without.Id, b.Id)));
    }

    [Fact]
    public void Most_models_compares_a_rules_parameter_or_a_stats_target_number_over_more_than_half()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Giant Slayer"), P("Armour Breaker")]);
        var big = Unit("Big", "4+", "3+", [], 3, [P("Tough", 3)]);
        var mixed = Unit("Mixed", "4+", "5+", [], 2, []);
        mixed = mixed with { Models = [mixed.Models[0] with { Properties = [P("Tough", 6)] }, mixed.Models[1]] };   // one of two: not most
        var s = State([a], [big, mixed]);
        Assert.Equal(4, HitBonus(Shot(s, a.Id, big.Id)));
        Assert.Equal(0, HitBonus(Shot(s, a.Id, mixed.Id)));
    }

    [Fact]
    public void A_charge_measures_its_range_from_where_it_started()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Long Charger")], Melee("Claws", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 12), GrimdarkFutureRules.Instance, Props, new ScriptedDice(6, 6, 6, 6, 6, 6));
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Equal(1074, sim.Steps("charge-target").Last().Values![GfNames.ChargeRangeKey]);          // 12" between centres, less two 0.63" radii; the range predicate floors 10.74 to 10, over 9
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = 10.74 } }))]));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "move" }) sim.Answer(Samples.Team2, new Passed());
        Assert.Contains(sim.Steps("roll-to-hit").First().Rolls.SelectMany(r => r.Modifiers), x => x.Source == "Long Charger");
    }

    [Fact]
    public void A_morale_test_knows_whether_shooting_melee_or_a_spell_caused_it()
    {
        var u = Unit("U", "4+", "4+", [], 1, [P("Steady")]);
        foreach (var (action, expected) in new[] { ("melee", true), ("spell", false) })
        {
            var sim = new Sim(State([u], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new ScriptedDice(3));
            sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], action));
            var roll = sim.Steps("test").Single().Rolls.Single();
            Assert.Equal(expected, roll.Modifiers.Any(x => x.Source == "Steady"));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~GrammarAdditionTests`
Expected: FAIL: both predicates answer false, `charge_range` is missing, `Steady` applies to neither test.

- [ ] **Step 3: The two predicates**

In `server/game/Effects/Conditions.cs`, add the two cases to `Holds`:

```csharp
            // The side holds the entry under its own aggregation, a granted instance included; a suppression does not unhold (spell library spec).
            case ConditionKind.HoldsProperty: return c.Values.Count == 1 && Lookup.UnitHas(ix.State, ix.Properties, ix.Of(side).UnitId, c.Values[0]);
            case ConditionKind.MostModels: return c.Values.Count == 1 && MostModels(ix, side, c);
```

and the helper:

```csharp
    /// <summary>"Most models have …": more than half the side's living models meet the comparison. A model stat compares its target number, read off
    /// the slot or the unit; a rule compares its parameter, and a model without the rule fails.</summary>
    static bool MostModels(Interaction ix, Side side, EffectCondition c)
    {
        var state = ix.State; var unit = state.Projection.Units[ix.Of(side).UnitId]; var name = c.Values[0];
        var models = Lookup.LivingModels(state, unit.Id);
        if (models.Count == 0) return false;
        var isStat = ix.Properties.Stat(StatLevel.Model, name) is not null || ix.Properties.Stat(StatLevel.Unit, name) is not null;
        int? Value(Guid m)
        {
            if (!isStat) return Lookup.ModelParameter(state, m, name);
            var text = state.Projection.Models[m].Slot.Stats.TryGetValue(name, out var own) ? own : unit.Definition.Stats.GetValueOrDefault(name);
            return text is null ? null : Tabletop.Content.StatValue.Read(Tabletop.Content.StatKind.TargetNumber, text).TargetNumber;
        }
        return models.Count(m => Value(m) is { } v && c.Compare(v)) * 2 > models.Count;
    }
```

In `PropertiesJson.ValidateEffect`, add:

```csharp
        foreach (var c in f.Conditions.Where(c => c.Kind is ConditionKind.HoldsProperty or ConditionKind.MostModels))
        {
            if (c.Subject is null) problems.Add(At($"{c.Kind} needs a subject"));
            if (c.Values.Count != 1) problems.Add(At($"{c.Kind} names exactly one value"));
            else if (c.Kind == ConditionKind.HoldsProperty && !names.Contains(c.Values[0])) problems.Add(At($"holds_property names no entry '{c.Values[0]}'"));
        }
```

- [ ] **Step 4: The charge's range**

In `GfNames`: `public const string ChargeRangeKey = "charge_range";   // hundredths of an inch, on the charge frame's charge-target step`.

In `GfMelee.ChargeTargetStep.Run`, the answered branch records the distance before the move:

```csharp
            if (ctx.History.LastClosed() is { } answered)
            {
                var picked = GfAnswers.Pieces(answered.Single, answered.Ask);
                if (picked.Count == 0) return new Finish([]);   // an answer that names no offered unit is no target
                var range = Battlefield.UnitDistance(ctx.State, Charger(ctx), picked[0]) ?? 0;
                return new Complete([], new Dictionary<string, int> { [GfNames.ChargeRangeKey] = (int)Math.Round(range * 100) });
            }
```

In `GfAttack.AttackInteraction`, the range of an interaction under a charge frame is that recorded distance:

```csharp
        var range = ChargeRange(ctx.Frame) ?? Battlefield.UnitDistance(state, a, target);
        return new Interaction(state, ctx.Properties, Party.Of(state, a, models, weapons), Party.Whole(state, target), kind, action, range, Battlefield.TerrainUnder(state, target));
```

```csharp
    /// <summary>"Charges enemies over 9" away", "charged from over 9" away": in the melee of a Charge, the distance the charge-target step recorded
    /// before the move (spell library → Grammar additions). Null outside a charge.</summary>
    static double? ChargeRange(FrameView frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.ChargeSeq && f.Values.TryGetValue(GfNames.ChargeRangeKey, out var hundredths)) return hundredths / 100.0;
        return null;
    }
```

- [ ] **Step 5: The morale test's cause**

In `GfMorale`, add:

```csharp
    /// <summary>What caused a general morale test: the kind of the last attack at the unit in the effective log, a spell's hits being Spell.
    /// Null when nothing attacked it.</summary>
    public static AttackKind? CauseOf(GameState state, Guid unit)
    {
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log).Reverse())
            if (e.Payload is SequenceStarted s && s.Context.TargetUnitIds.Contains(unit))
            {
                if (s.Sequence == GfNames.SpellHitsSeq) return AttackKind.Spell;
                if (s.Sequence == GfNames.AttackSequence && s.Context.WeaponIds.Count > 0) return GfAttack.KindOf(state, s.Context.WeaponIds);
            }
        return null;
    }
```

and in `TestStep.Run`, the interaction carries it:

```csharp
            var cause = ctx.Frame.Context.Action switch { "melee" => AttackKind.Melee, "spell" => AttackKind.Spell, _ => CauseOf(ctx.State, unit) };
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, cause, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit)));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/GrammarAdditionTests.cs
git commit -m "Add holds_property and most_models, measure a charge from its start, give a morale test its cause"
```
