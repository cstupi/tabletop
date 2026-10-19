using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One team deployment waits on: what everyone reads, and what this client may do about it.</summary>
public sealed record WaitingLine(string Text, TeamChoice? Join, ArmySeat? Army);

/// <summary>
/// The waiting panel (game-flow spec → Waiting for players): setup has ended and deployment waits while a team has no player or no army. Everyone
/// reads one line per team; any participant may join a team no one holds (one more team when it already plays one), and a player on a team with
/// no army picks it. A watcher (admin) is told and offered nothing.
/// </summary>
public static class WaitingView
{
    public const string Title = "Setup is over. Deployment begins once every team has a player and an army.";

    public static IReadOnlyList<WaitingLine> Of(GameInfo game, IReadOnlyList<TeamWaiting> waiting, Guid? participantId)
    {
        var seats = Seats.Mine(game, participantId);
        return waiting.Select(w => new WaitingLine(w.Text,
            !w.HasPlayer && seats.Count > 0 ? new TeamChoice(w.TeamId, $"Join Team {w.Ordinal}", true, "Take this team and bring an army to it.") : null,
            w.HasPlayer && seats.Any(s => s.TeamId == w.TeamId) ? new ArmySeat(w.TeamId, $"Army for Team {w.Ordinal}", null) : null)).ToList();
    }

    /// <summary>Whether the panel showing <paramref name="shown"/> rebuilds for <paramref name="now"/>. A game or board update that leaves the lines
    /// as they are keeps the panel's controls, so an open army dropdown stays open; a picked army that reaches the board changes them.</summary>
    public static bool Changed(IReadOnlyList<WaitingLine>? shown, IReadOnlyList<WaitingLine> now) => shown is null || !shown.SequenceEqual(now);
}
