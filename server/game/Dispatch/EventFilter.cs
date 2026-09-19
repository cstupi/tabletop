using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>The one per-viewer stream: a sealed window's answers are withheld from other teams until ask_closed reveals them.</summary>
public static class EventFilter
{
    public static IReadOnlySet<Guid> SealedOpenAsks(Projection p) => p.OpenAsks.Values.Where(a => a.Ask.Sealed).Select(a => a.Ask.Id).ToHashSet();

    /// <summary>
    /// An ask is sealed-open while its sealed ask_opened stands and no ask_closed stands after it, so the walk tracks the seq
    /// that opened and the seq that closed each one: an Undone discards every event with Seq &gt; ToSeq (EventApplier.EffectiveLog),
    /// which drops the opens and closes it reaches. The seeded ids opened before this batch, so nothing in it undoes them.
    /// </summary>
    public static IReadOnlyList<GameEvent> ForViewer(IReadOnlyList<GameEvent> events, Guid? viewerTeamId, IReadOnlySet<Guid> sealedOpenBefore)
    {
        var openedAt = sealedOpenBefore.ToDictionary(id => id, _ => long.MinValue);
        var closedAt = new Dictionary<Guid, long>();
        var result = new List<GameEvent>(events.Count);
        foreach (var e in events)
        {
            switch (e.Payload)
            {
                case AskOpened o when o.Ask.Sealed: openedAt[o.Ask.Id] = e.Seq; closedAt.Remove(o.Ask.Id); break;
                case AskClosed c: closedAt[c.AskId] = e.Seq; break;
                case Undone u:
                    foreach (var id in openedAt.Where(x => x.Value > u.ToSeq).Select(x => x.Key).ToList()) openedAt.Remove(id);
                    foreach (var id in closedAt.Where(x => x.Value > u.ToSeq).Select(x => x.Key).ToList()) closedAt.Remove(id);
                    break;
                case AskAnswered a when a.TeamId != viewerTeamId && openedAt.ContainsKey(a.AskId) && !closedAt.ContainsKey(a.AskId):
                    result.Add(e with { Payload = a with { Answer = null } });
                    continue;
            }
            result.Add(e);
        }
        return result;
    }
}
