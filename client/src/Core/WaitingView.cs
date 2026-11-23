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
    public const string Title = "Deployment starts once every team has a player and an army.";

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

    /// <summary>The quiet band over the waiting columns.</summary>
    public const string Band = "Deployment waits for every team";

    /// <summary>
    /// Spec 2026-11-07-ux-redesign-1-client-shell.md → Waiting for players: the compact team columns in the decision slot, a column per team by
    /// ordinal, its state from the wait (Ready, Needs a player, Needs an army), its members and armies as the lobby names them, and inside it the
    /// same Join or army picker the lines carry.
    /// </summary>
    public static IReadOnlyList<WaitingColumn> Columns(GameInfo game, IReadOnlyList<TeamWaiting> waiting, Guid? participantId)
    {
        var lines = Of(game, waiting, participantId);
        var byTeam = waiting.Select((w, i) => (w, Line: lines[i])).ToDictionary(x => x.w.TeamId);
        return TeamColumns.Of(game, participantId, isCreator: false, compact: true).Where(c => c.TeamId is not null).Select(c =>
        {
            var state = byTeam.TryGetValue(c.TeamId!.Value, out var wait) ? (wait.w.HasPlayer ? ColumnState.NeedsArmy : ColumnState.NeedsPlayer) : ColumnState.Ready;
            return new WaitingColumn(c.Ordinal, c.Title, state, TeamColumns.StateText(state), c.Members.Select(m => (m.Name, m.Army)).ToList(), wait.Line?.Join, wait.Line?.Army);
        }).ToList();
    }

    /// <summary>Whether the columns drawn as <paramref name="shown"/> rebuild for <paramref name="now"/>: only when something they show changed.</summary>
    public static bool Changed(IReadOnlyList<WaitingColumn>? shown, IReadOnlyList<WaitingColumn> now) =>
        shown is null || shown.Count != now.Count || shown.Zip(now).Any(p => p.First with { Members = [] } != p.Second with { Members = [] } || !p.First.Members.SequenceEqual(p.Second.Members));
}

/// <summary>One waiting column: the team, its state, each member's name and army line, and what this client may do in it.</summary>
public sealed record WaitingColumn(int Ordinal, string Title, ColumnState State, string StateText, IReadOnlyList<(string Name, string? Army)> Members,
    TeamChoice? Join, ArmySeat? Army);
