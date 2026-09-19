using Npgsql;
using NpgsqlTypes;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>game.game_event: kind is GameEvent.Kind, payload is WireJson.Serialize(GameEvent) — seq, actor and warning ride inside the jsonb.</summary>
public sealed class PostgresEventLog(NpgsqlDataSource db) : IEventLog
{
    public async Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var e in events)
            {
                await using var cmd = new NpgsqlCommand("insert into game.game_event (game_id, seq, kind, payload) values ($1, $2, $3, $4)", conn, tx);
                cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(e.Seq); cmd.Parameters.AddWithValue(e.Kind);
                cmd.Parameters.Add(new NpgsqlParameter { Value = WireJson.Serialize(e), NpgsqlDbType = NpgsqlDbType.Jsonb });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            var last = await LastSeqAsync(gameId, ct);
            throw new DuplicateSequenceException(gameId, events.Select(e => e.Seq).Where(s => s <= last).DefaultIfEmpty(events[0].Seq).Min());
        }
    }

    public async Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct)
    {
        var result = new List<GameEvent>();
        await using var cmd = db.CreateCommand("select payload::text from game.game_event where game_id = $1 and seq > $2 order by seq");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(afterSeq);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(WireJson.Deserialize<GameEvent>(r.GetString(0)));
        return result;
    }

    public async Task<long> LastSeqAsync(Guid gameId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select coalesce(max(seq), 0) from game.game_event where game_id = $1");
        cmd.Parameters.AddWithValue(gameId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
