using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The teams this client plays (game-flow spec → One account, several teams): every participant row of the account Welcome named, one per team,
/// and which of them it acts for now. One client plays both sides of a self-play game; two windows of one account each see every team's Asks.
/// </summary>
public static class Seats
{
    /// <summary>The account's rows: those sharing the account of the row Welcome named (an admin watcher's empty id names none).</summary>
    public static IReadOnlyList<Participant> Mine(GameInfo game, Guid? participantId) =>
        game.Participants.FirstOrDefault(p => p.Id == participantId) is { } me ? game.Participants.Where(p => p.AccountId == me.AccountId).ToList() : [];

    /// <summary>The account's teams, by ordinal.</summary>
    public static IReadOnlyList<Guid> Teams(GameInfo game, Guid? participantId)
    {
        var held = Mine(game, participantId).Select(p => p.TeamId).OfType<Guid>().ToHashSet();
        return game.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).Where(held.Contains).ToList();
    }

    /// <summary>
    /// The team this client acts for now: the team of the first open Ask (by id) addressed to one of mine that it has not answered, the first of
    /// mine by ordinal; else the active team when it is mine; between turns the team whose turn last started (from the log) when it is mine; else my first team. Null with no team. The decision box, the unit card, the roster
    /// and the camera all read it, so they follow the Ask from one team to the other.
    /// </summary>
    public static Guid? Acting(Projection? p, IReadOnlyList<Guid> mine, IReadOnlyList<GameEvent>? log = null)
    {
        if (mine.Count == 0) return null;
        if (mine.Count == 1 || p is null) return mine[0];
        foreach (var open in p.OpenAsks.Values.OrderBy(a => a.Ask.Id))
            foreach (var team in mine)
                if (open.Ask.Teams.Contains(team) && !open.Answers.ContainsKey(team)) return team;
        if (p.Scheduler.ActiveTeamId is { } active) return mine.Contains(active) ? active : mine[0];
        // Between turns (a turn ended, the next not yet started): the team that last had the turn, so nothing swings to my first team and back.
        return log is not null && EventApplier.EffectiveLog(log).LastOrDefault(e => e.Payload is TurnStarted) is { Payload: TurnStarted t } && mine.Contains(t.TeamId) ? t.TeamId : mine[0];
    }

    /// <summary>"Playing as Team 2" for a client holding several teams; null for one team or none.</summary>
    public static string? PlayingAs(GameInfo game, IReadOnlyList<Guid> mine, Guid? acting) =>
        mine.Count > 1 && game.Teams.FirstOrDefault(t => t.Id == acting) is { } team ? $"Playing as Team {team.Ordinal}" : null;
}
