using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum OutlineState { Done, Current, Next }
public sealed record OutlineItem(string Label, OutlineState State);

/// <summary>
/// The header's three lines (in-game UX spec → Header): round, turn and scores; the active unit, its action and the step strip; the
/// waiting line. Result is set once the log has ended the game.
/// </summary>
public sealed record HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action,
    IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result);

public static class HeaderView
{
    public static HeaderModel Of(Projection p, GameInfo game, Names n)
    {
        var s = p.Scheduler;
        var turn = string.Join(" · ", new[] { s.Round == 0 ? s.Phase : null, s.ActiveTeamId is { } t ? $"{n.Team(t)}'s turn" : null }.OfType<string>());
        var scores = game.Teams.OrderBy(t => t.Ordinal).Select(t => (n.Team(t.Id), p.Scores.GetValueOrDefault(t.Id))).ToList();
        var frame = ActionFrame(p);
        return new HeaderModel(s.Round, turn, scores,
            frame?.Context.ActorUnitId is { } unit ? n.Unit(unit) : null, frame?.Context.Action,
            frame is null ? [] : Outline(frame), Waiting(p, n), Result(p.Ended, n));
    }

    /// <summary>The activation's top frame: the outermost one whose context names an action. Nested frames run under it.</summary>
    public static Frame? ActionFrame(Projection p) => p.Stack.FirstOrDefault(f => f.Context.Action is not null);

    /// <summary>Per team: units with a model on the table and no action recorded this round. Who may activate is the scheduler's, not this count's.</summary>
    public static IReadOnlyDictionary<Guid, int> NotActivated(Projection p, GameInfo game) => game.Teams.ToDictionary(t => t.Id, t => p.Units.Values.Count(u =>
        u.Actions.Count == 0 && game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id) && p.Models.Values.Any(m => m.UnitId == u.Id && m.Location is OnTable)));

    /// <summary>Objective markers, setup's then play's, with their controller (Projection.Control); null is neutral.</summary>
    public static IReadOnlyList<(Guid MarkerId, Guid? TeamId)> Objectives(Projection p) => p.Board.Table.Markers.Concat(p.Markers.Values)
        .Where(m => m.Kind == MarkerKind.Objective).DistinctBy(m => m.Id).Select(m => (m.Id, p.Control.TryGetValue(m.Id, out var t) ? t : (Guid?)null)).ToList();

    /// <summary>"Orcs · Charge"; led by "Team 2 activating: " when the actor is not my team's (a watcher has none).</summary>
    public static string? ActivationLine(Projection p, GameInfo game, Names n, Guid? myTeam)
    {
        if (ActionFrame(p)?.Context is not { ActorUnitId: { } unit } c) return null;
        var line = c.Action is { } a ? $"{n.Unit(unit)} · {a}" : n.Unit(unit);
        var team = p.Units.GetValueOrDefault(unit) is { } u ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
        return team is { } t && t != myTeam ? $"{n.Team(t)} activating: {line}" : line;
    }

    /// <summary>
    /// Every outline step up to the last one the frame has completed is done, the next is current, the rest are next. A frame
    /// pushed by a step completes nothing on this one, so the step it runs under stays current. No outline, no strip.
    /// </summary>
    public static IReadOnlyList<OutlineItem> Outline(Frame frame)
    {
        var completed = frame.Steps.Select(x => x.Step).ToHashSet(StringComparer.Ordinal);
        var last = -1;
        for (var i = 0; i < frame.Outline.Count; i++)
            if (completed.Contains(frame.Outline[i].Step)) last = i;
        return frame.Outline.Select((o, i) => new OutlineItem(o.Label,
            i <= last ? OutlineState.Done : i == last + 1 ? OutlineState.Current : OutlineState.Next)).ToList();
    }

    /// <summary>Who the game waits on and for what: one entry per team that has not answered an open Ask, in Ask order.</summary>
    public static string? Waiting(Projection p, Names n)
    {
        var parts = p.OpenAsks.Values.OrderBy(a => a.Ask.Id)
            .SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t)).Select(t => $"{n.Team(t)}: {a.Ask.Question.Replace('_', ' ')}"))
            .Distinct().ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    static string? Result(GameEnded? ended, Names n) => ended is null ? null : "Game over · " + Outcome(ended, n);

    /// <summary>One winner wins, several draw, none is no result.</summary>
    public static string Outcome(GameEnded ended, Names n) => ended.Winners.Count switch
    {
        0 => "No result",
        1 => $"{n.Team(ended.Winners[0])} wins",
        _ => "Draw",
    };
}
