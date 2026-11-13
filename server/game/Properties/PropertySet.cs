using System.Text.Json.Serialization;
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

public enum StatLevel { Unit, Model, Weapon }
public sealed record StatDefinition(StatLevel Level, string Name, StatKind Kind);
/// <summary>stat_definition's key is (level, name), so a reference to a stat carries both.</summary>
public sealed record StatRef(StatLevel Level, string Name);
/// <summary>An action row: a name and a distance, because an effect needs something to point at (schema.md).</summary>
public sealed record ActionDefinition(string Name, double Distance);
/// <summary>A roll row: a name and nothing else. Convention, threshold and cap are GameRules'.</summary>
public sealed record RollDefinition(string Name);

public enum CatalogKind { Unit, Model, Weapon, Spell }
public enum Aggregation { All, Any, Majority, None }
public enum TargetSide { Friendly, Enemy }
public enum TargetGrain { Unit, Model }
/// <summary>Who a spell or a pick entry may target. LineOfSight null: the target must be in sight (3.5.1 p.13, and a Mark's or Debuff's "in line of
/// sight"); false: range alone (a Buff's "pick one friendly unit within 12""); true, kept on a spell only: its own "in line of sight", which 3.6.0
/// p.13's core rule no longer asks of a spell's null (GfCasting.Targets). Requires: an entry the target must hold, the unit (unit grain)
/// or the model (model grain): a pick's "pick one friendly model … with X". OncePerRound: a unit a use of this entry moved since the round
/// started is no target ("may only be moved once per round in this way"). CountFromParameter: the pick takes 1 to X units, X the entry's
/// parameter ("place X tokens on one enemy unit …, or one token on X enemy units"). Both are written only when true.</summary>
public sealed record SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range, bool? LineOfSight = null, string? Requires = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OncePerRound = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CountFromParameter = false);

/// <summary>One named rule. Effects may be empty: then it is a name GameRules checks for at a hook it owns. Played is false only on a generated
/// text-only entry (army-book rules spec → Played flag): the unit card shows its text, and LiveProperties.Gather skips it.</summary>
public sealed record CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction,
    string RuleText, SpellTargeting? Targeting, IReadOnlyList<Effect> Effects, bool Played = true);

public enum EffectSubject { Self, Opponent }
public enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers,
    CountAsTerrain, TakeTest, SetCondition, Move, Place, RemoveWounds, CountWounds, RemoveSpellTokens }
/// <summary>p.11's terrain types a holder can count as being in (count_as_terrain).</summary>
public enum TargetTerrain { Difficult, Dangerous }
public enum TargetQuantity { Hits, Wounds, Attacks }
public enum TargetResult { Wound, MoraleTest }
/// <summary>ForeignToHitPenalties: the negative hit modifiers that are not from the roller's own rules (3.6.0 p.15 Unstoppable); ToHitPenalties: all of them.
/// PositiveDefense: every positive modifier of the defense roll.</summary>
public enum TargetModifier { Cover, ToHitPenalties, RangePenalties, ForeignToHitPenalties, PositiveDefense }
public enum CapKind { ModelsInTargetUnit }
/// <summary>Interaction: the row acts whenever it holds. Once: a grant spent the next time it applies (spell library). Marker and SpentMarker: one grant per
/// marker a rule places on a unit (army-book rules spec, batch 6), stacking; a Marker stays for the game, a SpentMarker is a token the attacking team
/// may remove, X the count removed ("may remove tokens … where X is the number of removed tokens", GfChoice.SpotterTokensStep). A OneMarker row is one token a rule places: its grants stack, one acts on an attack and only that one
/// is spent ("may remove one token … to get"). A carried entry's marker rows act only through those grants.</summary>
public enum EffectDuration { Interaction, Once, Marker, SpentMarker, OneMarker }

/// <summary>One effect row with its conditions and nested children (a roll's per-success effect, a hit's granted rule). Optional: an add_hit's extra
/// attacks the roller's team may decline ("may"), written only when true.</summary>
public sealed record Effect(
    Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind,
    string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier,
    int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration,
    IReadOnlyList<EffectCondition> Conditions, IReadOnlyList<Effect> Children,
    TargetTerrain? TargetTerrain = null, string? TargetCondition = null, string? AmountDice = null, int? Floor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Optional = false)
{
    /// <summary>The signed amount: when it comes from the parameter, Amount is the sign (schema.md → amount is signed).</summary>
    public int EffectiveAmount(int? parameter) => AmountFromParameter ? Math.Sign(Amount ?? 1) * (parameter ?? 0) : Amount ?? 1;
}

public enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount, HoldsProperty, MostModels, AtPoint, WeaponRange, RemainingSize }
public enum Comparison { Eq, Neq, Gte, Lte, Gt, Lt }

/// <summary>A predicate. Relational kinds take no subject; Values is a set (membership, not or).</summary>
public sealed record EffectCondition(Guid Id, ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values)
{
    public bool Compare(int actual) => Comparison switch
    {
        Comparison.Eq => actual == Number, Comparison.Neq => actual != Number, Comparison.Gte => actual >= Number,
        Comparison.Lte => actual <= Number, Comparison.Gt => actual > Number, Comparison.Lt => actual < Number, _ => false,
    };

    public bool Compare(double actual) => Number is { } n && Comparison switch
    {
        Comparison.Eq => actual == n, Comparison.Neq => actual != n, Comparison.Gte => actual >= n,
        Comparison.Lte => actual <= n, Comparison.Gt => actual > n, Comparison.Lt => actual < n, _ => false,
    };
}

/// <summary>The data half of a ruleset, in memory. Addressed by its content hash; a game that has not ended runs its game rules' current set
/// (games-follow-the-current-rules spec).</summary>
public sealed class PropertySet(string contentHash, string name, string gameRules, IReadOnlyList<StatDefinition> stats,
    IReadOnlyList<ActionDefinition> actions, IReadOnlyList<RollDefinition> rolls, IReadOnlyList<CatalogEntry> entries, IReadOnlyList<BookRecord>? books = null)
{
    readonly IReadOnlyList<BookRecord> _books = books ?? [];
    IReadOnlyDictionary<Guid, Effect> _aliases = new Dictionary<Guid, Effect>();

    public string ContentHash { get; } = contentHash;
    public string Name { get; } = name;
    public string GameRules { get; } = gameRules;
    public IReadOnlyList<StatDefinition> Stats { get; } = stats;
    public IReadOnlyList<ActionDefinition> Actions { get; } = actions;
    public IReadOnlyList<RollDefinition> Rolls { get; } = rolls;
    public IReadOnlyList<CatalogEntry> Entries { get; } = entries;
    public IReadOnlyDictionary<string, CatalogEntry> ByName { get; } = entries.ToDictionary(e => e.Name);

    public IReadOnlyDictionary<string, BookRecord> Books { get; } = (books ?? []).ToDictionary(b => b.Uid);

    /// <summary>p.13: the spells a caster of this book may try, in the book's order, leaving out a text-only spell (played false). Empty for no book or a book the set does not list.</summary>
    public IReadOnlyList<CatalogEntry> SpellsOf(string? bookUid) =>
        bookUid is not null && Books.TryGetValue(bookUid, out var b) ? b.Spells.Select(Entry).OfType<CatalogEntry>().Where(e => e.Kind == CatalogKind.Spell && e.Played).ToList() : [];

    public CatalogEntry? Entry(string name) => ByName.GetValueOrDefault(name);
    public StatDefinition? Stat(StatLevel level, string name) => Stats.FirstOrDefault(s => s.Level == level && s.Name == name);
    public ActionDefinition Action(string name) => Actions.FirstOrDefault(a => a.Name == name) ?? throw new KeyNotFoundException($"No action '{name}' in {ContentHash}.");
    public bool HasRoll(string name) => Rolls.Any(r => r.Name == name);
    public IEnumerable<Effect> AllEffects() => Entries.SelectMany(e => e.Effects).SelectMany(Flatten);
    /// <summary>An effect row by id, children included: what a grant names. An id of another set of the same game rules resolves through
    /// its alias (EffectAliases), so a grant minted before the game moved to this set still acts.</summary>
    public Effect? EffectById(Guid id) => _byId.Value.GetValueOrDefault(id) ?? _aliases.GetValueOrDefault(id);

    /// <summary>This set with aliases for other sets' effect rows (EffectAliases.Match).</summary>
    public PropertySet WithAliases(IEnumerable<ForeignEffect> foreign)
    {
        var copy = new PropertySet(ContentHash, Name, GameRules, Stats, Actions, Rolls, Entries, _books);
        copy._aliases = EffectAliases.Match(this, foreign);
        return copy;
    }

    private readonly Lazy<IReadOnlyDictionary<Guid, Effect>> _byId = new(() => entries.SelectMany(e => e.Effects).SelectMany(Flatten).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First()));
    static IEnumerable<Effect> Flatten(Effect e) => new[] { e }.Concat(e.Children.SelectMany(Flatten));
}
