using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.7's contact clauses for a move answer, read as the steps apply it, the landing judged where it rests. "Or be placed in physically
    /// impossible locations": move_overlap, a landing on any other model's base, the moving unit's own landings included. "May never be within 1” of
    /// models from other units, unless they are taking a Charge action": move_separation, a landing within the region's Separation of another
    /// unit's model (Distance.BodyToBody), which a Charge's regions leave null; judged before the path, so a landing too close is never read as a
    /// path through. Then the path (Route; owner ruling: both clauses hold along the whole path): "may never move through other models or units
    /// (friendly or enemy), even if they are taking a Charge action": move_through_models, when no path round every other unit's perimeter and the
    /// models of the mover's own unit and a charge's target fits the move but one through them does; move_separation, when one fits round the
    /// perimeters only within the Separation of a unit. Neither for a region that PassesModels (p.13 Flying, Aircraft). Owner ruling: movement takes
    /// elevation into account, so all are judged in three dimensions and a model can cross above a model on a lower level. A game without melee
    /// separation (p.10: "players may agree not to separate units by 1" ... units can still freely activate, move, shoot, charge, etc. despite being
    /// in contact") holds no model to the 1" from a unit the mover was in contact with, within that 1", when the move started (the path too: a model
    /// starting within the separation of a unit is held only to its perimeter).
    /// </summary>
    public static AnswerBreachFound? ContactBreach(GameState state, MoveRegion region, Destinations answer) => ContactBreach(state, region, answer, TargetUnits(state, region));

    /// <summary>ContactBreach with a charge's target units given (`bases`): a charge judged without its draw-in has no ContactWith.</summary>
    internal static AnswerBreachFound? ContactBreach(GameState state, MoveRegion region, Destinations answer, IReadOnlyList<Guid>? bases)
    {
        var moves = GfAnswers.Moves(state, region, answer);
        var exempt = region.Separation is { } gap && !GfOptions.Of(state).MeleeSeparation ? InContact(state, moves, gap) : [];
        return Contact(state, moves, region.Separation, region.PassesModels, exempt, region, bases);
    }

    /// <summary>The other units with a model within gap of a moving model where it stands now.</summary>
    static HashSet<Guid> InContact(GameState state, IReadOnlyList<ModelRelocation> moves, double gap)
    {
        var p = state.Projection;
        var movers = moves.Where(m => p.Models.ContainsKey(m.ModelId)).Select(m => Battlefield.Placed(state, m.ModelId)).OfType<PlacedModel>().ToList();
        var units = moves.Where(m => p.Models.ContainsKey(m.ModelId)).Select(m => p.Models[m.ModelId].UnitId).ToHashSet();
        return p.Models.Values.Where(o => !units.Contains(o.UnitId) && Battlefield.Placed(state, o.Id) is { } at
                && movers.Any(m => Distance.BodyToBody(m, at) < gap - 1e-6)).Select(o => o.UnitId).ToHashSet();
    }

    /// <summary>A hand placement (IGameRules.PlacementBreach) is held to "physically impossible locations" only.</summary>
    public static AnswerBreachFound? OverlapBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => Contact(state, moves, null, passesModels: true, new HashSet<Guid>(), null, null);

    static AnswerBreachFound? Contact(GameState state, IReadOnlyList<ModelRelocation> answered, double? separation, bool passesModels, IReadOnlySet<Guid> separationExempt,
        MoveRegion? region, IReadOnlyList<Guid>? bases)
    {
        var p = state.Projection;
        var moves = answered.Where(m => m.To is OnTable && p.Models.ContainsKey(m.ModelId)).DistinctBy(m => m.ModelId).ToList();
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        var landed = moves.ToDictionary(m => m.ModelId, m => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement)));
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable)
            .Select(x => (x.Id, x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var everyone = standing.Concat(landed.Select(kv => (Id: kv.Key, p.Models[kv.Key].UnitId, At: kv.Value))).ToList();
        var others = new Others([.. standing.Select(o => o.At)], [.. standing.Select(o => o.UnitId)]);
        string UnitName(Guid? id) => id is { } u && p.Units.TryGetValue(u, out var found) ? found.Definition.Name : "another unit";
        foreach (var m in moves.Where(m => p.Models[m.ModelId].Location is not OnTable f
                     || f.Placement.Position.Horizontal.DistanceTo(landed[m.ModelId].Placement.Position.Horizontal) > 1e-6))
        {                                                                                  // a model answered where it stands has not moved
            var (name, unit, at) = (p.Models[m.ModelId].Slot.Name, p.Models[m.ModelId].UnitId, landed[m.ModelId]);
            if (everyone.Any(o => o.Id != m.ModelId && Overlap.Bases(at, o.At)))
                return new(GfNames.MoveOverlapRule, $"{name} would be placed on another model's base: models may never be placed in physically impossible locations");
            if (separation is { } gap && everyone.Any(o => o.UnitId != unit && !separationExempt.Contains(o.UnitId) && Distance.BodyToBody(at, o.At) < gap - 1e-6))
                return new(GfNames.MoveSeparationRule, $"{name} would end within {gap:0.#}\" of another unit's model: models may never be within 1\" of models from other units");
            if (passesModels || region is null || Battlefield.Placed(state, m.ModelId) is not { } start) continue;
            var path = Route(state, region, m.ModelId, start, at.Placement.Position.Horizontal, others, bases);
            if (path.CrossesModels)
                return new(GfNames.MoveThroughModelsRule, $"{name} would move through {UnitName(path.Unit)}: models may never move through other models or units");
            if (path.NearModels)
                return new(GfNames.MoveSeparationRule, $"{name} would pass within {region.Separation ?? 0:0.#}\" of {UnitName(path.Unit)}: models may never be within 1\" of models from other units");
        }
        return null;
    }
}
