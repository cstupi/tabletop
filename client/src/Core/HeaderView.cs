using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum OutlineState { Done, Current, Next }

/// <summary>How much the header's lines draw to fit its slot (UX redesign part 2 → Header): Full; Compact, the step strip's marks without
/// their words but for the current step's, the objectives as bare icons and the tiles without `VP`; Tight, also the round as its number
/// and the objectives on the second line.</summary>
public enum HeaderFit { Full, Compact, Tight }
public sealed record OutlineItem(string Label, OutlineState State);

/// <summary>
/// The header's three lines (in-game UX spec → Header): round, turn and scores; the active unit, its action and the step strip; the
/// waiting line. Result is set once the log has ended the game.
/// </summary>
public sealed record HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action,
    IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result)
{
    /// <summary>The first line: "Round 4 · Team 1's turn", or "Round 4" when no team has the turn; before the first round the phase and turn
    /// ("Deployment · Team 2's turn"), else "Before round 1".</summary>
    public string TopLine => Round == 0 ? (Turn.Length == 0 ? "Before round 1" : RosterView.Words(Turn)) : Turn.Length == 0 ? $"Round {Round}" : $"Round {Round} · {Turn}";
}

/// <summary>A team's tile on the header's first line (UX redesign part 2 → Header): its score, its units in play and how many of them have
/// acted this round (one dot each, filled when acted; past twelve the count), the turn mark when it is the active team, the player icon when
/// it is one of mine.</summary>
public sealed record TeamTile(Guid TeamId, int Ordinal, string Name, int Score, int Acted, int InPlay, bool Acting, bool Mine)
{
    public const int MaxDots = 12;
    public bool Dots => InPlay <= MaxDots;
    public string CountText => $"{Acted}/{InPlay}";
}

/// <summary>The header's third line: Mine when an open Ask waits on a team of mine (`Your move`, in that team's colour); else the first
/// team waited on, whose band leads the line, and what each waits for.</summary>
public sealed record WaitLine(bool Mine, int Ordinal, string Text);

public static class HeaderView
{
    /// <summary>One tile per team in ordinal order. Units in play as NotActivated counts them; acted is those with an action this round.</summary>
    public static IReadOnlyList<TeamTile> Tiles(Projection p, GameInfo game, Names n, IReadOnlyCollection<Guid> mine) =>
        game.Teams.OrderBy(t => t.Ordinal).Select(t =>
        {
            var units = p.Units.Values.Where(u => game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id)
                && p.Models.Values.Any(m => m.UnitId == u.Id && InPlay(p, m.Location))).ToList();
            return new TeamTile(t.Id, t.Ordinal, n.Team(t.Id), p.Scores.GetValueOrDefault(t.Id), units.Count(u => u.Actions.Count > 0), units.Count,
                p.Scheduler.ActiveTeamId == t.Id, mine.Contains(t.Id));
        }).ToList();

    /// <summary>The round on the header: `Round 2`; before round 1 the phase (`Deployment`), else `Before round 1`.</summary>
    public static string RoundText(int round, string? phase, HeaderFit fit = HeaderFit.Full) =>
        round > 0 ? fit == HeaderFit.Tight ? $"{round}" : $"Round {round}" : phase is { Length: > 0 } ? RosterView.Words(phase) : "Before round 1";

    /// <summary>A step of the strip: its mark (✓ done, ● current, ○ to come) and label; past Full only the current step keeps its label.</summary>
    public static string StepText(OutlineItem item, HeaderFit fit)
    {
        var mark = item.State switch { OutlineState.Done => "✓", OutlineState.Current => "●", _ => "○" };
        return fit == HeaderFit.Full || item.State == OutlineState.Current ? $"{mark} {item.Label}" : mark;
    }

    /// <summary>The next tighter fit, or null past Tight.</summary>
    public static HeaderFit? Tighter(HeaderFit fit) => fit == HeaderFit.Tight ? null : fit + 1;

    static readonly string[] ActionIcons = ["hold", "advance", "rush", "charge"];
    /// <summary>The theme icon for an activation's action word (the option label after its colon, or the frame's action): hold, advance, rush,
    /// charge; any other word draws the activation icon.</summary>
    public static string ActionIcon(string? action) =>
        action is not null && ActionIcons.FirstOrDefault(i => string.Equals(i, action.Trim(), StringComparison.OrdinalIgnoreCase)) is { } icon ? icon : "activation";

    /// <summary>The header's waiting line (WaitLine); null when no open Ask waits on anyone.</summary>
    public static WaitLine? Wait(Projection p, GameInfo game, Names n, IReadOnlyCollection<Guid> mine)
    {
        var waits = p.OpenAsks.Values.OrderBy(a => a.Ask.Id)
            .SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t)).Select(t => (Team: t, What: DecisionCard.Doing(a.Ask, p, n)))).Distinct().ToList();
        if (waits.Count == 0) return null;
        int Ordinal(Guid t) => game.Teams.FirstOrDefault(x => x.Id == t)?.Ordinal ?? 0;
        if (waits.FirstOrDefault(w => mine.Contains(w.Team)) is { Team: var my } && mine.Contains(my))
            return new WaitLine(true, Ordinal(my), mine.Count > 1 ? $"Your move · {n.Team(my)}" : "Your move");
        return new WaitLine(false, Ordinal(waits[0].Team), DecisionCard.WaitingFor(string.Join(", ", waits.Select(w => $"{n.Team(w.Team)} · {w.What}"))));
    }

    /// <summary>A team's score in the rulebook's word (p.26: "players get 1 VP for each objective marker they control").</summary>
    public static string ScoreText(int score) => $"{score} VP";

    /// <summary>A team's units in play not yet activated this round (HeaderView.NotActivated), shown from round 1 while playing.</summary>
    public static string NotActivatedText(int count) => count == 0 ? "all activated" : $"{count} not activated";

    /// <summary>The rule set the game is played under, by its name; a game read from a source with no name shows its rules id.</summary>
    public static string RuleSetLine(GameInfo game) => game.RuleSet ?? game.GameRules;

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

    /// <summary>Per team: units in play (a model on the table, or inside a container on the table: p.15 "units may enter/exit by using any move
    /// action") with no action recorded this round. Who may activate is the scheduler's, not this count's.</summary>
    public static IReadOnlyDictionary<Guid, int> NotActivated(Projection p, GameInfo game) => game.Teams.ToDictionary(t => t.Id, t => p.Units.Values.Count(u =>
        u.Actions.Count == 0 && game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id) && p.Models.Values.Any(m => m.UnitId == u.Id && InPlay(p, m.Location))));

    static bool InPlay(Projection p, Location at) =>
        at is OnTable || at is Inside { ContainerId: var c } && p.Models.GetValueOrDefault(c)?.Location is OnTable;

    /// <summary>Objective markers in setup's order, with their controller (Projection.Control); null is neutral.</summary>
    public static IReadOnlyList<(Guid MarkerId, Guid? TeamId)> Objectives(Projection p) =>
        p.Board.Table.Markers.Select(m => (m.Id, p.Control.TryGetValue(m.Id, out var t) ? t : (Guid?)null)).ToList();

    /// <summary>"Orcs · Charge"; led by "Team 2 activating: " when the actor is not my team's (a watcher has none), at Full only: past it the
    /// acting team's tile carries the ▶.</summary>
    public static string? ActivationLine(Projection p, GameInfo game, Names n, Guid? myTeam, HeaderFit fit = HeaderFit.Full)
    {
        if (ActionFrame(p)?.Context is not { ActorUnitId: { } unit } c) return null;
        var line = c.Action is { } a ? $"{n.Unit(unit)} · {a}" : n.Unit(unit);
        var team = p.Units.GetValueOrDefault(unit) is { } u ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
        return fit == HeaderFit.Full && team is { } t && t != myTeam ? $"{n.Team(t)} activating: {line}" : line;
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
    public static string? Waiting(Projection p, Names n) => Waiting(p, n, (Guid?)null);

    /// <summary>As Waiting, with my team's entries first and named "Your move", so the player sees at once whether the game waits on them.</summary>
    public static string? Waiting(Projection p, Names n, Guid? myTeam) => Waiting(p, n, myTeam is { } t ? [t] : []);

    /// <summary>As Waiting, for a client holding several teams (self-play): each of mine is "Your move (Team 2)". Any player of a team answers
    /// its Asks (rulebook p.50 Team Games), so every Ask waiting on a team of mine is my move.</summary>
    public static string? Waiting(Projection p, Names n, IReadOnlyCollection<Guid> mine)
    {
        string Who(Guid t, bool myMove) => myMove ? (mine.Count > 1 ? $"Your move ({n.Team(t)})" : "Your move") : n.Team(t);
        var parts = p.OpenAsks.Values.OrderBy(a => a.Ask.Id)
            .SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t)).Select(t =>
            {
                var myMove = mine.Contains(t);
                return (Mine: myMove, Text: $"{Who(t, myMove)}: {a.Ask.Question.Replace('_', ' ')}");
            }))
            .Distinct().OrderByDescending(x => x.Mine).Select(x => x.Text).ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The header's line while deployment waits for players (WaitingForPlayers), one clause per team it waits on.</summary>
    public static string? WaitingForPlayers(IReadOnlyList<TeamWaiting> waiting) =>
        waiting.Count == 0 ? null : "Deployment waits: " + string.Join(" · ", waiting.Select(w => w.Text));

    /// <summary>The header's line for a client the server answers as a watcher (Session.IsWatching).</summary>
    public const string Watching = "Watching";
    /// <summary>The header's line in a replay (Session.Replaying).</summary>
    public const string Replaying = "Replay: the table at this point in the game";

    static string? Result(GameEnded? ended, Names n) => ended is null ? null : "Game over · " + Outcome(ended, n);

    /// <summary>One winner wins, several draw, none is no result.</summary>
    public static string Outcome(GameEnded ended, Names n) => ended.Winners.Count switch
    {
        0 => "No result",
        1 => $"{n.Team(ended.Winners[0])} wins",
        _ => "Draw",
    };
}
