using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>The one per-viewer stream: a sealed window's answers are withheld from other teams until ask_closed reveals them.</summary>
public static class EventFilter
{
    public static IReadOnlySet<Guid> SealedOpenAsks(Projection p) => p.OpenAsks.Values.Where(a => a.Ask.Sealed).Select(a => a.Ask.Id).ToHashSet();

    public static IReadOnlyList<GameEvent> ForViewer(IReadOnlyList<GameEvent> events, Guid? viewerTeamId, IReadOnlySet<Guid> sealedOpenBefore)
    {
        var sealedOpen = new HashSet<Guid>(sealedOpenBefore);
        var result = new List<GameEvent>(events.Count);
        foreach (var e in events)
        {
            switch (e.Payload)
            {
                case AskOpened o when o.Ask.Sealed: sealedOpen.Add(o.Ask.Id); result.Add(e); break;
                case AskClosed c: sealedOpen.Remove(c.AskId); result.Add(e); break;
                case AskAnswered a when sealedOpen.Contains(a.AskId) && a.TeamId != viewerTeamId:
                    result.Add(e with { Payload = a with { Answer = null } }); break;
                default: result.Add(e); break;
            }
        }
        return result;
    }
}
