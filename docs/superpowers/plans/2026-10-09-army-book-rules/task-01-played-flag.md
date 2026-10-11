# Task 1: Played flag in the catalog

**Files:**
- Modify: `server/game/Properties/PropertySet.cs` (`CatalogEntry`)
- Modify: `server/game/Properties/PropertySetDocument.cs` (`EntryDocument`, `Parse`, `Validate`, `ToSet`)
- Modify: `server/game/Properties/PostgresPropertiesStore.cs` (read and write `played`)
- Modify: `server/game/Effects/LiveProperties.cs` (`Gather`)
- Create: `server/management/Persistence/Migrations/008_catalog_played.sql`
- Modify: `docs/schema.md` (the `catalog_entry` table)
- Test: `tests/Tabletop.GameServer.Tests/PlayedFlagTests.cs` (new), `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `CatalogEntry.Played` (bool, default true), `EntryDocument.Played` (bool?, null = played; `Parse` turns `true`
  into null so a written `"played": true` and an omitted one hash alike), column `management.catalog_entry.played
  boolean not null default true`.

Spec *Played flag*: "`CatalogEntry` gains `Played` (default true; `played` in JSON, omitted when true). … `PropertiesJson.Validate`
refuses `played: false` on an entry with effects." and "`Gather` skips an entry with `Played == false` as it skips an
absent name today. Nothing else reads it."

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/PlayedFlagTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class PlayedFlagTests
{
    const string Doc = """
    { "name": "Doc", "gameRules": "grimdark-future", "rolls": [ "hit" ],
      "entries": [
        { "name": "Banner", "kind": "model", "aggregation": "any", "ruleText": "", "played": false },
        { "name": "Shown", "kind": "model", "aggregation": "all", "ruleText": "Shown.", "played": true }
      ] }
    """;

    [Fact]
    public void Played_false_is_read_and_played_true_reads_as_omitted()
    {
        var doc = PropertiesJson.Parse(Doc);
        Assert.False(doc.Entries.Single(e => e.Name == "Banner").Played);
        Assert.Null(doc.Entries.Single(e => e.Name == "Shown").Played);
        var omitted = PropertiesJson.Parse(Doc.Replace(", \"played\": true", ""));
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(omitted));
        Assert.DoesNotContain("played", PropertiesJson.EntryCanonical(omitted.Entries.Single(e => e.Name == "Shown")));
    }

    [Fact]
    public void A_set_carries_played_onto_its_entries()
    {
        var set = PropertiesJson.ToSet(PropertiesJson.Parse(Doc), Guid.CreateVersion7);
        Assert.False(set.Entry("Banner")!.Played);
        Assert.True(set.Entry("Shown")!.Played);
    }

    [Fact]
    public void Played_false_on_an_entry_with_effects_is_refused()
    {
        var bad = PropertiesJson.Parse(Doc.Replace("\"ruleText\": \"\", \"played\": false",
            "\"ruleText\": \"\", \"played\": false, \"effects\": [ { \"subject\": \"self\", \"ordinal\": 1, \"kind\": \"modify_roll\", \"targetRoll\": \"hit\", \"amount\": 1 } ]"));
        Assert.Contains("Banner: a text-only entry (played false) has no effects", PropertiesJson.Validate(bad));
    }

    [Fact]
    public void Gather_skips_an_unplayed_entry_as_it_skips_an_absent_name()
    {
        var unplayed = new EntryDocument("Not Yet", CatalogKind.Model, false, Aggregation.Any, null, null, "Not yet.", null, [], Played: false);
        var props = TestProperties.WithSpells(unplayed);
        var a = Unit("A", "4+", "4+", [P("Not Yet")], 1, [P("Not Yet")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.False(live.Has(Side.Attacker, "Not Yet"));
    }
}
```

In `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`, add after the first test:

```csharp
    [Fact]
    public async Task Played_round_trips_through_the_catalog_entry_table()
    {
        var name = "Played " + Guid.CreateVersion7().ToString("N")[24..32];
        var doc = new PropertySetDocument(name, "played-" + name, [], [], ["hit"],
            [new EntryDocument("Text Only", CatalogKind.Model, false, Aggregation.Any, null, null, "Text.", null, [], Played: false),
             new EntryDocument("Played", CatalogKind.Model, false, Aggregation.All, null, null, "Played.", null, [])]);
        var hash = PropertiesJson.Hash(doc);
        try
        {
            Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
            var set = (await _store.LoadAsync(hash, CancellationToken.None))!;
            Assert.False(set.Entry("Text Only")!.Played);
            Assert.True(set.Entry("Played")!.Played);
        }
        finally { await pg.DeletePropertiesAsync(hash); }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PlayedFlagTests"`
Expected: build fails with `CS1739: The best overload for 'EntryDocument' does not have a parameter named 'Played'` and
`'EntryDocument' does not contain a definition for 'Played'`.

- [ ] **Step 3: Add `Played` to the records**

In `server/game/Properties/PropertySet.cs`, replace the `CatalogEntry` record:

```csharp
/// <summary>One named rule. Effects may be empty: then it is a name GameRules checks for at a hook it owns. Played is false only on a generated
/// text-only entry (army-book rules spec → Played flag): the stat sheet shows its text, and LiveProperties.Gather skips it.</summary>
public sealed record CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction,
    string RuleText, SpellTargeting? Targeting, IReadOnlyList<Effect> Effects, bool Played = true);
```

In `server/game/Properties/PropertySetDocument.cs`, replace the `EntryDocument` record:

```csharp
/// <summary>Played: null when the entry is played (written only as `"played": false`, on a generated text-only entry).</summary>
public sealed record EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText,
    SpellTargeting? Targeting, IReadOnlyList<EffectDocument> Effects, bool? Played = null);
```

In `PropertiesJson.Parse`, replace the `Entries = …` line:

```csharp
            Entries = (doc.Entries ?? []).Select(e => e with { Effects = Fix(e.Effects), Played = e.Played == false ? false : null }).ToList(),
```

In `PropertiesJson.Validate`, inside `foreach (var e in d.Entries)`, after the `TakesParameter` check:

```csharp
            if (e.Played == false && e.Effects.Count > 0) problems.Add($"{e.Name}: a text-only entry (played false) has no effects");
```

In `PropertiesJson.ToSet`, replace the entries projection:

```csharp
            d.Entries.Select(e => new CatalogEntry(e.Name, e.Kind, e.TakesParameter, e.Aggregation, e.Cost, e.Faction, e.RuleText, e.Targeting,
                e.Effects.Select(Build).ToList(), e.Played != false)).ToList(),
```

- [ ] **Step 4: Skip an unplayed entry in `Gather`**

In `server/game/Effects/LiveProperties.cs`, `Gather`, add a local function as its first statement after `var result = new List<LiveProperty>();`:

```csharp
        // A text-only entry (Played false) plays as an absent name does (army-book rules spec → Played flag).
        CatalogEntry? Playing(string name) => ix.Properties.Entry(name) is { Played: true } e ? e : null;
```

Then replace the three name lookups:

```csharp
        foreach (var p in unit.Definition.Properties) if (Playing(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
```

```csharp
            if (Playing(group.Key) is not { } e || e.Kind == CatalogKind.Unit) continue;   // added above, off the definition
```

```csharp
        foreach (var (weaponId, modelId, p) in Lookup.WeaponProperties(state, party.WeaponIds))
            if (Playing(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, modelId, weaponId, null));
```

`Lookup.UnitKindOnSlots` returns unit-kind entries; the generator writes text-only entries as `model` or `weapon`, so
it never returns one.

- [ ] **Step 5: Run the document tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PlayedFlagTests|FullyQualifiedName~PropertySetDocumentTests"`
Expected: PASS.

- [ ] **Step 6: The migration and the store**

Create `server/management/Persistence/Migrations/008_catalog_played.sql`:

```sql
-- 008_catalog_played.sql: a generated text-only entry is not played (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Played flag).
alter table management.catalog_entry add column played boolean not null default true;
alter table management.catalog_entry add constraint catalog_entry_played_check check (played or kind <> 'spell');
```

In `server/game/Properties/PostgresPropertiesStore.cs`, `LoadAsync`, replace the entries query and mapping:

```csharp
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetDouble(10)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList(), r.GetBoolean(11)));
```

In `SeedAsync`, replace the `catalog_entry` insert:

```csharp
        foreach (var e in set.Entries)
            await Run("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)",
                set.ContentHash, e.Name, EnumText(e.Kind), e.TakesParameter, e.Aggregation is { } ag ? EnumText(ag) : null, e.Cost, e.Faction, e.RuleText,
                e.Targeting is { } t ? EnumText(t.Side) : null, e.Targeting is { } t2 ? EnumText(t2.Grain) : null, e.Targeting?.Count, e.Targeting is { } t3 ? (double?)t3.Range : null, e.Played);
```

- [ ] **Step 7: Apply the migration and run the store tests**

Run: `docker compose restart management && sleep 15 && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PropertiesStoreTests"`
Expected: PASS, `Played_round_trips_through_the_catalog_entry_table` included. (Management runs DbUp at startup; the
restart applies `008`.)

- [ ] **Step 8: The schema document**

In `docs/schema.md`, in the `catalog_entry` section (`### The catalog holds four kinds, and one of them is a spell`),
add after the paragraph ending *"a rule with no entry is tagged *not automated*."*:

```markdown
**`played`** (boolean, default true) is false only on an entry `scripts/ArmyBooks` generated as text only: the Army Forge
description as `rule_text`, no effects. The engine plays it as an absent name, and `GET /v1/properties/{hash}/rules`
returns it so the stat sheet tags the rule *not automated* while showing its text. A spell is always played.
```

In the entity diagram's `CATALOG_ENTRY` block, add `bool played` after `float8 target_range`.

- [ ] **Step 9: Run the game server suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add server/game/Properties/PropertySet.cs server/game/Properties/PropertySetDocument.cs server/game/Properties/PostgresPropertiesStore.cs \
  server/game/Effects/LiveProperties.cs server/management/Persistence/Migrations/008_catalog_played.sql docs/schema.md \
  tests/Tabletop.GameServer.Tests/PlayedFlagTests.cs tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs
git commit -m "Add the played flag: a text-only catalog entry plays as an absent name"
```
