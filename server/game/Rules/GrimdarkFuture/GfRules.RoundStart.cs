using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The round boundary: the round-start rolls of rules read "at the beginning of the round" (army-book rules spec, batch 6), then the Ambushers (p.13).</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>
    /// Once a round, before the Ambushers: one `rally` sequence per Shaken unit holding Steadfast, Battleborn or Honor Code ("If a unit … is Shaken at the
    /// beginning of the round, roll one die. On a 4+, it stops being Shaken"). Started together, they run before anything else of the round.
    /// </summary>
    public HookResult RoundBoundaryStep(GameState state, PropertySet props, int round, IReadOnlyList<Guid> order)
    {
        if (round >= 1)
        {
            var since = SchedulerLog.Of(state).SinceRound.Select(e => e.Payload).ToList();
            if (!since.Any(p => p is SequenceStarted { Sequence: GfNames.RallySeq }) && GfMarkers.Rallying(state, props) is { Count: > 0 } rallying)
                return new HookStartSequences(rallying.Select(r => new SequenceStart(GfNames.RallySeq, new SequenceContext(r.Unit, [], [], r.Rule))).ToList());
            if (state.Projection.Stack.Any(f => f.Sequence == GfNames.RallySeq)) return new HookWaiting();
        }
        return AmbushBoundary(state, props, round, order);
    }
}
