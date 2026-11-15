using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The unit card's cover shield: the units in cover where they stand.</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>
    /// p.11, 3.6.0 p.12 (the same words): "If the majority of models in a unit are fully inside a piece of Cover terrain or behind a sight blocker
    /// (for multi-model units), or that are mostly inside Cover terrain or behind sight blockers (for single-model units), they get +1 to Defense
    /// rolls when blocking hits from shooting attacks." The terrain half alone (`GfRolls.Cover` with the sight half dropped): being behind a sight
    /// blocker depends on the shooter. Only volumes declared Cover count; 3.6.0 Dense (see in and out) gives sight rules, not cover.
    /// </summary>
    public IReadOnlyList<Guid> InCover(GameState state) =>
        state.Projection.Units.Keys.Where(u => Lookup.LivingModels(state, u).Count > 0
            && GfRolls.Cover(state, u, [], ignoresSightObstructions: true).Majority).ToList();
}
