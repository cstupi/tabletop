using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Persistence;

public sealed partial class PostgresGameRows(NpgsqlDataSource db) : IGameRows
{
    public async Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var game = new NpgsqlCommand("select name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by from management.game where id = $1", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } };
        await using var r = await game.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var (name, status, rules, hash, shape, enforcement, waits, rngSeed, createdBy) =
            (r.GetString(0), EnumFrom<GameStatus>(r.GetString(1)), r.GetString(2), r.GetString(3), EnumFrom<SchedulerShape>(r.GetString(4)), EnumFrom<Enforcement>(r.GetString(5)), r.GetBoolean(6), r.GetInt64(7), r.GetGuid(8));
        await r.CloseAsync();

        var teams = new List<Team>();
        await using (var cmd = new NpgsqlCommand("select id, ordinal from management.team where game_id = $1 order by ordinal", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var tr = await cmd.ExecuteReaderAsync(ct))
            while (await tr.ReadAsync(ct)) teams.Add(new Team(tr.GetGuid(0), tr.GetInt32(1)));

        var participants = new List<Participant>();
        await using (var cmd = new NpgsqlCommand("select p.id, p.account_id, a.name, p.team_id, p.army_id from management.participant p join management.account a on a.id = p.account_id where p.game_id = $1 order by p.id", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var pr = await cmd.ExecuteReaderAsync(ct))
            while (await pr.ReadAsync(ct)) participants.Add(new Participant(pr.GetGuid(0), pr.GetGuid(1), pr.GetString(2), NullableGuid(pr, 3), NullableGuid(pr, 4)));

        return new GameInfo(gameId, name, status, createdBy, rules, hash, shape, enforcement, waits, rngSeed, teams, participants);
    }

    public async Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select id from management.participant where game_id = $1 and account_id = $2");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(accountId);
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    public async Task<bool> PropertiesExistAsync(string hash, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select exists (select 1 from management.properties where content_hash = $1)");
        cmd.Parameters.AddWithValue(hash);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await ReadTableStateAsync(conn, null, gameId, ct);
    }

    internal static async Task<TableState?> ReadTableStateAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid gameId, CancellationToken ct)
    {
        Guid id; string? name; TableSize size; VisualRef texture;
        await using (var cmd = new NpgsqlCommand("select id, name, width, length, texture_builtin, texture_asset_id from management.table_state where game_id = $1", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (id, name, size, texture) = (r.GetGuid(0), NullableString(r, 1), new TableSize(r.GetDouble(2), r.GetDouble(3)), Visual(NullableString(r, 4), NullableGuid(r, 5)));
        }
        var volumes = new Dictionary<Guid, List<TerrainVolume>>();
        await using (var cmd = new NpgsqlCommand($"select v.id, v.terrain_placement_id, {string.Join(", ", VolumeColumns.Split(", ").Select(c => "v." + c))} from management.terrain_volume v join management.terrain_placement p on p.id = v.terrain_placement_id where p.table_state_id = $1 order by v.id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var list = volumes.TryGetValue(r.GetGuid(1), out var l) ? l : volumes[r.GetGuid(1)] = new();
                list.Add(new TerrainVolume(r.GetGuid(0), VolumeFrom(r, 2)));
            }
        var terrain = new List<TerrainPlacement>();
        await using (var cmd = new NpgsqlCommand("select id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale from management.terrain_placement where table_state_id = $1 order by id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                terrain.Add(new TerrainPlacement(r.GetGuid(0), Visual(NullableString(r, 1), NullableGuid(r, 2)), new Point3(r.GetDouble(3), r.GetDouble(4), r.GetDouble(5)), new Yaw(r.GetDouble(6)), r.GetDouble(7),
                    volumes.TryGetValue(r.GetGuid(0), out var vs) ? vs : []));
        var markers = new List<Marker>();
        await using (var cmd = new NpgsqlCommand("select id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id from management.marker where table_state_id = $1 order by id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                markers.Add(new Marker(r.GetGuid(0), EnumFrom<MarkerKind>(r.GetString(1)), new Point3(r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)), new Yaw(r.GetDouble(5)), r.GetDouble(6), NullableString(r, 7), Visual(NullableString(r, 8), NullableGuid(r, 9))));
        var zones = new List<DeploymentZone>();
        await using (var cmd = new NpgsqlCommand("select id, shape, team_ordinal from management.deployment_zone where table_state_id = $1 order by team_ordinal", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) zones.Add(new DeploymentZone(r.GetGuid(0), r.GetString(1), r.GetInt32(2)));
        return new TableState(id, name, size, texture, terrain, markers, zones);
    }

    public async Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct)
    {
        var result = new List<ArmyInPlay>();
        await using var cmd = db.CreateCommand("select id, army_snapshot from management.participant where game_id = $1 and army_snapshot is not null order by id");
        cmd.Parameters.AddWithValue(gameId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new ArmyInPlay(r.GetGuid(0), ContentJson.Deserialize<ArmySnapshot>(r.GetString(1))));
        return result;
    }

    public async Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        Guid owner; string name, system, source;
        await using (var cmd = new NpgsqlCommand("select owner_id, name, game_system, source from management.army where id = $1", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (owner, name, system, source) = (r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3));
        }
        var stats = new Dictionary<Guid, Dictionary<string, string>>();
        var props = new Dictionary<Guid, List<PropertyInstance>>();
        // order by ctid: weapons, stats and properties have no ordinal column, so the only way to hand each slot's Stats dictionary
        // back in the order it was written (and so serialize identically) is physical insertion order.
        foreach (var (table, owner_col, parent) in new[] { ("unit_stat", "unit_id", "unit u on u.id = s.unit_id where u.army_id"), ("slot_stat", "slot_id", "model_slot m on m.id = s.slot_id join management.unit u on u.id = m.unit_id where u.army_id"), ("weapon_stat", "weapon_id", "weapon w on w.id = s.weapon_id join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id") })
        await using (var cmd = new NpgsqlCommand($"select s.{owner_col}, s.stat, s.value from management.{table} s join management.{parent} = $1 order by s.ctid", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (stats.TryGetValue(r.GetGuid(0), out var d) ? d : stats[r.GetGuid(0)] = new())[r.GetString(1)] = r.GetString(2);
        foreach (var (table, owner_col, parent) in new[] { ("unit_property", "unit_id", "unit u on u.id = s.unit_id where u.army_id"), ("slot_property", "slot_id", "model_slot m on m.id = s.slot_id join management.unit u on u.id = m.unit_id where u.army_id"), ("weapon_property", "weapon_id", "weapon w on w.id = s.weapon_id join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id") })
        await using (var cmd = new NpgsqlCommand($"select s.{owner_col}, s.name, s.parameter from management.{table} s join management.{parent} = $1 order by s.ctid", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (props.TryGetValue(r.GetGuid(0), out var l) ? l : props[r.GetGuid(0)] = new()).Add(new PropertyInstance(r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2)));
        IReadOnlyDictionary<string, string> S(Guid id) => stats.TryGetValue(id, out var d) ? d : new Dictionary<string, string>();
        IReadOnlyList<PropertyInstance> P(Guid id) => props.TryGetValue(id, out var l) ? l : [];

        var weapons = new Dictionary<Guid, List<Weapon>>();
        await using (var cmd = new NpgsqlCommand("select w.id, w.slot_id, w.name from management.weapon w join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id = $1 order by w.ctid", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (weapons.TryGetValue(r.GetGuid(1), out var l) ? l : weapons[r.GetGuid(1)] = new()).Add(new Weapon(r.GetGuid(0), r.GetString(2), S(r.GetGuid(0)), P(r.GetGuid(0))));
        var slots = new Dictionary<Guid, List<ModelSlot>>();
        await using (var cmd = new NpgsqlCommand("select m.id, m.unit_id, m.ordinal, m.name, m.base_shape, m.base_a, m.base_b, m.height, m.asset_id from management.model_slot m join management.unit u on u.id = m.unit_id where u.army_id = $1 order by m.ordinal", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                (slots.TryGetValue(r.GetGuid(1), out var l) ? l : slots[r.GetGuid(1)] = new()).Add(new ModelSlot(r.GetGuid(0), r.GetInt32(2), r.GetString(3), S(r.GetGuid(0)), P(r.GetGuid(0)),
                    new Base(EnumFrom<BaseShape>(r.GetString(4)), r.GetDouble(5), r.GetDouble(6)), r.GetDouble(7), NullableGuid(r, 8), weapons.TryGetValue(r.GetGuid(0), out var ws) ? ws : []));
        var units = new List<Unit>();
        await using (var cmd = new NpgsqlCommand("select id, ordinal, name from management.unit where army_id = $1 order by ordinal", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) units.Add(new Unit(r.GetGuid(0), r.GetInt32(1), r.GetString(2), S(r.GetGuid(0)), P(r.GetGuid(0)), slots.TryGetValue(r.GetGuid(0), out var ss) ? ss : []));
        return new OwnedArmy(new Army(armyId, name, system, source, units), owner);
    }
}
