using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

// The authored form: the eight tables without ids or the hash. Field names are the tables' columns in camelCase.
public sealed record PropertySetDocument(string Name, string GameRules, IReadOnlyList<StatDefinition> Stats, IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<string> Rolls, IReadOnlyList<EntryDocument> Entries, IReadOnlyList<BookRecord>? Books = null);
/// <summary>An army book: its Army Forge uid, name and version, and the spell entries it lists, in its own order (spell library spec).</summary>
public sealed record BookRecord(string Uid, string Name, string? Version, IReadOnlyList<string> Spells);
/// <summary>A generated army-book file (`Content/army-books/&lt;uid&gt;.book.json`): the book's identity and its spells as entries.</summary>
public sealed record BookDocument(string Uid, string Name, string? Version, string GameRules, IReadOnlyList<EntryDocument> Spells);
public sealed record EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText,
    SpellTargeting? Targeting, IReadOnlyList<EffectDocument> Effects);
public sealed record EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind,
    string? Replacement, EffectDuration Duration, IReadOnlyList<ConditionDocument> Conditions, IReadOnlyList<EffectDocument> Children,
    TargetTerrain? TargetTerrain = null, string? TargetCondition = null);
public sealed record ConditionDocument(ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values);

public static class PropertiesJson
{
    public static readonly JsonSerializerOptions Options = ContentJson.Configure(new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

    public static PropertySetDocument Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<PropertySetDocument>(json, Options) ?? throw new JsonException("empty document");
        // Omitted lists are empty lists, omitted duration is interaction, omitted amountFromParameter is false: the schema's defaults.
        return doc with
        {
            Stats = doc.Stats ?? [], Actions = doc.Actions ?? [], Rolls = doc.Rolls ?? [],
            Entries = (doc.Entries ?? []).Select(e => e with { Effects = Fix(e.Effects) }).ToList(),
            Books = (doc.Books ?? []).Select(b => b with { Spells = b.Spells ?? [] }).ToList(),
        };
        static IReadOnlyList<EffectDocument> Fix(IReadOnlyList<EffectDocument>? effects) =>
            (effects ?? []).Select(f => f with { Conditions = (f.Conditions ?? []).Select(c => c with { Values = c.Values ?? [] }).ToList(), Children = Fix(f.Children) }).ToList();
    }

    public static BookDocument ParseBook(string json)
    {
        var book = JsonSerializer.Deserialize<BookDocument>(json, Options) ?? throw new JsonException("empty book");
        var asDocument = Parse(JsonSerializer.Serialize(new PropertySetDocument(book.Name, book.GameRules, [], [], [], book.Spells ?? []), Options));
        return book with { Spells = asDocument.Entries };   // the same defaults Parse applies to an entry's effects and conditions
    }

    /// <summary>An entry's canonical form: what composition compares two same-named entries by.</summary>
    public static string EntryCanonical(EntryDocument e) => JsonSerializer.Serialize(e, Options);

    /// <summary>Sorted where order carries no meaning (stats, actions, rolls, entries); effect and condition order is the author's and is kept.</summary>
    public static string Canonical(PropertySetDocument d) => JsonSerializer.Serialize(d with
    {
        Stats = d.Stats.OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.Ordinal).ToList(),
        Actions = d.Actions.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(),
        Rolls = d.Rolls.OrderBy(r => r, StringComparer.Ordinal).ToList(),
        Entries = d.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList(),
        Books = (d.Books ?? []).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList(),
    }, Options);

    public static string Hash(PropertySetDocument d) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(d))));

    public static IReadOnlyList<string> Validate(PropertySetDocument d)
    {
        var problems = new List<string>();
        var names = d.Entries.Select(e => e.Name).ToHashSet();
        if (names.Count != d.Entries.Count) problems.Add("entry names must be unique");
        foreach (var r in d.Rolls.Where(r => r is "raw" or "modified")) problems.Add($"roll '{r}' is reserved");
        foreach (var e in d.Entries)
        {
            if ((e.Kind == CatalogKind.Model) != (e.Aggregation is not null)) problems.Add($"{e.Name}: aggregation is set exactly when kind is model");
            var spell = e.Kind == CatalogKind.Spell;
            if (spell != (e.Cost is not null && e.Targeting is not null)) problems.Add($"{e.Name}: cost and targeting are set exactly when kind is spell");
            if (spell && e.TakesParameter) problems.Add($"{e.Name}: a spell takes no parameter");
            // Spell effects (09-04) read forty-eight real spells as N hits or a grant that is "always with one duration: once".
            // Anything else on a spell would be applied nowhere, so it is an authoring error rather than a silent no-op.
            if (spell) foreach (var f in e.Effects.Where(f => f.Kind is not EffectKind.AddHit and not EffectKind.TakeTest && f.Duration != EffectDuration.Once))
                problems.Add($"{e.Name}: a spell's effect is add_hit, take_test or has duration once; {f.Kind} with duration {f.Duration} applies nowhere");
            foreach (var f in e.Effects) ValidateEffect(d, names, e.Name, f, problems);
        }
        var books = d.Books ?? [];
        if (books.Select(b => b.Uid).Distinct().Count() != books.Count) problems.Add("book uids must be unique");
        var spells = d.Entries.Where(e => e.Kind == CatalogKind.Spell).Select(e => e.Name).ToHashSet();
        foreach (var b in books)
            foreach (var s in b.Spells.Where(s => !spells.Contains(s)))
                problems.Add($"book {b.Uid} lists '{s}', which is no spell entry");
        var listed = books.SelectMany(b => b.Spells).ToHashSet();
        foreach (var e in d.Entries.Where(e => e.Kind == CatalogKind.Spell && !listed.Contains(e.Name))) problems.Add($"{e.Name}: no book lists this spell");
        return problems;
    }

    static void ValidateEffect(PropertySetDocument d, HashSet<string> names, string entry, EffectDocument f, List<string> problems)
    {
        string At(string what) => $"{entry}: {what}";
        if (f.TargetRoll is not null && !d.Rolls.Contains(f.TargetRoll)) problems.Add(At($"no roll '{f.TargetRoll}'"));
        if (f.TargetStat is { } s && !d.Stats.Any(x => x.Level == s.Level && x.Name == s.Name)) problems.Add(At($"no stat {s.Level}/{s.Name}"));
        if (f.TargetEntry is not null && !names.Contains(f.TargetEntry)) problems.Add(At($"no entry '{f.TargetEntry}'"));
        if (f.TargetAction is not null && d.Actions.All(a => a.Name != f.TargetAction)) problems.Add(At($"no action '{f.TargetAction}'"));
        if (f.Threshold is not null && f.Kind != EffectKind.Roll) problems.Add(At("threshold is roll only"));
        if (f.CapKind is not null && f.Kind != EffectKind.Multiply) problems.Add(At("cap_kind is multiply only"));
        if (f.Replacement is not null && f.Kind != EffectKind.ReplaceStat) problems.Add(At("replacement is replace_stat only"));
        if (f.TargetTerrain is not null && f.Kind != EffectKind.CountAsTerrain) problems.Add(At("target_terrain is count_as_terrain only"));
        if (f.Kind == EffectKind.AddHit && f.TargetQuantity is TargetQuantity.Wounds && !f.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            problems.Add(At("add_hit wounds is per die: it needs a roll_result condition"));
        if (f.Kind == EffectKind.CountAsTerrain && f.TargetTerrain is null) problems.Add(At("count_as_terrain needs target_terrain"));
        if (f.TargetCondition is not null && f.Kind != EffectKind.SetCondition) problems.Add(At("target_condition is set_condition only"));
        if (f.Kind == EffectKind.SetCondition && f.TargetCondition is null) problems.Add(At("set_condition needs target_condition"));
        if (f.Kind == EffectKind.Roll && f.Children.Count == 0) problems.Add(At("a roll needs a per-success child"));
        foreach (var c in f.Conditions)
            if (c.Kind is ConditionKind.Range or ConditionKind.RoundNumber or ConditionKind.UsageCount && c.Subject is not null) problems.Add(At($"{c.Kind} takes no subject"));
        foreach (var c in f.Conditions)
        {
            if (c.Kind == ConditionKind.RollResult)
            {
                if (c.Values.Count != 2) problems.Add(At($"roll_result condition has {c.Values.Count} values, need exactly 2"));
                else
                {
                    if (!d.Rolls.Contains(c.Values[0])) problems.Add(At($"roll_result roll '{c.Values[0]}' is not declared"));
                    if (c.Values[1] is not "raw" and not "modified") problems.Add(At($"roll_result modifier is '{c.Values[1]}', need 'raw' or 'modified'"));
                }
            }
        }
        foreach (var c in f.Conditions.Where(c => c.Kind is ConditionKind.HoldsProperty or ConditionKind.MostModels))
        {
            if (c.Subject is null) problems.Add(At($"{c.Kind} needs a subject"));
            if (c.Values.Count != 1) problems.Add(At($"{c.Kind} names exactly one value"));
            else if (c.Kind == ConditionKind.HoldsProperty && !names.Contains(c.Values[0])) problems.Add(At($"holds_property names no entry '{c.Values[0]}'"));
        }
        foreach (var child in f.Children) ValidateEffect(d, names, entry, child, problems);
    }

    public static PropertySet ToSet(PropertySetDocument d, Func<Guid> newId)
    {
        var problems = Validate(d);
        if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));
        Effect Build(EffectDocument f) => new(newId(), f.Subject, f.Ordinal, f.Kind, f.TargetRoll, f.TargetStat, f.TargetEntry, f.TargetAction, f.TargetQuantity, f.TargetResult,
            f.TargetModifier, f.Amount, f.AmountFromParameter, f.Threshold, f.CapKind, f.Replacement, f.Duration,
            f.Conditions.Select(c => new EffectCondition(newId(), c.Kind, c.Subject, c.Comparison, c.Number, c.Values)).ToList(), f.Children.Select(Build).ToList(),
            f.TargetTerrain, f.TargetCondition);
        return new PropertySet(Hash(d), d.Name, d.GameRules, d.Stats, d.Actions, d.Rolls.Select(r => new RollDefinition(r)).ToList(),
            d.Entries.Select(e => new CatalogEntry(e.Name, e.Kind, e.TakesParameter, e.Aggregation, e.Cost, e.Faction, e.RuleText, e.Targeting, e.Effects.Select(Build).ToList())).ToList(),
            d.Books ?? []);
    }
}
