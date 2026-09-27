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
