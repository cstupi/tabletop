using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The Undo button and Ctrl+Z (client UX spec → Undo): undo the last action in the game, whoever took it. An action is a live event with
/// an actor (an engine event has none) that is not an Undone marker; the engine events after it go with it. The target is the live seq
/// immediately before the action, 0 when none: the server accepts only a live seq. LostRolls: each live roll after the target in the
/// combat log's collapsed words, and each live reroll or hand edit after it of a roll at or before it.
/// </summary>
public sealed record UndoPlan(long ToSeq, GameEvent Action, string Tooltip, IReadOnlyList<string> LostRolls)
{
    public static UndoPlan? Of(IReadOnlyList<GameEvent> log, Names n)
    {
        var live = EventApplier.EffectiveLog(log);
        var at = -1;
        for (var i = live.Count - 1; i >= 0 && at < 0; i--) if (live[i].Actor is not null && live[i].Payload is not Undone) at = i;
        if (at < 0) return null;
        var to = at == 0 ? 0 : live[at - 1].Seq;
        return new UndoPlan(to, live[at], "Undo: " + LogText.Describe(live[at], n), Lost(log, n, to));
    }

    static IReadOnlyList<string> Lost(IReadOnlyList<GameEvent> log, Names n, long to)
    {
        var lost = new List<(long Seq, string Text)>();
        foreach (var e in CombatLog.Build(log, n).SelectMany(e => e is ActivationBlock b ? b.Entries : (IEnumerable<LogEntry>)[e]))
        {
            if (e.Undone) continue;
            if (e is RollEntry r)
            {
                if (r.Seq > to) lost.Add((r.Seq, r.Collapsed(CombatLog.Pip)));
                else lost.AddRange(r.Amendments.Where(a => !a.Undone && a.Seq > to).Select(a => (a.Seq, $"{r.Title} · {a.Text}")));
            }
            else if (e is LineEntry { Event.Payload: DiceRerolled or DiceEdited } line && line.Seq > to) lost.Add((line.Seq, line.Text));
        }
        return lost.OrderBy(x => x.Seq).Select(x => x.Text).ToList();
    }
}
