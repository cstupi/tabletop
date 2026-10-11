using Tabletop.GameServer.Effects;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// A rolling step and the step after it that decides from the dice as they stand (opr-sequences → Dice edits) read one live set
/// between them. A granted instance is consumed where it is read, so the rolling step keeps back every grant the deciding step will
/// read, with the rest of that spell's grants, and the deciding step settles the live set the roll was planned against.
/// </summary>
public static class GfDeferred
{
    /// <summary>`spent` without the GrantRemoveds that `reads`, run on a fresh live set of the same interaction, would consume.</summary>
    public static IReadOnlyList<StateChange> Leave(IReadOnlyList<StateChange> spent, Interaction ix, Action<LiveSet> reads)
    {
        var later = LiveProperties.Settle(ix);
        reads(later);
        var kept = RollEffects.Consume(later).ToHashSet();
        return spent.Where(c => !kept.Contains(c)).ToList();
    }
}
