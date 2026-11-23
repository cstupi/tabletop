using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What a placement ghost draws: the model's own body (the hitbox), the mesh of its slot's asset (null draws the hitbox alone), and
/// where it stands facing which way. The mesh's scale and orientation are the asset's, applied by the loader exactly as for the placed
/// model, so a ghost and the model it becomes look the same.
/// </summary>
public sealed record GhostLook(Guid ModelId, Guid UnitId, Base Base, double Height, Guid? AssetId, Placement At);

/// <summary>The ghosts the table draws for a placement: a deployment or a Spawn or Split reserve (shelf models at their proposed
/// placements, a disembark's occupants beside their container), and another player's proposed placement.</summary>
public static class GhostLooks
{
    public static GhostLook Of(ModelInPlay m, Placement at) => new(m.Id, m.UnitId, m.Slot.Base, m.Slot.Height, m.Slot.AssetId, at);

    /// <summary>One ghost per relocation onto the table, at its placement and facing; a model the projection lacks draws none.</summary>
    public static IReadOnlyList<GhostLook> Placing(Projection p, IReadOnlyList<ModelRelocation> placements) =>
        placements.Where(r => r.To is OnTable).Select(r => (r, m: p.Models.GetValueOrDefault(r.ModelId)))
            .Where(x => x.m is not null).Select(x => Of(x.m!, ((OnTable)x.r.To).Placement)).ToList();

    /// <summary>Another player's proposed placement of a model: that model's ghost where and as they turned it. Null for a piece that is no model.</summary>
    public static GhostLook? Proposed(Projection? p, PresencePlacement placement) =>
        p?.Models.GetValueOrDefault(placement.PieceId) is { } m ? Of(m, new Placement(placement.Position, placement.Yaw)) : null;
}
