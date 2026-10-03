# Task 3: The schema: migration 005, the new grammar fields, the store

**Files:**
- Create: `server/management/Persistence/Migrations/005_spell_library.sql`
- Modify: `server/game/Properties/PropertySet.cs` (enum values, `TargetTerrain`, `Effect` fields)
- Modify: `server/game/Properties/PropertySetDocument.cs` (`EffectDocument` fields, `ToSet`)
- Modify: `server/game/Properties/PostgresPropertiesStore.cs` (books, `target_terrain`, `target_condition`)
- Test: `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`, `tests/Tabletop.Management.Tests/MigrationTests.cs`

**Interfaces:**
- Consumes: `BookRecord`, `PropertySet.Books` (Task 1); `RowMapping.EnumText/EnumFrom`.
- Produces: `EffectKind.CountAsTerrain, TakeTest, SetCondition, Move`; `ConditionKind.HoldsProperty, MostModels`; `enum TargetTerrain { Difficult, Dangerous }`; `Effect(..., IReadOnlyList<Effect> Children, TargetTerrain? TargetTerrain = null, string? TargetCondition = null)`; `EffectDocument(..., IReadOnlyList<EffectDocument> Children, TargetTerrain? TargetTerrain = null, string? TargetCondition = null)`; tables `management.book`, `management.book_spell`; column `management.model_slot.army_book`.

Spec → *Schema and wire*. The enum values exist from here so the store round-trips them; Tasks 9 and 10 give them behaviour.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`:

```csharp
    [Fact]
    public async Task Books_and_the_new_effect_columns_round_trip()
    {
        var test = new EffectDocument(EffectSubject.Opponent, 10, EffectKind.TakeTest, "morale", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [],
            [new EffectDocument(EffectSubject.Opponent, 20, EffectKind.SetCondition, null, null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], [], TargetCondition: "fatigued")]);
        var stride = new EffectDocument(EffectSubject.Self, 10, EffectKind.CountAsTerrain, null, null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [], TargetTerrain: TargetTerrain.Dangerous);
        EntryDocument Spell(string n, EffectDocument f) => new(n, CatalogKind.Spell, false, null, 1, null, $"{n}.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [f]);
        var doc = TestProperties.RulebookDocument with
        {
            Name = "Books " + Guid.CreateVersion7().ToString("N")[24..32],
            Entries = [.. TestProperties.RulebookDocument.Entries, Spell("Terrifying Fury", test), Spell("Cursed Stride", stride)],
            Books = [new BookRecord("b-one", "Book One", "3.5.3", ["Terrifying Fury", "Cursed Stride"]), new BookRecord("b-two", "Book Two", null, ["Cursed Stride"])],
        };
        var hash = PropertiesJson.Hash(doc);
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        var set = (await _store.LoadAsync(hash, CancellationToken.None))!;
        Assert.Equal(["Terrifying Fury", "Cursed Stride"], set.Books["b-one"].Spells);
        Assert.Equal(("Book Two", (string?)null), (set.Books["b-two"].Name, set.Books["b-two"].Version));
        Assert.Equal("fatigued", set.Entry("Terrifying Fury")!.Effects[0].Children[0].TargetCondition);
        Assert.Equal(TargetTerrain.Dangerous, set.Entry("Cursed Stride")!.Effects[0].TargetTerrain);
        Assert.Equal(PropertiesJson.Hash(doc), set.ContentHash);
        await pg.DeletePropertiesAsync(hash);
    }
```

In `tests/Tabletop.Management.Tests/MigrationTests.cs`, change `Assert.Equal(33, tables);` to `Assert.Equal(35, tables);` and add before the method's closing brace:

```csharp
        Assert.Contains("army_book", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='model_slot'"));
        Assert.Contains("target_terrain", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~PropertiesStoreTests`
Expected: compile errors (`EffectKind.TakeTest`, `TargetCondition`, `TargetTerrain` do not exist).

- [ ] **Step 3: Write the migration**

Create `server/management/Persistence/Migrations/005_spell_library.sql`:

```sql
-- 005_spell_library.sql: army books in a pinned set, the four effect kinds and two predicates the spell library adds, and the
-- army book each imported model slot came from (docs/superpowers/specs/2026-10-02-spell-library-design.md).
create table management.book (
    properties_hash text not null references management.properties(content_hash) on delete cascade,
    uid             text not null,
    name            text not null,
    version         text,
    primary key (properties_hash, uid)
);
create table management.book_spell (
    properties_hash text not null,
    book_uid        text not null,
    entry_name      text not null,
    ordinal         int  not null,
    primary key (properties_hash, book_uid, entry_name),
    foreign key (properties_hash, book_uid)   references management.book(properties_hash, uid) on delete cascade,
    foreign key (properties_hash, entry_name) references management.catalog_entry(properties_hash, name) on delete cascade
);

alter table management.effect drop constraint effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move'));
alter table management.effect add column target_terrain text check (target_terrain in ('difficult', 'dangerous'));
alter table management.effect add column target_condition text;

alter table management.effect_condition drop constraint effect_condition_kind_check;
alter table management.effect_condition add constraint effect_condition_kind_check check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result',
    'actor_state', 'position_terrain', 'round_number', 'usage_count', 'holds_property', 'most_models'));

alter table management.model_slot add column army_book text;
```

The two dropped constraints carry PostgreSQL's generated names for an inline column check, `<table>_<column>_check`; `MigrationTests.Running_the_migrator_twice_is_a_no_op` and a fresh `tabletop_test_*` database both exercise them.

- [ ] **Step 4: Add the enum values and the fields**

In `server/game/Properties/PropertySet.cs`:

```csharp
public enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers,
    CountAsTerrain, TakeTest, SetCondition, Move }
/// <summary>p.11's terrain types a holder can count as being in (count_as_terrain).</summary>
public enum TargetTerrain { Difficult, Dangerous }
public enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount, HoldsProperty, MostModels }
```

and append two trailing optional parameters to `Effect`:

```csharp
public sealed record Effect(
    Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind,
    string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier,
    int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration,
    IReadOnlyList<EffectCondition> Conditions, IReadOnlyList<Effect> Children,
    TargetTerrain? TargetTerrain = null, string? TargetCondition = null)
```

In `server/game/Properties/PropertySetDocument.cs`, append the same two to `EffectDocument`:

```csharp
public sealed record EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind,
    string? Replacement, EffectDuration Duration, IReadOnlyList<ConditionDocument> Conditions, IReadOnlyList<EffectDocument> Children,
    TargetTerrain? TargetTerrain = null, string? TargetCondition = null);
```

and pass them in `ToSet`'s `Build`:

```csharp
        Effect Build(EffectDocument f) => new(newId(), f.Subject, f.Ordinal, f.Kind, f.TargetRoll, f.TargetStat, f.TargetEntry, f.TargetAction, f.TargetQuantity, f.TargetResult,
            f.TargetModifier, f.Amount, f.AmountFromParameter, f.Threshold, f.CapKind, f.Replacement, f.Duration,
            f.Conditions.Select(c => new EffectCondition(newId(), c.Kind, c.Subject, c.Comparison, c.Number, c.Values)).ToList(), f.Children.Select(Build).ToList(),
            f.TargetTerrain, f.TargetCondition);
```

In `Validate`, widen the spell families to the three the spec names (spell library → *Grammar additions*, kind 14):

```csharp
            if (spell) foreach (var f in e.Effects.Where(f => f.Kind is not EffectKind.AddHit and not EffectKind.TakeTest && f.Duration != EffectDuration.Once))
                problems.Add($"{e.Name}: a spell's effect is add_hit, take_test or has duration once; {f.Kind} with duration {f.Duration} applies nowhere");
```

In `ValidateEffect`, add:

```csharp
        if (f.TargetTerrain is not null && f.Kind != EffectKind.CountAsTerrain) problems.Add(At("target_terrain is count_as_terrain only"));
        if (f.Kind == EffectKind.CountAsTerrain && f.TargetTerrain is null) problems.Add(At("count_as_terrain needs target_terrain"));
        if (f.TargetCondition is not null && f.Kind != EffectKind.SetCondition) problems.Add(At("target_condition is set_condition only"));
        if (f.Kind == EffectKind.SetCondition && f.TargetCondition is null) problems.Add(At("set_condition needs target_condition"));
```

- [ ] **Step 5: Read and write the books and the columns**

In `PostgresPropertiesStore.LoadAsync`, extend the effect query's column list with `, target_terrain, target_condition` (indexes 20 and 21), read them inside the row lambda:

```csharp
                var targetTerrain = r.IsDBNull(20) ? null : (TargetTerrain?)EnumFrom<TargetTerrain>(r.GetString(20));
                var targetCondition = NullableString(r, 21);
```

pass `targetTerrain, targetCondition` as the last two arguments of `new Effect(...)` in `Build`, load the books before the `return`:

```csharp
        var bookSpells = (await Rows(conn, "select book_uid, entry_name from management.book_spell where properties_hash = $1 order by book_uid, ordinal", hash, ct,
            r => (Book: r.GetString(0), Entry: r.GetString(1)))).ToLookup(x => x.Book, x => x.Entry);
        var books = await Rows(conn, "select uid, name, version from management.book where properties_hash = $1 order by uid", hash, ct,
            r => (Uid: r.GetString(0), Name: r.GetString(1), Version: NullableString(r, 2)));
        return new PropertySet(hash, name, rules, stats, actions, rolls, entries, books.Select(b => new BookRecord(b.Uid, b.Name, b.Version, bookSpells[b.Uid].ToList())).ToList());
```

In `SeedAsync`, extend the effect insert with `, target_terrain, target_condition` and `$22, $23`, passing `f.TargetTerrain is { } tt ? EnumText(tt) : null, f.TargetCondition`, and write the books after the effects loop:

```csharp
        foreach (var b in set.Books.Values)
        {
            await Run("insert into management.book (properties_hash, uid, name, version) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, b.Name, b.Version);
            foreach (var (spell, i) in b.Spells.Select((s, i) => (s, i)))
                await Run("insert into management.book_spell (properties_hash, book_uid, entry_name, ordinal) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, spell, i);
        }
```

- [ ] **Step 6: Apply the migration to the compose database**

Run: `docker compose up -d` and then `./scripts/management`; stop it once its log shows the migrator ran `005_spell_library.sql`.
Expected: `select count(*) from management.book` answers `0` in `docker compose exec postgres psql -U tabletop -c "select count(*) from management.book"`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PropertiesStoreTests|FullyQualifiedName~PropertySetDocumentTests" && dotnet test tests/Tabletop.Management.Tests --filter FullyQualifiedName~MigrationTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add server/management/Persistence/Migrations/005_spell_library.sql server/game/Properties tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs tests/Tabletop.Management.Tests/MigrationTests.cs
git commit -m "Store army books and the spell library's effect columns"
```
