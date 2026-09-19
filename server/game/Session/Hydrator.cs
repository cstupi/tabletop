using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed class GameNotFoundException(Guid gameId) : Exception($"No game {gameId}.");
public sealed class GameRowsInconsistentException(Guid gameId, string what) : Exception($"Game {gameId}: {what}.");

/// <summary>The one path: rows, then the draft or the snapshot, then the log. A first join, a rejoin, a reconnect and a crashed task all come through here.</summary>
public static class Hydrator
{
    public static async Task<GameSession> HydrateAsync(Guid gameId, SessionServices services, CancellationToken ct)
    {
        var info = await services.Rows.ReadGameAsync(gameId, ct) ?? throw new GameNotFoundException(gameId);
        var rules = GameRulesRegistry.Resolve(info.GameRules);
        if (info.Status == GameStatus.Lobby) return new GameSession(gameId, info, rules, null, null, null, services);

        var draft = await services.Rows.ReadDraftAsync(gameId, ct);
        // Ended is a flag, not a terminal state, and the last participant leaving ends a game that may never have left the
        // lobby (accounts -> Ending is a flag). That game has no table_state row and no log, so it hydrates boardless.
        if (draft is null)
        {
            if (info.Status == GameStatus.Ended) return new GameSession(gameId, info, rules, null, null, null, services);
            throw new GameRowsInconsistentException(gameId, $"status is {info.Status} but there is no table_state row");
        }
        if (info.Status == GameStatus.Setup) return new GameSession(gameId, info, rules, draft, null, null, services);

        var board = new Board(draft, await services.Rows.ReadSnapshotsAsync(gameId, ct));
        var applier = new EventApplier(board, info);
        applier.ApplyAll(await services.Log.ReadAsync(gameId, 0, ct));
        var engine = services.Engines.Create(rules, info);
        return new GameSession(gameId, info, rules, null, new GameState(info, board, applier, rules), engine, services);
    }
}
