# Task 1: The typed `Properties` set

**Files:**
- Create: `server/game/Properties/PropertySet.cs`
- Test: `tests/Tabletop.GameServer.Tests/PropertySetTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.StatKind`.
- Produces (namespace `Tabletop.GameServer.Properties`): `enum StatLevel { Unit, Model, Weapon }`; `StatDefinition(StatLevel Level, string Name, StatKind Kind)`; `StatRef(StatLevel Level, string Name)`; `ActionDefinition(string Name, double Distance)`; `RollDefinition(string Name)`; `enum CatalogKind { Unit, Model, Weapon, Spell }`; `enum Aggregation { All, Any, Majority, None }`; `enum TargetSide { Friendly, Enemy }`; `enum TargetGrain { Unit, Model }`; `SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range)`; `CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText, SpellTargeting? Targeting, IRL<Effect> Effects)`; `enum EffectSubject { Self, Opponent }`; `enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers }`; `enum TargetQuantity { Hits, Wounds }`; `enum TargetResult { Wound, MoraleTest }`; `enum TargetModifier { Cover, ToHitPenalties, RangePenalties }`; `enum CapKind { ModelsInTargetUnit }`; `enum EffectDuration { Interaction, Once }`; `Effect(Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction, TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration, IRL<EffectCondition> Conditions, IRL<Effect> Children)` with `int EffectiveAmount(int? parameter)`; `enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount }`; `enum Comparison { Eq, Neq, Gte, Lte, Gt, Lt }`; `EffectCondition(Guid Id, ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IRL<string> Values)` with `bool Compare(int actual)`; `sealed class PropertySet(string ContentHash, string Name, string GameRules, IRL<StatDefinition> Stats, IRL<ActionDefinition> Actions, IRL<RollDefinition> Rolls, IRL<CatalogEntry> Entries)` with `IRD<string, CatalogEntry> ByName`, `CatalogEntry? Entry(string)`, `StatDefinition? Stat(StatLevel, string)`, `ActionDefinition Action(string)` (throws `KeyNotFoundException`), `bool HasRoll(string)`, `IEnumerable<Effect> AllEffects()` (children included, depth first).
- Every column of the eight tables in `schema.md` has a member here; every closed set is an enum whose wire spelling is the schema's check-constraint value under `ContentJson`'s snake_case policy (`modify_roll`, `models_in_target_unit`, `target_number`).

Spec: `schema.md` → `Properties` is eight tables (every column), One `effect` table not twelve (`amount` signed, `amount_from_parameter`, three one-kind columns, `parent_effect_id` nesting, `duration`), A condition is a row and its values are rows (`neq`; a set of values); effect grammar → An effect, Conditions, The twelve effect kinds, `Roll` nests; identity and enumerations → The closed sets; spell effects → Target selection, Faction rules are catalog entries.

- [ ] **Step 1: Write the failing test**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetTests
{
    public static Effect ModifyRoll(string roll, int amount, bool fromParameter = false, EffectSubject subject = EffectSubject.Opponent, int ordinal = 10, params EffectCondition[] conditions) =>
        new(Guid.CreateVersion7(), subject, ordinal, EffectKind.ModifyRoll, roll, null, null, null, null, null, null, amount, fromParameter, null, null, null, EffectDuration.Interaction, conditions, []);

    public static PropertySet Tiny() => new("sha256:tiny", "Tiny", "grimdark-future",
        [new StatDefinition(StatLevel.Unit, "Quality", StatKind.TargetNumber), new StatDefinition(StatLevel.Weapon, "Range", StatKind.Distance)],
        [new ActionDefinition("Hold", 0), new ActionDefinition("Advance", 6)],
        [new RollDefinition("hit"), new RollDefinition("defense")],
        [new CatalogEntry("AP", CatalogKind.Weapon, true, null, null, null, "Targets get -X to Defense rolls.", null, [ModifyRoll("defense", -1, fromParameter: true)])]);

    [Fact]
    public void Lookups_answer_by_name_and_level()
    {
        var set = Tiny();
        Assert.Equal(6, set.Action("Advance").Distance);
        Assert.Throws<KeyNotFoundException>(() => set.Action("Rush"));
        Assert.Equal(StatKind.Distance, set.Stat(StatLevel.Weapon, "Range")!.Kind);
        Assert.Null(set.Stat(StatLevel.Unit, "Range"));
        Assert.True(set.HasRoll("hit")); Assert.False(set.HasRoll("save"));
        Assert.Equal(-3, set.Entry("AP")!.Effects[0].EffectiveAmount(3));
        Assert.Equal(-1, set.Entry("AP")!.Effects[0] with { AmountFromParameter = false } is var e ? e.EffectiveAmount(3) : 0);
    }

    [Fact]
    public void Comparison_covers_the_six_operators_and_neq_tests_absence()
    {
        EffectCondition C(Comparison c) => new(Guid.CreateVersion7(), ConditionKind.Range, null, c, 9, []);
        Assert.True(C(Comparison.Gt).Compare(10)); Assert.False(C(Comparison.Gt).Compare(9));
        Assert.True(C(Comparison.Gte).Compare(9)); Assert.True(C(Comparison.Lt).Compare(8)); Assert.True(C(Comparison.Lte).Compare(9));
        Assert.True(C(Comparison.Eq).Compare(9)); Assert.True(C(Comparison.Neq).Compare(0));
    }

    [Fact]
    public void Enums_spell_as_the_schema_does()
    {
        Assert.Equal("\"modify_roll\"", ContentJson.Serialize(EffectKind.ModifyRoll));
        Assert.Equal("\"models_in_target_unit\"", ContentJson.Serialize(CapKind.ModelsInTargetUnit));
        Assert.Equal("\"to_hit_penalties\"", ContentJson.Serialize(TargetModifier.ToHitPenalties));
        Assert.Equal(ConditionKind.PositionTerrain, ContentJson.Deserialize<ConditionKind>("\"position_terrain\""));
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PropertySetTests`; expect compile errors.**

- [ ] **Step 3: Write `PropertySet.cs`**

```csharp
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

public enum StatLevel { Unit, Model, Weapon }
public sealed record StatDefinition(StatLevel Level, string Name, StatKind Kind);
/// <summary>stat_definition's key is (level, name), so a reference to a stat carries both.</summary>
public sealed record StatRef(StatLevel Level, string Name);
/// <summary>An action row: a name and a distance, because Fast and Slow need something to point at (schema.md).</summary>
public sealed record ActionDefinition(string Name, double Distance);
/// <summary>A roll row: a name and nothing else. Convention, threshold and cap are GameRules'.</summary>
public sealed record RollDefinition(string Name);

public enum CatalogKind { Unit, Model, Weapon, Spell }
public enum Aggregation { All, Any, Majority, None }
public enum TargetSide { Friendly, Enemy }
public enum TargetGrain { Unit, Model }
public sealed record SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range);

/// <summary>One named rule. Effects may be empty: then it is a name GameRules checks for at a hook it owns.</summary>
public sealed record CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction,
    string RuleText, SpellTargeting? Targeting, IReadOnlyList<Effect> Effects);

public enum EffectSubject { Self, Opponent }
public enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers }
public enum TargetQuantity { Hits, Wounds }
public enum TargetResult { Wound, MoraleTest }
public enum TargetModifier { Cover, ToHitPenalties, RangePenalties }
public enum CapKind { ModelsInTargetUnit }
public enum EffectDuration { Interaction, Once }

/// <summary>One effect row with its conditions and nested children (Roll's per-success effect, a hit's granted rule).</summary>
public sealed record Effect(
    Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind,
    string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier,
    int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration,
    IReadOnlyList<EffectCondition> Conditions, IReadOnlyList<Effect> Children)
{
    /// <summary>The signed amount: when it comes from the parameter, Amount is the sign (schema.md → amount is signed).</summary>
    public int EffectiveAmount(int? parameter) => AmountFromParameter ? Math.Sign(Amount ?? 1) * (parameter ?? 0) : Amount ?? 1;
}

public enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount }
public enum Comparison { Eq, Neq, Gte, Lte, Gt, Lt }

/// <summary>A predicate. Relational kinds take no subject; Values is a set (Rush/Charge is membership, not or).</summary>
public sealed record EffectCondition(Guid Id, ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values)
{
    public bool Compare(int actual) => Comparison switch
    {
        Comparison.Eq => actual == Number, Comparison.Neq => actual != Number, Comparison.Gte => actual >= Number,
        Comparison.Lte => actual <= Number, Comparison.Gt => actual > Number, Comparison.Lt => actual < Number, _ => false,
    };
}

/// <summary>The pinned data half of a ruleset, in memory. Addressed by its content hash; a game pins one.</summary>
public sealed class PropertySet(string contentHash, string name, string gameRules, IReadOnlyList<StatDefinition> stats,
    IReadOnlyList<ActionDefinition> actions, IReadOnlyList<RollDefinition> rolls, IReadOnlyList<CatalogEntry> entries)
{
    public string ContentHash { get; } = contentHash;
    public string Name { get; } = name;
    public string GameRules { get; } = gameRules;
    public IReadOnlyList<StatDefinition> Stats { get; } = stats;
    public IReadOnlyList<ActionDefinition> Actions { get; } = actions;
    public IReadOnlyList<RollDefinition> Rolls { get; } = rolls;
    public IReadOnlyList<CatalogEntry> Entries { get; } = entries;
    public IReadOnlyDictionary<string, CatalogEntry> ByName { get; } = entries.ToDictionary(e => e.Name);

    public CatalogEntry? Entry(string name) => ByName.GetValueOrDefault(name);
    public StatDefinition? Stat(StatLevel level, string name) => Stats.FirstOrDefault(s => s.Level == level && s.Name == name);
    public ActionDefinition Action(string name) => Actions.FirstOrDefault(a => a.Name == name) ?? throw new KeyNotFoundException($"No action '{name}' in {ContentHash}.");
    public bool HasRoll(string name) => Rolls.Any(r => r.Name == name);
    public IEnumerable<Effect> AllEffects() => Entries.SelectMany(e => e.Effects).SelectMany(Flatten);
    static IEnumerable<Effect> Flatten(Effect e) => new[] { e }.Concat(e.Children.SelectMany(Flatten));
}
```

- [ ] **Step 4: Run; expect 3 passed. Commit: `Define the typed Properties set`.**
