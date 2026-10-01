# Task 2: The authored document — validation, canonical hash, `ToSet`

**Files:**
- Create: `server/game/Properties/PropertySetDocument.cs`
- Test: `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`

**Interfaces:**
- Consumes: Task 1; `Tabletop.Content.ContentJson`.
- Produces: `PropertySetDocument(string Name, string GameRules, IRL<StatDefinition> Stats, IRL<ActionDefinition> Actions, IRL<string> Rolls, IRL<EntryDocument> Entries)`; `EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText, SpellTargeting? Targeting, IRL<EffectDocument> Effects)`; `EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction, TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration, IRL<ConditionDocument> Conditions, IRL<EffectDocument> Children)`; `ConditionDocument(ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IRL<string> Values)`; `static class PropertiesJson` with `PropertySetDocument Parse(string json)`, `string Canonical(PropertySetDocument)` (sorted, camelCase, no ids), `string Hash(PropertySetDocument)` (`"sha256:" + 64 lowercase hex over the canonical form`), `IReadOnlyList<string> Validate(PropertySetDocument)` (every reference resolves, every per-kind check of `schema.md` holds, and every top-level effect on a `spell` entry is either `add_hit` or `duration = once`; empty when valid), `PropertySet ToSet(PropertySetDocument, Func<Guid> newId)` (throws `InvalidDataException` listing the validation failures).
- The document has no ids: effect and condition ids are minted when the rows are inserted (Task 4) and read back with them, so `Grant.EffectId` is stable within a database. The hash covers content only, so the same file seeds the same hash on every machine.

Spec: `architecture.md` → Rules as data ("content-addressed by SHA-256 over a canonical serialisation … seeded as rows by the game server at startup, once per hash"), Pinning ("content-addressed"); `schema.md` → `Properties` is eight tables ("pinning by content hash now needs a canonical serialisation to hash"), The catalog's per-kind check, Per-kind `CHECK` constraints on `effect`; spell effects → Duration ("always with one duration: **once**"), Damage spells need nothing new (kind 6). Ruling 1 records who seeds.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetDocumentTests
{
    public const string ApJson = """
    { "name": "Doc", "gameRules": "grimdark-future",
      "stats": [ { "level": "unit", "name": "Quality", "kind": "target_number" }, { "level": "unit", "name": "Defense", "kind": "target_number" } ],
      "actions": [ { "name": "Hold", "distance": 0 }, { "name": "Advance", "distance": 6 } ],
      "rolls": [ "hit", "defense" ],
      "entries": [
        { "name": "Regeneration", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 5+ it is ignored.",
          "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5,
                         "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound" } ] } ] },
        { "name": "Bane", "kind": "weapon", "ruleText": "Ignores Regeneration; re-roll unmodified Defense rolls of 6.",
          "effects": [ { "subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense",
                         "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "defense", "raw" ] } ] },
                       { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" } ] }
      ] }
    """;

    [Fact]
    public void Parses_hashes_canonically_and_builds_a_set()
    {
        var doc = PropertiesJson.Parse(ApJson);
        var reordered = doc with { Entries = doc.Entries.Reverse().ToList(), Rolls = doc.Rolls.Reverse().ToList() };
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(reordered));
        Assert.Matches("^sha256:[0-9a-f]{64}$", PropertiesJson.Hash(doc));
        Assert.NotEqual(PropertiesJson.Hash(doc), PropertiesJson.Hash(doc with { Name = "Other" }));
        var set = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(PropertiesJson.Hash(doc), set.ContentHash);
        var bane = set.Entry("Bane")!;
        Assert.Equal(2, bane.Effects.Count);
        Assert.Equal(["defense", "raw"], bane.Effects[0].Conditions[0].Values);
        Assert.Equal(EffectKind.OverrideResult, set.Entry("Regeneration")!.Effects[0].Children[0].Kind);
        Assert.Equal(4, set.AllEffects().Count());
        Assert.NotEqual(Guid.Empty, bane.Effects[0].Id);
        Assert.All(set.AllEffects(), e => Assert.Equal(EffectDuration.Interaction, e.Duration));
    }

    [Fact]
    public void Validation_checks_every_reference_and_per_kind_rule()
    {
        var doc = PropertiesJson.Parse(ApJson);
        Assert.Empty(PropertiesJson.Validate(doc));
        var e0 = doc.Entries[1].Effects[0];
        var bad = doc with { Entries = [
            doc.Entries[0] with { Aggregation = null },                                                    // model needs aggregation
            doc.Entries[1] with { Effects = [ e0 with { TargetRoll = "save" }, e0 with { Kind = EffectKind.Multiply, Threshold = 2 } ] },
            new EntryDocument("Bolt", CatalogKind.Spell, true, null, null, "Book", "x", null, []),        // spell: no parameter, needs cost + targeting
            new EntryDocument("Ghost", CatalogKind.Weapon, false, null, null, null, "x", null,
                [ new EffectDocument(EffectSubject.Self, 0, EffectKind.SuppressProperty, null, null, "Nobody", null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], []) ]) ] };
        var problems = PropertiesJson.Validate(bad);
        Assert.Contains(problems, p => p.Contains("Regeneration") && p.Contains("aggregation"));
        Assert.Contains(problems, p => p.Contains("save"));
        Assert.Contains(problems, p => p.Contains("threshold") && p.Contains("Bane"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("cost"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("parameter"));
        Assert.Contains(problems, p => p.Contains("Nobody"));
        Assert.Throws<InvalidDataException>(() => PropertiesJson.ToSet(bad, Guid.CreateVersion7));
    }

    [Fact]
    public void A_spells_effect_is_hits_or_a_once_grant_and_nothing_else()
    {
        var doc = PropertiesJson.Parse(ApJson);
        EntryDocument Spell(EffectKind kind, EffectDuration duration) => new("Bolt", CatalogKind.Spell, false, null, 2, "Book", "x",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, kind, null, null, kind == EffectKind.GrantProperty ? "Regeneration" : null, null, null, null, null, 3, false, null, null, null, duration, [], [])]);
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.AddHit, EffectDuration.Interaction)] }));
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Once)] }));
        Assert.Contains(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Interaction)] }),
            p => p.Contains("Bolt") && p.Contains("duration"));
    }

    [Fact]
    public void Roll_result_condition_must_have_exactly_two_values_with_second_raw_or_modified()
    {
        var doc = PropertiesJson.Parse(ApJson);
        // Well-formed: ["defense", "raw"] validates clean
        var wellFormed = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit", "raw"])], [])
            ])
        ]};
        Assert.Empty(PropertiesJson.Validate(wellFormed));

        // One value only: ["hit"]
        var oneValue = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit"])], [])
            ])
        ]};
        var problems = PropertiesJson.Validate(oneValue);
        Assert.Contains(problems, p => p.Contains("Test") && p.Contains("roll_result") && p.Contains("values"));

        // Invalid second value: ["hit", "modified_invalid"]
        var badModifier = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["hit", "modified_invalid"])], [])
            ])
        ]};
        var badProblems = PropertiesJson.Validate(badModifier);
        Assert.Contains(badProblems, p => p.Contains("Test") && p.Contains("modifier"));

        // Undeclared roll: ["unknown", "raw"]
        var undeclaredRoll = doc with { Entries = [
            new EntryDocument("Test", CatalogKind.Weapon, false, null, null, null, "x", null, [
                new EffectDocument(EffectSubject.Self, 0, EffectKind.ForceReroll, "hit", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction,
                    [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 5, ["unknown", "raw"])], [])
            ])
        ]};
        var undeclaredProblems = PropertiesJson.Validate(undeclaredRoll);
        Assert.Contains(undeclaredProblems, p => p.Contains("Test") && p.Contains("unknown"));
    }

    [Fact]
    public void Rolls_named_raw_or_modified_are_reserved()
    {
        var doc = PropertiesJson.Parse(ApJson);
        var withRawRoll = doc with { Rolls = [.. doc.Rolls, "raw"] };
        var problems = PropertiesJson.Validate(withRawRoll);
        Assert.Contains(problems, p => p.Contains("raw") && p.Contains("reserved"));

        var withModifiedRoll = doc with { Rolls = [.. doc.Rolls, "modified"] };
        var modifiedProblems = PropertiesJson.Validate(withModifiedRoll);
        Assert.Contains(modifiedProblems, p => p.Contains("modified") && p.Contains("reserved"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `PropertySetDocument.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

// The authored form: the eight tables without ids or the hash. Field names are the tables' columns in camelCase.
public sealed record PropertySetDocument(string Name, string GameRules, IReadOnlyList<StatDefinition> Stats, IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<string> Rolls, IReadOnlyList<EntryDocument> Entries);
public sealed record EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText,
    SpellTargeting? Targeting, IReadOnlyList<EffectDocument> Effects);
public sealed record EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind,
    string? Replacement, EffectDuration Duration, IReadOnlyList<ConditionDocument> Conditions, IReadOnlyList<EffectDocument> Children);
public sealed record ConditionDocument(ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values);

public static class PropertiesJson
{
    static readonly JsonSerializerOptions Options = ContentJson.Configure(new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

    public static PropertySetDocument Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<PropertySetDocument>(json, Options) ?? throw new JsonException("empty document");
        // Omitted lists are empty lists, omitted duration is interaction, omitted amountFromParameter is false: the schema's defaults.
        return doc with
        {
            Stats = doc.Stats ?? [], Actions = doc.Actions ?? [], Rolls = doc.Rolls ?? [],
            Entries = (doc.Entries ?? []).Select(e => e with { Effects = Fix(e.Effects) }).ToList(),
        };
        static IReadOnlyList<EffectDocument> Fix(IReadOnlyList<EffectDocument>? effects) =>
            (effects ?? []).Select(f => f with { Conditions = (f.Conditions ?? []).Select(c => c with { Values = c.Values ?? [] }).ToList(), Children = Fix(f.Children) }).ToList();
    }

    /// <summary>Sorted where order carries no meaning (stats, actions, rolls, entries); effect and condition order is the author's and is kept.</summary>
    public static string Canonical(PropertySetDocument d) => JsonSerializer.Serialize(d with
    {
        Stats = d.Stats.OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.Ordinal).ToList(),
        Actions = d.Actions.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(),
        Rolls = d.Rolls.OrderBy(r => r, StringComparer.Ordinal).ToList(),
        Entries = d.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList(),
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
            if (spell) foreach (var f in e.Effects.Where(f => f.Kind != EffectKind.AddHit && f.Duration != EffectDuration.Once))
                problems.Add($"{e.Name}: a spell's effect is add_hit or has duration once; {f.Kind} with duration {f.Duration} applies nowhere");
            foreach (var f in e.Effects) ValidateEffect(d, names, e.Name, f, problems);
        }
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
        foreach (var child in f.Children) ValidateEffect(d, names, entry, child, problems);
    }

    public static PropertySet ToSet(PropertySetDocument d, Func<Guid> newId)
    {
        var problems = Validate(d);
        if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));
        Effect Build(EffectDocument f) => new(newId(), f.Subject, f.Ordinal, f.Kind, f.TargetRoll, f.TargetStat, f.TargetEntry, f.TargetAction, f.TargetQuantity, f.TargetResult,
            f.TargetModifier, f.Amount, f.AmountFromParameter, f.Threshold, f.CapKind, f.Replacement, f.Duration,
            f.Conditions.Select(c => new EffectCondition(newId(), c.Kind, c.Subject, c.Comparison, c.Number, c.Values)).ToList(), f.Children.Select(Build).ToList());
        return new PropertySet(Hash(d), d.Name, d.GameRules, d.Stats, d.Actions, d.Rolls.Select(r => new RollDefinition(r)).ToList(),
            d.Entries.Select(e => new CatalogEntry(e.Name, e.Kind, e.TakesParameter, e.Aggregation, e.Cost, e.Faction, e.RuleText, e.Targeting, e.Effects.Select(Build).ToList())).ToList());
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PropertySetDocumentTests`; expect 5 passed. Commit: `Parse, validate and content-address an authored Properties document`.**
