# Task 2: Played delivery

**Files:**
- Modify: `server/management/Games/GameRepository.cs` (`CatalogRuleText`, `RuleTexts`)
- Modify: `client/src/Core/ManagementDtos.cs` (`RuleTextDto`)
- Modify: `client/src/Core/RuleTexts.cs`
- Modify: `client/src/Hud/StatSheet.cs` (the tooltip of an empty text)
- Test: `tests/Tabletop.Management.Tests/RuleTextTests.cs`, `tests/Tabletop.Client.Tests/RuleTextsTests.cs`,
  `tests/Tabletop.Client.Tests/UnitSheetTests.cs`

**Interfaces:**
- Consumes: column `management.catalog_entry.played` (Task 1).
- Produces: `GET /v1/properties/{hash}/rules` → `[{ "name", "kind", "takesParameter", "ruleText", "played" }]`;
  `RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText, bool Played = true)`;
  `RuleTexts.NotAutomated(name)` true for an absent name or an entry with `Played == false`.

Spec *Played flag*: "`GET /v1/properties/{hash}/rules` (`GameRepository.RuleTexts`) returns `Played` with each text." and
"`RuleTexts.NotAutomated(name)` … is true for an absent name or an entry with `Played == false`. `StatSheet` shows the
rule dimmed with " (not automated)" and the Army Forge text as its tooltip". `StatSheet.Rules` already dims and tags a
`SheetRule` whose `NotAutomated` is set and shows its `Text` as the tooltip; `UnitSheet.Rule` reads both from
`RuleTexts`, so the client change is in `RuleTexts`. An entry with an empty text (Banner, Musician) gets the fallback
tooltip.

- [ ] **Step 1: Write the failing tests**

In `tests/Tabletop.Management.Tests/RuleTextTests.cs`, add to the `catalog_entry` insert of `Seed()` one row, so the
values list reads:

```csharp
            insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, rule_text, played) values
                (@Hash, 'Tough', 'model', true, 'all', 'Must take X wounds before being killed.', true),
                (@Hash, 'AP', 'weapon', true, null, 'Targets get -X to Defense rolls when blocking hits.', true),
                (@Hash, 'Fearless', 'unit', false, null, 'Gets +1 to morale tests.', true),
                (@Hash, 'Precise', 'model', false, 'any', 'Gets +1 to hit when attacking.', false)
            on conflict do nothing;
```

and replace the assertion block of `Rules_returns_the_catalog_entries_of_a_pinned_set_by_name`:

```csharp
        var rows = json.RootElement.EnumerateArray().Select(e => (
            e.GetProperty("name").GetString(), e.GetProperty("kind").GetString(),
            e.GetProperty("takesParameter").GetBoolean(), e.GetProperty("ruleText").GetString(), e.GetProperty("played").GetBoolean())).ToList();
        Assert.Equal([
            ("AP", "weapon", true, "Targets get -X to Defense rolls when blocking hits.", true),
            ("Fearless", "unit", false, "Gets +1 to morale tests.", true),
            ("Precise", "model", false, "Gets +1 to hit when attacking.", false),
            ("Tough", "model", true, "Must take X wounds before being killed.", true),
        ], rows);
```

In `tests/Tabletop.Client.Tests/RuleTextsTests.cs`, add:

```csharp
    [Fact]
    public void An_unplayed_entry_keeps_its_text_and_is_not_automated()
    {
        var rules = RuleTexts.From([Tough, new RuleTextDto("Precise", "model", false, "Gets +1 to hit when attacking.", Played: false)]);
        Assert.Equal("Gets +1 to hit when attacking.", rules.TextOf("Precise"));
        Assert.False(rules.IsAutomated("Precise"));
        Assert.True(rules.NotAutomated("Precise"));
        Assert.True(rules.IsAutomated("Tough"));
    }

    [Fact]
    public void A_row_without_played_reads_as_played()
    {
        var row = System.Text.Json.JsonSerializer.Deserialize<RuleTextDto>("""{"name":"Tough","kind":"model","takesParameter":true,"ruleText":"T."}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.True(row.Played);
    }
```

In `tests/Tabletop.Client.Tests/UnitSheetTests.cs`, add:

```csharp
    [Fact]
    public void An_unplayed_rule_shows_its_text_tagged_not_automated()
    {
        var catalog = RuleTexts.From([new RuleTextDto("Tough", "model", true, "Must take X wounds to be killed."),
            new RuleTextDto("Fearless", "model", false, "Gets +1 to morale tests.", Played: false)]);
        var a = Applier(Samples.Board);
        var s = UnitSheet.Of(a.Projection, new Names(Game, a.Projection), Samples.Unit, catalog, new Dictionary<Guid, int> { [Samples.Model] = 3 });
        Assert.Contains(("Fearless", (string?)"Gets +1 to morale tests.", true), s.Rules.Select(r => (r.Label, r.Text, r.NotAutomated)));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~RuleTextsTests|FullyQualifiedName~UnitSheetTests"`
Expected: build fails with `CS1739: … does not have a parameter named 'Played'`.

Run: `dotnet test tests/Tabletop.Management.Tests --filter RuleTextTests`
Expected: `Rules_returns_…` fails with `KeyNotFoundException` for `played`.

- [ ] **Step 3: Return `played` from management**

In `server/management/Games/GameRepository.cs`, replace the record and the query:

```csharp
/// <summary>One catalog entry's player-facing text (spec: in-game UX → Rule text). Played is false on a generated text-only entry
/// (army-book rules spec → Played flag). Dapper maps by constructor parameter name.</summary>
public sealed record CatalogRuleText(string Name, string Kind, bool TakesParameter, string RuleText, bool Played);
```

```csharp
        return (await c.QueryAsync<CatalogRuleText>("""
            select name as Name, kind as Kind, takes_parameter as TakesParameter, rule_text as RuleText, played as Played
            from catalog_entry where properties_hash = @hash order by name
            """, new { hash })).ToList();
```

- [ ] **Step 4: Read `played` in the client**

In `client/src/Core/ManagementDtos.cs`, replace `RuleTextDto`:

```csharp
/// <summary>One catalog entry's text. Played is false on a generated text-only entry; a row without it is played.</summary>
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText, bool Played = true);
```

Replace the body of `client/src/Core/RuleTexts.cs`:

```csharp
namespace Tabletop.Client.Core;

/// <summary>A pinned set's catalog by rule name: the hover text of every rule, and whether the engine plays it.
/// A rule with no entry, or a text-only entry (Played false), is not automated (army-book rules spec → Played flag). Empty is the
/// catalog not loaded yet, or a failed fetch: it knows no text and tags nothing, since an unknown catalog says nothing about what is automated.</summary>
public sealed class RuleTexts
{
    readonly IReadOnlyDictionary<string, (string Text, bool Played)> _byName;
    RuleTexts(IReadOnlyDictionary<string, (string Text, bool Played)> byName, bool loaded) { _byName = byName; Loaded = loaded; }

    public static RuleTexts Empty { get; } = new(new Dictionary<string, (string, bool)>(), loaded: false);

    public static RuleTexts From(IReadOnlyList<RuleTextDto> rows)
    {
        var byName = new Dictionary<string, (string Text, bool Played)>(StringComparer.Ordinal);
        foreach (var row in rows) byName.TryAdd(row.Name, (row.RuleText, row.Played));
        return new RuleTexts(byName, loaded: true);
    }

    public bool Loaded { get; }
    public string? TextOf(string name) => _byName.TryGetValue(name, out var e) ? e.Text : null;
    public bool IsAutomated(string name) => _byName.TryGetValue(name, out var e) && e.Played;
    /// <summary>The *not automated* tag: the catalog is loaded, and has no entry for the name or a text-only one.</summary>
    public bool NotAutomated(string name) => Loaded && !IsAutomated(name);
}
```

- [ ] **Step 5: The stat sheet's tooltip for an empty text**

In `client/src/Hud/StatSheet.cs`, `Rules`, replace the `TooltipText` line:

```csharp
                TooltipText = !string.IsNullOrEmpty(r.Text) ? r.Text : r.NotAutomated ? "Not automated: no rule text." : "",
```

and the summary above the method:

```csharp
    /// <summary>One label per rule; hovering shows its catalog text. A rule the engine does not play reads "not automated", its text still shown.</summary>
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet test tests/Tabletop.Management.Tests --filter "RuleTextTests|MigrationTests" && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: PASS; `check-client` prints nothing.

- [ ] **Step 7: Commit**

```bash
git add server/management/Games/GameRepository.cs client/src/Core/ManagementDtos.cs client/src/Core/RuleTexts.cs client/src/Hud/StatSheet.cs \
  tests/Tabletop.Management.Tests/RuleTextTests.cs tests/Tabletop.Client.Tests/RuleTextsTests.cs tests/Tabletop.Client.Tests/UnitSheetTests.cs
git commit -m "Deliver played with each rule text; tag a text-only rule not automated"
```
