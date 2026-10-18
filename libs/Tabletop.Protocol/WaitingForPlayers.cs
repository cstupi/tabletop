namespace Tabletop.Protocol;

/// <summary>A team deployment waits on: HasPlayer false, no participant holds it; true, a seat on it has no army with a unit. Unarmed names
/// those seats' players when another seat of the team has brought one (rulebook p.50 Team Games: every player of a side brings an army).</summary>
public sealed record TeamWaiting(Guid TeamId, int Ordinal, bool HasPlayer, IReadOnlyList<string>? Unarmed = null)
{
    public string Text => !HasPlayer ? $"Waiting for a player on Team {Ordinal}"
        : Unarmed is { Count: > 0 } names ? $"Team {Ordinal} waits for {string.Join(", ", names)} to pick an army" : $"Team {Ordinal} has no army";
}

/// <summary>
/// Ending setup always opens the game; deployment (the roll-off first) then waits while any team has no participant, or any seat of a team
/// has no army in play (game-flow spec → Waiting for players). The wait holds only while the log is empty: nothing has been rolled or deployed,
/// so a player who joins the empty team and picks an army adds that army to the board the log opens on. Server and client read it the same way.
/// </summary>
public static class WaitingForPlayers
{
    /// <summary>Every team that holds deployment, by ordinal; empty when none does.</summary>
    public static IReadOnlyList<TeamWaiting> Teams(GameInfo info, Board board) =>
        info.Teams.OrderBy(t => t.Ordinal).Select(t =>
        {
            var seats = info.Participants.Where(p => p.TeamId == t.Id).ToList();
            if (seats.Count == 0) return new TeamWaiting(t.Id, t.Ordinal, false);
            var unarmed = seats.Where(s => !board.Armies.Any(a => a.ParticipantId == s.Id && a.Snapshot.Units.Count > 0)).ToList();
            if (unarmed.Count == 0) return null;
            return new TeamWaiting(t.Id, t.Ordinal, true, unarmed.Count == seats.Count ? null : unarmed.Select(s => s.Name).ToList());
        }).OfType<TeamWaiting>().ToList();

    /// <summary>The game is playing, its log is still empty, and some team waits.</summary>
    public static bool Holds(GameInfo info, Board? board, long lastSeq) =>
        info.Status == GameStatus.Playing && board is not null && lastSeq == 0 && Teams(info, board).Count > 0;
}
