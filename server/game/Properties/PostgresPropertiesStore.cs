using Npgsql;
using Tabletop.Content;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Properties;

/// <summary>The ten tables. Enum spellings are the schema's check-constraint values, which are the wire spellings (RowMapping).</summary>
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
        var actions = await Rows(conn, "select name, distance from management.action where properties_hash = $1 order by position, name", hash, ct, r => new ActionDefinition(r.GetString(0), r.GetDouble(1)));
        var rolls = await Rows(conn, "select name from management.roll where properties_hash = $1", hash, ct, r => new RollDefinition(r.GetString(0)));
        var values = (await Rows(conn, "select cv.condition_id, cv.value from management.condition_value cv join management.effect_condition c on c.id = cv.condition_id join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by cv.position, cv.value", hash, ct,
            r => (Id: r.GetGuid(0), Value: r.GetString(1)))).ToLookup(x => x.Id, x => x.Value);
        var conditions = (await Rows(conn, "select c.id, c.effect_id, c.kind, c.subject, c.comparison, c.number from management.effect_condition c join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by c.position, c.id", hash, ct,
            r => (EffectId: r.GetGuid(1), Condition: Condition(r.GetGuid(0), EnumFrom<ConditionKind>(r.GetString(2)), NullableString(r, 3), EnumFrom<Comparison>(r.GetString(4)), r.IsDBNull(5) ? null : r.GetInt32(5), values[r.GetGuid(0)].ToList())))).ToLookup(x => x.EffectId, x => x.Condition);
        // Build captures locals read while the reader is open, never `r` itself — `r` is disposed when Rows returns,
        // but Build is not invoked until Children(...) runs afterward.
        var effectRows = await Rows(conn, "select id, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration, target_terrain, target_condition, amount_dice from management.effect where properties_hash = $1 order by position, ordinal, id", hash, ct,
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
                var targetTerrain = r.IsDBNull(20) ? null : (TargetTerrain?)EnumFrom<TargetTerrain>(r.GetString(20));
                var targetCondition = NullableString(r, 21);
                var amountDice = NullableString(r, 22);
                var effectConditions = conditions[id].ToList();
                return (Entry: entry, Parent: parent, Row: id, Build: new Func<IReadOnlyList<Effect>, Effect>(children => new Effect(id, subject, ordinal, kind,
                    targetRoll, targetStat, targetEntry, targetAction, targetQuantity, targetResult, targetModifier, amount, amountFromParameter, threshold, capKind,
                    replacement, duration, effectConditions, children, targetTerrain, targetCondition, amountDice)));
            });
        IReadOnlyList<Effect> Children(Guid parent) => effectRows.Where(x => x.Parent == parent).Select(x => x.Build(Children(x.Row))).ToList();
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played, target_line_of_sight from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetDouble(10), r.IsDBNull(12) ? null : r.GetBoolean(12)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList(), r.GetBoolean(11)));
        var bookSpells = (await Rows(conn, "select book_uid, entry_name from management.book_spell where properties_hash = $1 order by book_uid, ordinal", hash, ct,
            r => (Book: r.GetString(0), Entry: r.GetString(1)))).ToLookup(x => x.Book, x => x.Entry);
        var books = await Rows(conn, "select uid, name, version from management.book where properties_hash = $1 order by uid", hash, ct,
            r => (Uid: r.GetString(0), Name: r.GetString(1), Version: NullableString(r, 2)));
        return new PropertySet(hash, name, rules, stats, actions, rolls, entries, books.Select(b => new BookRecord(b.Uid, b.Name, b.Version, bookSpells[b.Uid].ToList())).ToList());
    }

    /// <summary>roll_result's values are the roll name, raw|modified and an optional success|fail; they go back in that order (a row
    /// without a position, seeded before 007_properties_positions.sql and not since, reads its values in value order and is re-ordered here).</summary>
    static EffectCondition Condition(Guid id, ConditionKind kind, string? subject, Comparison cmp, int? number, List<string> values)
    {
        static int Place(string v) => v switch { "raw" or "modified" => 1, Effects.Conditions.Success or Effects.Conditions.Fail => 2, _ => 0 };
        if (kind == ConditionKind.RollResult) values = values.OrderBy(Place).ToList();
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

    public Task<bool> SeedAsync(PropertySetDocument doc, CancellationToken ct) => SeedAsync(doc, Guid.CreateVersion7, ct);

    /// <summary>
    /// Every ordered child row records its index among its siblings in the document (`position`): actions, effects, conditions,
    /// condition values. Row ids order nothing: v7 ids minted in one loop are random within a millisecond.
    /// </summary>
    public async Task<bool> SeedAsync(PropertySetDocument doc, Func<Guid> newId, CancellationToken ct)
    {
        var set = PropertiesJson.ToSet(doc, newId);
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
            if (await Unpositioned(conn, tx, set.ContentHash, ct)) await Position(set, Run, ct);
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
        foreach (var (a, i) in set.Actions.Select((a, i) => (a, i))) await Run("insert into management.action (properties_hash, name, distance, position) values ($1, $2, $3, $4)", set.ContentHash, a.Name, a.Distance, i);
        foreach (var r in set.Rolls) await Run("insert into management.roll (properties_hash, name) values ($1, $2)", set.ContentHash, r.Name);
        foreach (var e in set.Entries)
            await Run("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played, target_line_of_sight) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)",
                set.ContentHash, e.Name, EnumText(e.Kind), e.TakesParameter, e.Aggregation is { } ag ? EnumText(ag) : null, e.Cost, e.Faction, e.RuleText,
                e.Targeting is { } t ? EnumText(t.Side) : null, e.Targeting is { } t2 ? EnumText(t2.Grain) : null, e.Targeting?.Count, e.Targeting is { } t3 ? (double?)t3.Range : null, e.Played,
                e.Targeting?.LineOfSight);
        foreach (var e in set.Entries) foreach (var (f, i) in e.Effects.Select((f, i) => (f, i))) await Effect(e.Name, null, f, i);
        foreach (var b in set.Books.Values)
        {
            await Run("insert into management.book (properties_hash, uid, name, version) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, b.Name, b.Version);
            foreach (var (spell, i) in b.Spells.Select((s, i) => (s, i)))
                await Run("insert into management.book_spell (properties_hash, book_uid, entry_name, ordinal) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, spell, i);
        }
        await tx.CommitAsync(ct);
        return true;

        async Task Effect(string entry, Guid? parent, Effect f, int position)
        {
            await Run("insert into management.effect (id, properties_hash, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration, target_terrain, target_condition, amount_dice, position) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24, $25)",
                f.Id, set.ContentHash, entry, parent, EnumText(f.Subject), f.Ordinal, EnumText(f.Kind), f.TargetRoll, f.TargetStat is { } s ? EnumText(s.Level) : null, f.TargetStat?.Name, f.TargetEntry, f.TargetAction,
                f.TargetQuantity is { } q ? EnumText(q) : null, f.TargetResult is { } tr ? EnumText(tr) : null, f.TargetModifier is { } tm ? EnumText(tm) : null, f.Amount, f.AmountFromParameter, f.Threshold,
                f.CapKind is { } ck ? EnumText(ck) : null, f.Replacement, EnumText(f.Duration), f.TargetTerrain is { } tt ? EnumText(tt) : null, f.TargetCondition, f.AmountDice, position);
            foreach (var (c, i) in f.Conditions.Select((c, i) => (c, i)))
            {
                await Run("insert into management.effect_condition (id, effect_id, kind, subject, comparison, number, position) values ($1, $2, $3, $4, $5, $6, $7)", c.Id, f.Id, EnumText(c.Kind), c.Subject is { } cs ? EnumText(cs) : null, EnumText(c.Comparison), c.Number, i);
                foreach (var (v, j) in c.Values.Select((v, j) => (v, j))) await Run("insert into management.condition_value (condition_id, value, position) values ($1, $2, $3)", c.Id, v, j);
            }
            foreach (var (child, i) in f.Children.Select((child, i) => (child, i))) await Effect(entry, f.Id, child, i);
        }
    }

    static async Task<bool> Unpositioned(NpgsqlConnection conn, NpgsqlTransaction tx, string hash, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            select exists (select 1 from management.action where properties_hash = $1 and position is null)
                or exists (select 1 from management.effect where properties_hash = $1 and position is null)
                or exists (select 1 from management.effect_condition c join management.effect e on e.id = c.effect_id where e.properties_hash = $1 and c.position is null)
                or exists (select 1 from management.condition_value v join management.effect_condition c on c.id = v.condition_id join management.effect e on e.id = c.effect_id where e.properties_hash = $1 and v.position is null)
            """, conn, tx) { Parameters = { new() { Value = hash } } };
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// A set seeded before rows recorded their positions: its order is not in the rows, so it is taken from the document the hash names.
    /// Rows keep their ids, because a game's grants name effect ids. Each document sibling is matched to a stored sibling with the same
    /// content regardless of order; siblings with equal content are interchangeable, so which of them takes which position changes nothing.
    /// </summary>
    async Task Position(PropertySet document, Func<string, object?[], Task<int>> run, CancellationToken ct)
    {
        var stored = await LoadAsync(document.ContentHash, ct) ?? throw new InvalidOperationException($"Properties {document.ContentHash} vanished while being positioned.");
        foreach (var (a, i) in document.Actions.Select((a, i) => (a, i)))
            await run("update management.action set position = $3 where properties_hash = $1 and name = $2", [document.ContentHash, a.Name, i]);
        foreach (var entry in document.Entries) await Siblings(entry.Effects, stored.Entry(entry.Name)?.Effects ?? []);

        async Task Siblings(IReadOnlyList<Effect> wanted, IReadOnlyList<Effect> rows)
        {
            var free = rows.ToList();
            foreach (var (f, i) in wanted.Select((f, i) => (f, i)))
            {
                var row = Take(free, EffectKey(f), EffectKey) ?? throw Mismatch($"effect {f.Kind} at {i}");
                await run("update management.effect set position = $2 where id = $1", [row.Id, i]);
                var conditions = row.Conditions.ToList();
                foreach (var (c, j) in f.Conditions.Select((c, j) => (c, j)))
                {
                    var kept = Take(conditions, ConditionKey(c), ConditionKey) ?? throw Mismatch($"condition {c.Kind} at {j}");
                    await run("update management.effect_condition set position = $2 where id = $1", [kept.Id, j]);
                    foreach (var (v, k) in c.Values.Select((v, k) => (v, k)))
                        await run("update management.condition_value set position = $3 where condition_id = $1 and value = $2", [kept.Id, v, k]);
                }
                await Siblings(f.Children, row.Children);
            }
        }
        InvalidOperationException Mismatch(string what) => new($"Properties {document.ContentHash}: the stored rows have no {what} its document has.");
        static T? Take<T>(List<T> free, string key, Func<T, string> keyOf) where T : class
        {
            var hit = free.FirstOrDefault(x => keyOf(x) == key);
            if (hit is not null) free.Remove(hit);
            return hit;
        }
    }

    /// <summary>Content without ids or order: conditions, their values and children compared as sets.</summary>
    static string EffectKey(Effect e) =>
        System.Text.Json.JsonSerializer.Serialize(e with { Id = Guid.Empty, Conditions = [], Children = [] }, PropertiesJson.Options)
        + "|" + string.Join(",", e.Conditions.Select(ConditionKey).Order(StringComparer.Ordinal)) + "|" + string.Join(",", e.Children.Select(EffectKey).Order(StringComparer.Ordinal));

    static string ConditionKey(EffectCondition c) =>
        System.Text.Json.JsonSerializer.Serialize(c with { Id = Guid.Empty, Values = c.Values.Order(StringComparer.Ordinal).ToList() }, PropertiesJson.Options);
}
