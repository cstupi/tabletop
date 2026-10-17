using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.7's contact clauses for a move answer, read as the steps apply it and judged at the rested landing. "Or be placed in physically
    /// impossible locations": move_overlap, a landing on any other model's base, the moving unit's own landings included. "May never move
    /// through other models or units (friendly or enemy), even if they are taking a Charge action": move_through_models, the base's true outline swept
    /// from its start across a model the answer does not move, less one it already overlaps (Collision.Crosses); not for a region that PassesModels (p.13 Flying,
    /// Aircraft). "May never be within 1” of models from other units, unless they are taking a Charge action": move_separation, the region's
    /// Separation, which a Charge's regions leave null. A game without melee separation (p.10: "players may agree not to separate units by 1" ...
    /// units can still freely activate, move, shoot, charge, etc. despite being in contact") holds no model to the 1" from a unit the mover was
    /// in contact with, within that 1", when the move started.
    /// </summary>
    public static AnswerBreachFound? ContactBreach(GameState state, MoveRegion region, Destinations answer)
    {
        var moves = GfAnswers.Moves(state, region, answer);
        var exempt = region.Separation is { } gap && !GfOptions.Of(state).MeleeSeparation ? InContact(state, moves, gap) : [];
        return Contact(state, moves, region.Separation, region.PassesModels, exempt);
    }

    /// <summary>The other units with a model within gap of a moving model where it stands now.</summary>
    static HashSet<Guid> InContact(GameState state, IReadOnlyList<ModelRelocation> moves, double gap)
    {
        var p = state.Projection;
        var movers = moves.Where(m => p.Models.ContainsKey(m.ModelId)).Select(m => Battlefield.Placed(state, m.ModelId)).OfType<PlacedModel>().ToList();
        var units = moves.Where(m => p.Models.ContainsKey(m.ModelId)).Select(m => p.Models[m.ModelId].UnitId).ToHashSet();
        return p.Models.Values.Where(o => !units.Contains(o.UnitId) && Battlefield.Placed(state, o.Id) is { } at
                && movers.Any(m => Distance.EdgeToEdge(m, at) < gap - 1e-6)).Select(o => o.UnitId).ToHashSet();
    }

    /// <summary>A hand placement (IGameRules.PlacementBreach) is held to "physically impossible locations" only.</summary>
    public static AnswerBreachFound? OverlapBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => Contact(state, moves, null, passesModels: true, new HashSet<Guid>());

    static AnswerBreachFound? Contact(GameState state, IReadOnlyList<ModelRelocation> answered, double? separation, bool passesModels, IReadOnlySet<Guid> separationExempt)
    {
        var p = state.Projection;
        var moves = answered.Where(m => m.To is OnTable && p.Models.ContainsKey(m.ModelId)).DistinctBy(m => m.ModelId).ToList();
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        var landed = moves.ToDictionary(m => m.ModelId, m => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement)));
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable)
            .Select(x => (x.Id, x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var everyone = standing.Concat(landed.Select(kv => (Id: kv.Key, p.Models[kv.Key].UnitId, At: kv.Value))).ToList();
        foreach (var m in moves.Where(m => p.Models[m.ModelId].Location is not OnTable f
                     || f.Placement.Position.Horizontal.DistanceTo(landed[m.ModelId].Placement.Position.Horizontal) > 1e-6))
        {                                                                                  // a model answered where it stands has not moved
            var (name, unit, at) = (p.Models[m.ModelId].Slot.Name, p.Models[m.ModelId].UnitId, landed[m.ModelId]);
            if (everyone.Any(o => o.Id != m.ModelId && Overlap.Bases(at, o.At)))
                return new(GfNames.MoveOverlapRule, $"{name} would be placed on another model's base: models may never be placed in physically impossible locations (p.7)");
            if (!passesModels && Battlefield.Placed(state, m.ModelId) is { } start && Collision.Crosses(start, at.Placement.Position.Horizontal, standing.Select(o => o.At).ToList()))
                return new(GfNames.MoveThroughModelsRule, $"{name} would move through another model: models may never move through other models or units (p.7)");
            if (separation is { } gap && everyone.Any(o => o.UnitId != unit && !separationExempt.Contains(o.UnitId) && Distance.EdgeToEdge(at, o.At) < gap - 1e-6))
                return new(GfNames.MoveSeparationRule, $"{name} would end within {gap:0.#}\" of another unit's model: models may never be within 1\" of models from other units (p.7)");
        }
        return null;
    }
}
