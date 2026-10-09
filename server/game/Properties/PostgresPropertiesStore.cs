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
        var effectRows = await Rows(conn, "select id, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration, target_terrain, target_condition, amount_dice from management.effect where properties_hash = $1 order by ordinal, id", hash, ct,
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
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetDouble(10)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList()));
        var bookSpells = (await Rows(conn, "select book_uid, entry_name from management.book_spell where properties_hash = $1 order by book_uid, ordinal", hash, ct,
            r => (Book: r.GetString(0), Entry: r.GetString(1)))).ToLookup(x => x.Book, x => x.Entry);
        var books = await Rows(conn, "select uid, name, version from management.book where properties_hash = $1 order by uid", hash, ct,
            r => (Uid: r.GetString(0), Name: r.GetString(1), Version: NullableString(r, 2)));
        return new PropertySet(hash, name, rules, stats, actions, rolls, entries, books.Select(b => new BookRecord(b.Uid, b.Name, b.Version, bookSpells[b.Uid].ToList())).ToList());
    }

    /// <summary>roll_result's values are the roll name, raw|modified and an optional success|fail; they go back in that order.</summary>
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
        foreach (var b in set.Books.Values)
        {
            await Run("insert into management.book (properties_hash, uid, name, version) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, b.Name, b.Version);
            foreach (var (spell, i) in b.Spells.Select((s, i) => (s, i)))
                await Run("insert into management.book_spell (properties_hash, book_uid, entry_name, ordinal) values ($1, $2, $3, $4)", set.ContentHash, b.Uid, spell, i);
        }
        await tx.CommitAsync(ct);
        return true;

        async Task Effect(string entry, Guid? parent, Effect f)
        {
            await Run("insert into management.effect (id, properties_hash, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration, target_terrain, target_condition, amount_dice) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24)",
                f.Id, set.ContentHash, entry, parent, EnumText(f.Subject), f.Ordinal, EnumText(f.Kind), f.TargetRoll, f.TargetStat is { } s ? EnumText(s.Level) : null, f.TargetStat?.Name, f.TargetEntry, f.TargetAction,
                f.TargetQuantity is { } q ? EnumText(q) : null, f.TargetResult is { } tr ? EnumText(tr) : null, f.TargetModifier is { } tm ? EnumText(tm) : null, f.Amount, f.AmountFromParameter, f.Threshold,
                f.CapKind is { } ck ? EnumText(ck) : null, f.Replacement, EnumText(f.Duration), f.TargetTerrain is { } tt ? EnumText(tt) : null, f.TargetCondition, f.AmountDice);
            foreach (var c in f.Conditions)
            {
                await Run("insert into management.effect_condition (id, effect_id, kind, subject, comparison, number) values ($1, $2, $3, $4, $5, $6)", c.Id, f.Id, EnumText(c.Kind), c.Subject is { } cs ? EnumText(cs) : null, EnumText(c.Comparison), c.Number);
                foreach (var v in c.Values) await Run("insert into management.condition_value (condition_id, value) values ($1, $2)", c.Id, v);
            }
            foreach (var child in f.Children) await Effect(entry, f.Id, child);
        }
    }
}
