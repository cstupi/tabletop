using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>p.6: objectives are seized at the end of each round. Round 0, the phases before round one, has no end.</summary>
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => round < 1 ? [] : GfObjectives.Seize(state, properties);
}
