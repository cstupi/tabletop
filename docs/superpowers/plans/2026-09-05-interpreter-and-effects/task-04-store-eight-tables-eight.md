# Task 4: The store — eight tables in, eight tables out — and the startup seeder

**Files:**
- Create: `server/game/Properties/IPropertiesStore.cs`, `server/game/Properties/PostgresPropertiesStore.cs`, `server/game/Properties/PropertiesSeeder.cs`
- Modify: `server/game/Program.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs` (the skeleton's fixture gains `DeletePropertiesAsync`)
- Test: `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; the skeleton's `RowMapping.EnumText/EnumFrom`, `PostgresFixture`; `scripts/postgres/init/001_schema.sql` (the eight tables, `effect.target_stat_level`); management migration `004_properties_seeded_at.sql` (`properties.seeded_at`).
- Produces: `interface IPropertiesStore { Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct); Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct); }` (`SeedAsync` returns false when the hash already exists, and then writes only `properties.seeded_at = now()` for it, so the set seeded last is the one management's `GET /v1/properties` lists per `(game_rules, name)`; `PostgresPropertiesStore.SeedAsync` throws `InvalidOperationException`, inside the seed transaction and so writing nothing, when an entry's name already has another `kind` in a stored set of the same `game_rules`, because management's `CatalogKinds` reads one kind per name across them; `InMemoryPropertiesStore` has no such check); `sealed class InMemoryPropertiesStore : IPropertiesStore` (+ `Add(PropertySet)`); `sealed class PostgresPropertiesStore(NpgsqlDataSource db) : IPropertiesStore`; `sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)` with `static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()` and `Task<IReadOnlyList<string>> SeedEmbeddedAsync(CancellationToken ct)` (returns the hashes now present); `Program.cs` registers `IPropertiesStore` and runs `SeedEmbeddedAsync` once at startup, before the host serves.
- Row mapping: `roll_result` values and the other `condition_value` rows are written in list order with an `ordinal`-free primary key `(condition_id, value)`, so they are read back sorted by a stable rule: the loader orders `condition_value` by `value` **except** for `roll_result`, whose two values are the roll name and `raw|modified` and are re-ordered so the roll name comes first (a roll named `raw` or `modified` is refused by `Validate`; `schema.md` → A condition is a row, and its values are rows, names the shape). Effect children are rows whose `parent_effect_id` is the parent; the loader reads all effect rows of the set, then assembles the tree ordered by `ordinal` then `id`.
- Who seeds: the game server, at startup, from its embedded documents — it is the only process that interprets the catalog, and the write is idempotent by hash. Management's `GET /v1/properties` then lists the set. One system, two processes, one database: the seeder writes these rows directly and passes through no endpoint. Ruling 1; `architecture.md` → Rules as data.

Spec: `schema.md` → `Properties` is eight tables; `architecture.md` → Pinning ("hashed and pinned in the game record"); management plan → Interfaces needed → Game-server plan ("load at least one `properties` row, without which `POST /v1/games` answers `422`").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PropertiesStoreTests(PostgresFixture pg)
{
    readonly PostgresPropertiesStore _store = new(pg.Db);

    [Fact]
    public async Task Seeding_the_core_document_round_trips_through_the_eight_tables_and_is_idempotent()
    {
        // [24..32], not [..6]: a v7 guid's leading hex digits are its millisecond timestamp, identical across calls made
        // minutes or hours apart; the tail is random.
        var doc = TestProperties.CoreDocument with { Name = "Core " + Guid.CreateVersion7().ToString("N")[24..32] };
        var hash = PropertiesJson.Hash(doc);
        Assert.Null(await _store.LoadAsync(hash, CancellationToken.None));
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        await pg.CountAsync("update management.properties set seeded_at = now() - interval '1 day' where content_hash = $1", hash);
        Assert.False(await _store.SeedAsync(doc, CancellationToken.None));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.properties where content_hash = $1 and seeded_at > now() - interval '1 hour'", hash));   // a re-seed marks the set current again
        var set = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.NotNull(set);
        var expected = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(Shape(expected), Shape(set));
        Assert.Equal(33, set.Entries.Count);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.properties where content_hash = $1", hash));
        Assert.Equal(expected.AllEffects().Count(), await pg.CountAsync("select count(*) from management.effect where properties_hash = $1", hash));
        var again = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.Equal(set.Entry("Bane")!.Effects[0].Id, again!.Entry("Bane")!.Effects[0].Id);     // ids are stable once seeded
        await pg.DeletePropertiesAsync(hash);
    }

    /// <summary>
    /// The set with every id blanked, as canonical JSON, so two loads compare on content. Row order within an entry
    /// carries no meaning (`PropertiesJson.ToSet` keeps the author's order; the store's loader returns `order by
    /// ordinal, id`, and `id` is not deterministic between siblings sharing an ordinal), so effects, their
    /// children and their conditions are each sorted by their own blanked, serialised content before comparing.
    /// Only `Ordinal` and the conjunction of `Conditions` carry meaning, and both are preserved either way.
    /// </summary>
    static string Shape(PropertySet s)
    {
        Effect Blank(Effect e) => e with
        {
            Id = Guid.Empty,
            Conditions = SortByContent(e.Conditions.Select(c => c with { Id = Guid.Empty })),
            Children = SortByContent(e.Children.Select(Blank)),
        };
        var entries = s.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => e with { Effects = SortByContent(e.Effects.Select(Blank)) });
        return ContentJson.Serialize(new { s.ContentHash, s.Name, s.GameRules, Stats = s.Stats.OrderBy(x => x.Level).ThenBy(x => x.Name), Actions = s.Actions.OrderBy(a => a.Name), Rolls = s.Rolls.OrderBy(r => r.Name), Entries = entries });
    }

    static List<T> SortByContent<T>(IEnumerable<T> items) =>
        items.Select(i => (Item: i, Key: ContentJson.Serialize(i))).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Item).ToList();

    [Fact]
    public async Task The_embedded_documents_seed_at_startup()
    {
        var docs = PropertiesSeeder.EmbeddedDocuments();
        Assert.Single(docs);
        var hashes = await new PropertiesSeeder(_store, Microsoft.Extensions.Logging.Abstractions.NullLogger<PropertiesSeeder>.Instance).SeedEmbeddedAsync(CancellationToken.None);
        Assert.Equal([PropertiesJson.Hash(docs[0])], hashes);
        Assert.NotNull(await _store.LoadAsync(hashes[0], CancellationToken.None));
    }

    static PropertySetDocument Tiny(string rules, string name, CatalogKind kind, string entry = "Alpha") =>
        new(name, rules, [], [], [], [new EntryDocument(entry, kind, false, kind == CatalogKind.Model ? Aggregation.All : null, null, null, "text", null, [])]);

    [Fact]
    public async Task A_second_set_that_changes_a_catalog_entrys_kind_for_the_same_rules_is_refused_and_a_consistent_one_seeds()
    {
        var rules = "kinds-" + Guid.CreateVersion7().ToString("N")[24..32];
        var first = Tiny(rules, "one", CatalogKind.Unit);
        var consistent = Tiny(rules, "two", CatalogKind.Unit, "Beta");
        var conflicting = Tiny(rules, "three", CatalogKind.Model);
        try
        {
            Assert.True(await _store.SeedAsync(first, CancellationToken.None));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SeedAsync(conflicting, CancellationToken.None));
            Assert.Contains("Alpha", ex.Message); Assert.Contains("Unit", ex.Message); Assert.Contains("Model", ex.Message);
            Assert.Null(await _store.LoadAsync(PropertiesJson.Hash(conflicting), CancellationToken.None));
            Assert.True(await _store.SeedAsync(consistent, CancellationToken.None));
        }
        finally { foreach (var d in new[] { first, consistent, conflicting }) await pg.DeletePropertiesAsync(PropertiesJson.Hash(d)); }
    }
}
```

Add `public Task DeletePropertiesAsync(string hash)` to the skeleton's `PostgresFixture`: it deletes the hash's `management.effect` rows first, because `effect`'s `target_*` foreign keys do not cascade, then `management.properties` where `content_hash = $1`, which cascades to the other tables. The fixture only tracks rows it seeded itself. The embedded core set is left in place after the second test: it is what a developer's database should hold.

- [ ] **Step 2: Run; expect compile errors. Write `IPropertiesStore.cs`**

```csharp
namespace Tabletop.GameServer.Properties;

/// <summary>The pinned sets. Load by hash at hydration; seed once from an authored document.</summary>
public interface IPropertiesStore
{
    Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct);
    /// <summary>Writes the eight tables for the document's hash. False, and nothing written, when the hash is already present.</summary>
    Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct);
}

public sealed class InMemoryPropertiesStore : IPropertiesStore
{
    private readonly Dictionary<string, PropertySet> _sets = new();
    public InMemoryPropertiesStore Add(PropertySet set) { _sets[set.ContentHash] = set; return this; }
    public Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct) => Task.FromResult(_sets.GetValueOrDefault(contentHash));
    public Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct)
    {
        var hash = PropertiesJson.Hash(document);
        if (_sets.ContainsKey(hash)) return Task.FromResult(false);
        _sets[hash] = PropertiesJson.ToSet(document, Guid.CreateVersion7);
        return Task.FromResult(true);
    }
}
```

- [ ] **Step 3: Write `PostgresPropertiesStore.cs`**

```csharp
using Npgsql;
using Tabletop.Content;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Properties;

/// <summary>The eight tables. Enum spellings are the schema's check-constraint values, which are the wire spellings (RowMapping).</summary>
public sealed class PostgresPropertiesStore(NpgsqlDataSource db) : IPropertiesStore
{
    public async Task<PropertySet?> LoadAsync(string hash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        string name, rules;
        await using (var cmd = new NpgsqlCommand("select name, game_rules from management.properties where content_hash = $1", conn) { Parameters = { new() { Value = hash } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct)) { if (!await r.ReadAsync(ct)) return null; (name, rules) = (r.GetString(0), r.GetString(1)); }

        var stats = await Rows(conn, "select level, name, kind from management.stat_definition where properties_hash = $1", hash, ct,
            r => new StatDefinition(EnumFrom<StatLevel>(r.GetString(0)), r.GetString(1), EnumFrom<StatKind>(r.GetString(2))));
        var actions = await Rows(conn, "select name, distance from management.action where properties_hash = $1", hash, ct, r => new ActionDefinition(r.GetString(0), r.GetDouble(1)));
        var rolls = await Rows(conn, "select name from management.roll where properties_hash = $1", hash, ct, r => new RollDefinition(r.GetString(0)));
        var values = (await Rows(conn, "select cv.condition_id, cv.value from management.condition_value cv join management.effect_condition c on c.id = cv.condition_id join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by cv.value", hash, ct,
            r => (Id: r.GetGuid(0), Value: r.GetString(1)))).ToLookup(x => x.Id, x => x.Value);
        var conditions = (await Rows(conn, "select c.id, c.effect_id, c.kind, c.subject, c.comparison, c.number from management.effect_condition c join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by c.id", hash, ct,
            r => (EffectId: r.GetGuid(1), Condition: Condition(r.GetGuid(0), EnumFrom<ConditionKind>(r.GetString(2)), NullableString(r, 3), EnumFrom<Comparison>(r.GetString(4)), r.IsDBNull(5) ? null : r.GetInt32(5), values[r.GetGuid(0)].ToList())))).ToLookup(x => x.EffectId, x => x.Condition);
        // Build captures locals read while the reader is open, never `r` itself — `r` is disposed when Rows returns,
        // but Build is not invoked until Children(...) runs afterward.
        var effectRows = await Rows(conn, "select id, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration from management.effect where properties_hash = $1 order by ordinal, id", hash, ct,
            r =>
            {
                var id = r.GetGuid(0);
                var entry = r.GetString(1);
                var parent = NullableGuid(r, 2);
                var subject = EnumFrom<EffectSubject>(r.GetString(3));
                var ordinal = r.GetInt32(4);
                var kind = EnumFrom<EffectKind>(r.GetString(5));
                var targetRoll = NullableString(r, 6);
                var targetStat = r.IsDBNull(8) ? null : new StatRef(EnumFrom<StatLevel>(r.GetString(7)), r.GetString(8));
                var targetEntry = NullableString(r, 9);
                var targetAction = NullableString(r, 10);
                var targetQuantity = r.IsDBNull(11) ? null : (TargetQuantity?)EnumFrom<TargetQuantity>(r.GetString(11));
                var targetResult = r.IsDBNull(12) ? null : (TargetResult?)EnumFrom<TargetResult>(r.GetString(12));
                var targetModifier = r.IsDBNull(13) ? null : (TargetModifier?)EnumFrom<TargetModifier>(r.GetString(13));
                var amount = r.IsDBNull(14) ? null : (int?)r.GetInt32(14);
                var amountFromParameter = r.GetBoolean(15);
                var threshold = r.IsDBNull(16) ? null : (int?)r.GetInt32(16);
                var capKind = r.IsDBNull(17) ? null : (CapKind?)EnumFrom<CapKind>(r.GetString(17));
                var replacement = NullableString(r, 18);
                var duration = EnumFrom<EffectDuration>(r.GetString(19));
                var effectConditions = conditions[id].ToList();
                return (Entry: entry, Parent: parent, Row: id, Build: new Func<IReadOnlyList<Effect>, Effect>(children => new Effect(id, subject, ordinal, kind,
                    targetRoll, targetStat, targetEntry, targetAction, targetQuantity, targetResult, targetModifier, amount, amountFromParameter, threshold, capKind,
                    replacement, duration, effectConditions, children)));
            });
        IReadOnlyList<Effect> Children(Guid parent) => effectRows.Where(x => x.Parent == parent).Select(x => x.Build(Children(x.Row))).ToList();
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetDouble(10)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList()));
        return new PropertySet(hash, name, rules, stats, actions, rolls, entries);
    }

    /// <summary>roll_result's two values are the roll name and raw|modified; the roll name goes first on the way back.</summary>
    static EffectCondition Condition(Guid id, ConditionKind kind, string? subject, Comparison cmp, int? number, List<string> values)
    {
        if (kind == ConditionKind.RollResult && values.Count == 2 && values[0] is "raw" or "modified") values.Reverse();
        return new EffectCondition(id, kind, subject is null ? null : EnumFrom<EffectSubject>(subject), cmp, number, values);
    }

    static async Task<List<T>> Rows<T>(NpgsqlConnection conn, string sql, string hash, CancellationToken ct, Func<NpgsqlDataReader, T> map)
    {
        var list = new List<T>();
        await using var cmd = new NpgsqlCommand(sql, conn) { Parameters = { new() { Value = hash } } };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }

    public async Task<bool> SeedAsync(PropertySetDocument doc, CancellationToken ct)
    {
        var set = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        async Task<int> Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        if (await Run("insert into management.properties (content_hash, name, game_rules) values ($1, $2, $3) on conflict do nothing", set.ContentHash, set.Name, set.GameRules) == 0)
        {
            // Already present: seeding it again makes it the set GET /v1/properties lists for its name.
            await Run("update management.properties set seeded_at = now() where content_hash = $1", set.ContentHash);
            await tx.CommitAsync(ct);
            return false;
        }
        // management's CatalogKinds reads one kind per name across a game_rules' sets; two kinds for a name would fail every import.
        var existing = new List<(string Name, string Kind)>();
        await using (var cmd = new NpgsqlCommand("select distinct ce.name, ce.kind from management.catalog_entry ce join management.properties p on p.content_hash = ce.properties_hash where p.game_rules = $1 and p.content_hash <> $2", conn, tx) { Parameters = { new() { Value = set.GameRules }, new() { Value = set.ContentHash } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct)) existing.Add((r.GetString(0), r.GetString(1)));
        foreach (var e in set.Entries)
            if (existing.FirstOrDefault(x => x.Name == e.Name && x.Kind != EnumText(e.Kind)) is { Name: not null } clash)
                throw new InvalidOperationException($"Properties '{set.Name}' gives catalog entry '{e.Name}' kind {e.Kind}, but game rules '{set.GameRules}' already has it as {EnumFrom<CatalogKind>(clash.Kind)}.");
        foreach (var s in set.Stats) await Run("insert into management.stat_definition (properties_hash, level, name, kind) values ($1, $2, $3, $4)", set.ContentHash, EnumText(s.Level), s.Name, EnumText(s.Kind));
        foreach (var a in set.Actions) await Run("insert into management.action (properties_hash, name, distance) values ($1, $2, $3)", set.ContentHash, a.Name, a.Distance);
        foreach (var r in set.Rolls) await Run("insert into management.roll (properties_hash, name) values ($1, $2)", set.ContentHash, r.Name);
        foreach (var e in set.Entries)
            await Run("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)",
                set.ContentHash, e.Name, EnumText(e.Kind), e.TakesParameter, e.Aggregation is { } ag ? EnumText(ag) : null, e.Cost, e.Faction, e.RuleText,
                e.Targeting is { } t ? EnumText(t.Side) : null, e.Targeting is { } t2 ? EnumText(t2.Grain) : null, e.Targeting?.Count, e.Targeting is { } t3 ? (double?)t3.Range : null);
        foreach (var e in set.Entries) foreach (var f in e.Effects) await Effect(e.Name, null, f);
        await tx.CommitAsync(ct);
        return true;

        async Task Effect(string entry, Guid? parent, Effect f)
        {
            await Run("insert into management.effect (id, properties_hash, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21)",
                f.Id, set.ContentHash, entry, parent, EnumText(f.Subject), f.Ordinal, EnumText(f.Kind), f.TargetRoll, f.TargetStat is { } s ? EnumText(s.Level) : null, f.TargetStat?.Name, f.TargetEntry, f.TargetAction,
                f.TargetQuantity is { } q ? EnumText(q) : null, f.TargetResult is { } tr ? EnumText(tr) : null, f.TargetModifier is { } tm ? EnumText(tm) : null, f.Amount, f.AmountFromParameter, f.Threshold,
                f.CapKind is { } ck ? EnumText(ck) : null, f.Replacement, EnumText(f.Duration));
            foreach (var c in f.Conditions)
            {
                await Run("insert into management.effect_condition (id, effect_id, kind, subject, comparison, number) values ($1, $2, $3, $4, $5, $6)", c.Id, f.Id, EnumText(c.Kind), c.Subject is { } cs ? EnumText(cs) : null, EnumText(c.Comparison), c.Number);
                foreach (var v in c.Values) await Run("insert into management.condition_value (condition_id, value) values ($1, $2)", c.Id, v);
            }
            foreach (var child in f.Children) await Effect(entry, f.Id, child);
        }
    }
}
```

- [ ] **Step 4: Write `PropertiesSeeder.cs` and register**

```csharp
namespace Tabletop.GameServer.Properties;

/// <summary>Authored content reaches the tables here: every embedded *.properties.json is seeded once, by hash, when the server starts.</summary>
public sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)
{
    public static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        return asm.GetManifestResourceNames().Where(n => n.EndsWith(".properties.json")).OrderBy(n => n)
            .Select(n => { using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s); return PropertiesJson.Parse(r.ReadToEnd()); }).ToList();
    }

    public async Task<IReadOnlyList<string>> SeedEmbeddedAsync(CancellationToken ct)
    {
        var hashes = new List<string>();
        foreach (var doc in EmbeddedDocuments())
        {
            var inserted = await store.SeedAsync(doc, ct);
            var hash = PropertiesJson.Hash(doc);
            log.LogInformation("Properties '{Name}' {Hash}: {What}", doc.Name, hash, inserted ? "seeded" : "already present");
            hashes.Add(hash);
        }
        return hashes;
    }
}
```

`Program.cs`: `builder.Services.AddSingleton<IPropertiesStore, PostgresPropertiesStore>(); builder.Services.AddSingleton<PropertiesSeeder>();` and, after `var app = builder.Build();`: `if (!app.Environment.IsEnvironment("Test")) await app.Services.GetRequiredService<PropertiesSeeder>().SeedEmbeddedAsync(CancellationToken.None);` (the test factory seeds through the fixture). The `Test` guard keeps `WebApplicationFactory` tests off the shared database's write path.

- [ ] **Step 5: Run `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests --filter PropertiesStoreTests`; expect 3 passed. Then `dotnet run --project server/game` once and check `psql -c "select name, content_hash from management.properties"` lists the core set. Commit: `Load and seed Properties through the eight tables`.**
