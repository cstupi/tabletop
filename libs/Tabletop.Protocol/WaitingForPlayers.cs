namespace Tabletop.Protocol;

/// <summary>A team deployment waits on: HasPlayer false, no participant holds it; true, no seat on it has an army with a unit.</summary>
public sealed record TeamWaiting(Guid TeamId, int Ordinal, bool HasPlayer)
{
    public string Text => HasPlayer ? $"Team {Ordinal} has no army" : $"Waiting for a player on Team {Ordinal}";
}

/// <summary>
/// Ending setup always opens the game; deployment (the roll-off first) then waits while any team has no participant, or no army with a unit
/// in play (game-flow spec → Waiting for players; owner ruling: "You need an army on your team."). A seat with no army is no wait: any player
/// of a team commands every unit of the team. The wait holds only while the log is empty: nothing has been rolled or deployed, so a player who
/// joins the empty team and picks an army adds that army to the board the log opens on. Server and client read it the same way.
/// </summary>
public static class WaitingForPlayers
{
    /// <summary>Every team that holds deployment, by ordinal; empty when none does.</summary>
    public static IReadOnlyList<TeamWaiting> Teams(GameInfo info, Board board) =>
        info.Teams.OrderBy(t => t.Ordinal).Select(t =>
        {
            var seats = info.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Id).ToHashSet();
            if (seats.Count == 0) return new TeamWaiting(t.Id, t.Ordinal, false);
            return board.Armies.Any(a => seats.Contains(a.ParticipantId) && a.Snapshot.Units.Count > 0) ? null : new TeamWaiting(t.Id, t.Ordinal, true);
        }).OfType<TeamWaiting>().ToList();

    /// <summary>The game is playing, its log is still empty, and some team waits.</summary>
    public static bool Holds(GameInfo info, Board? board, long lastSeq) =>
        info.Status == GameStatus.Playing && board is not null && lastSeq == 0 && Teams(info, board).Count > 0;
}
