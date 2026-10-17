using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The Undo button and Ctrl+Z (client UX spec → Undo): undo the last action in the game, whoever took it. An action is a live event with
/// an actor (an engine event has none) that is neither an Undone marker nor an AskClosed: the close carries the actor of the answer that
/// caused it, and is part of that answer. The engine events after it go with it. The target is the live seq
/// immediately before the action, 0 when none: the server accepts only a live seq. LostRolls: each live roll, as the combat log's line,
/// once, when it or a live reroll or hand edit of it is after the target, ordered by the earliest such seq.
/// </summary>
public sealed record UndoPlan(long ToSeq, GameEvent Action, string Tooltip, IReadOnlyList<string> LostRolls)
{
    public static UndoPlan? Of(IReadOnlyList<GameEvent> log, Names n) => Of(log, n, null);

    /// <summary>With the combat log already built over this log (CombatLogState), so the plan does not build it again.</summary>
    public static UndoPlan? Of(IReadOnlyList<GameEvent> log, Names n, IReadOnlyList<LogEntry>? entries)
    {
        var live = EventApplier.EffectiveLog(log);
        var at = -1;
        for (var i = live.Count - 1; i >= 0 && at < 0; i--) if (live[i].Actor is not null && live[i].Payload is not (Undone or AskClosed)) at = i;
        if (at < 0) return null;
        var to = at == 0 ? 0 : live[at - 1].Seq;
        return new UndoPlan(to, live[at], "Undo: " + LogText.Describe(live[at], n), Lost(entries ?? CombatLog.Build(log, n), to));
    }

    static IReadOnlyList<string> Lost(IReadOnlyList<LogEntry> entries, long to)
    {
        var lost = new List<(long Seq, string Text)>();
        foreach (var e in CombatLog.Flatten(entries))
        {
            if (e.Undone) continue;
            if (e is RollEntry r)
            {
                var seqs = r.Amendments.Where(a => !a.Undone).Select(a => a.Seq).Prepend(r.Seq).Where(s => s > to).ToList();
                if (seqs.Count > 0) lost.Add((seqs.Min(), r.Collapsed(CombatLog.Pip)));   // the line already ends with its live amendments
            }
            else if (e is LineEntry { Event.Payload: DiceRerolled or DiceEdited } line && line.Seq > to) lost.Add((line.Seq, line.Text));
        }
        return lost.OrderBy(x => x.Seq).Select(x => x.Text).ToList();
    }
}
