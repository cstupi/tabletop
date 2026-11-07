using Npgsql;
using NpgsqlTypes;
using Tabletop.Content;
using Tabletop.Protocol;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Persistence;

public sealed partial class PostgresGameRows
{
    public async Task EndGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.game set status = 'ended', ended_at = now() where id = $1 and status = 'playing'");
        cmd.Parameters.AddWithValue(gameId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task WriteProgressAsync(Guid gameId, GameProgress progress, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.game set round = $2, acting_team = $3, waiting_on = $4, outcome = $5 where id = $1");
        cmd.Parameters.AddWithValue(gameId);
        cmd.Parameters.AddWithValue(progress.Round);
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)progress.ActingTeam ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Integer });
        cmd.Parameters.Add(new NpgsqlParameter { Value = progress.WaitingOn.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)progress.Outcome?.ToArray() ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RepinPropertiesAsync(Guid gameId, string hash, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.game set properties_hash = $2 where id = $1 and status <> 'ended'");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = db.CreateCommand("insert into management.team (id, game_id, ordinal) select $1, $2, coalesce(max(ordinal), 0) + 1 from management.team where game_id = $2 returning ordinal");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId);
        return new Team(id, (int)(await cmd.ExecuteScalarAsync(ct))!);
    }

    public async Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.participant set team_id = $2 where id = $1");
        cmd.Parameters.AddWithValue(participantId); cmd.Parameters.AddWithValue((object?)teamId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.participant set army_id = $2 where id = $1");
        cmd.Parameters.AddWithValue(participantId); cmd.Parameters.AddWithValue((object?)armyId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddSeatAsync(Guid id, Guid gameId, Guid accountId, Guid teamId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("insert into management.participant (id, game_id, account_id, team_id) values ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(accountId); cmd.Parameters.AddWithValue(teamId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveSeatAsync(Guid participantId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("delete from management.participant where id = $1");
        cmd.Parameters.AddWithValue(participantId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task JoinArmyAsync(Guid gameId, Guid participantId, Guid armyId, ArmySnapshot snapshot, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            update management.participant p set army_id = $3, army_snapshot = $4 from management.game g
            where p.id = $1 and p.game_id = $2 and g.id = p.game_id and g.status = 'playing' and p.army_snapshot is null
            """);
        cmd.Parameters.AddWithValue(participantId); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(armyId);
        cmd.Parameters.Add(new NpgsqlParameter { Value = ContentJson.Serialize(snapshot), NpgsqlDbType = NpgsqlDbType.Jsonb });
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("the participant already brought an army, or the game is not playing");
    }

    public async Task SetConfigurationAsync(Guid gameId, SetConfiguration c, CancellationToken ct)
    {
        // game_rules follows the chosen properties row, as management copies it on create (management plan → Rows by writer: both are rewritten on set_configuration).
        // The seven configuration columns and nothing else: rng_seed is management's, written once at creation.
        await using var cmd = db.CreateCommand("update management.game set name = $2, properties_hash = $3, game_rules = (select game_rules from management.properties where content_hash = $3), scheduler_shape = $4, enforcement = $5, waits_between_steps = $6, optional_rules = $7::jsonb where id = $1");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(c.Name); cmd.Parameters.AddWithValue(c.PropertiesHash);
        cmd.Parameters.AddWithValue(EnumText(c.SchedulerShape)); cmd.Parameters.AddWithValue(EnumText(c.Enforcement)); cmd.Parameters.AddWithValue(c.WaitsBetweenSteps); cmd.Parameters.AddWithValue(OptionalRulesJson(c.OptionalRules));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("insert into management.table_state (id, game_id, name, width, length, texture_builtin, texture_asset_id) values ($1, $2, $3, $4, $5, $6, $7)", conn, tx))
        {
            cmd.Parameters.AddWithValue(draft.Id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue((object?)draft.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue(draft.Size.Width); cmd.Parameters.AddWithValue(draft.Size.Length);
            cmd.Parameters.AddWithValue((object?)draft.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)draft.Texture.AssetId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await WriteTreeAsync(conn, tx, draft, ct);
        await using (var cmd = new NpgsqlCommand("update management.game set status = 'setup' where id = $1 and status = 'lobby'", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("game is not in lobby");
        await tx.CommitAsync(ct);
    }

    public async Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("update management.table_state set name = $3, width = $4, length = $5, texture_builtin = $6, texture_asset_id = $7 where id = $1 and game_id = $2", conn, tx))
        {
            cmd.Parameters.AddWithValue(draft.Id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue((object?)draft.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue(draft.Size.Width); cmd.Parameters.AddWithValue(draft.Size.Length);
            cmd.Parameters.AddWithValue((object?)draft.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)draft.Texture.AssetId ?? DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("no draft row for this game");
        }
        await WriteTreeAsync(conn, tx, draft, ct);
        await tx.CommitAsync(ct);
    }

    public async Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("update management.table_state set width = $3, length = $4, texture_builtin = $5, texture_asset_id = $6 where id = $1 and game_id = $2", conn, tx))
        {
            cmd.Parameters.AddWithValue(snapshot.Id); cmd.Parameters.AddWithValue(gameId);
            cmd.Parameters.AddWithValue(snapshot.Size.Width); cmd.Parameters.AddWithValue(snapshot.Size.Length);
            cmd.Parameters.AddWithValue((object?)snapshot.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)snapshot.Texture.AssetId ?? DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("no draft row for this game");
        }
        await WriteTreeAsync(conn, tx, snapshot, ct);
        foreach (var (participantId, army) in snapshotsByParticipant)
        {
            await using var cmd = new NpgsqlCommand("update management.participant set army_snapshot = $2 where id = $1 and game_id = $3", conn, tx);
            cmd.Parameters.AddWithValue(participantId);
            cmd.Parameters.Add(new NpgsqlParameter { Value = army is null ? DBNull.Value : ContentJson.Serialize(army), NpgsqlDbType = NpgsqlDbType.Jsonb });
            cmd.Parameters.AddWithValue(gameId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new NpgsqlCommand("update management.game set status = 'playing' where id = $1 and status = 'setup'", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("game is not in setup");
        await tx.CommitAsync(ct);
    }

    /// <summary>Children are replaced whole: the draft is a value and one drop is one write. Every delete and insert goes in one
    /// NpgsqlBatch, one round trip, however many pieces the draft holds (security review M6).</summary>
    internal static async Task WriteTreeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, TableState draft, CancellationToken ct)
    {
        await using var batch = new NpgsqlBatch(conn, tx);
        void Add(string sql, params object?[] args)
        {
            var cmd = new NpgsqlBatchCommand(sql);
            foreach (var a in args) cmd.Parameters.Add(new NpgsqlParameter { Value = a ?? DBNull.Value });
            batch.BatchCommands.Add(cmd);
        }
        foreach (var table in new[] { "terrain_placement", "marker", "deployment_zone" }) Add($"delete from management.{table} where table_state_id = $1", draft.Id);
        foreach (var t in draft.Terrain)
        {
            Add("insert into management.terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
                t.Id, draft.Id, t.Mesh.Builtin, t.Mesh.AssetId, t.Position.X, t.Position.Y, t.Position.Z, t.Yaw.Degrees, t.Scale);
            foreach (var v in t.Volumes)
                Add($"insert into management.terrain_volume (id, terrain_placement_id, {VolumeColumns}) values ($1, $2, {string.Join(", ", Enumerable.Range(3, VolumeColumns.Split(", ").Length).Select(i => "$" + i))})",
                    [v.Id, t.Id, .. VolumeValues(v.Volume)]);
        }
        foreach (var m in draft.Markers)
            Add("insert into management.marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
                m.Id, draft.Id, EnumText(m.Kind), m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees, m.Diameter, m.Label, m.Visual.Builtin, m.Visual.AssetId);
        foreach (var z in draft.Zones)
            Add("insert into management.deployment_zone (id, table_state_id, shape, team_ordinal) values ($1, $2, $3, $4)", z.Id, draft.Id, z.Shape, z.TeamOrdinal);
        await batch.ExecuteNonQueryAsync(ct);
    }
}
