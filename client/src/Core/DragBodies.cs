using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One held model: its body where it stands now and where the cursor puts it.</summary>
public sealed record HeldBody(Guid Id, PlacedModel At, Point2 To);

/// <summary>What a drag collides with (client UX spec → Physical collision → Client). Alt places freely; the drop still rests.</summary>
public static class DragBodies
{
    public static IReadOnlyList<PlacedModel> Stationary(Projection p, IReadOnlyCollection<Guid> held) =>
        p.Models.Values.Where(m => !held.Contains(m.Id) && m.Location is OnTable)
            .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();

    /// <summary>What a body slides along: inside a move Ask the volumes it lists as impassable, otherwise every volume declared Impassable. Collision keeps the Solid ones.</summary>
    public static IReadOnlyList<WorldVolume> Walls(IReadOnlyList<WorldVolume> volumes, MoveRegion? region) => region is not null
        ? volumes.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList()
        : volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();

    public static IReadOnlyDictionary<Guid, Point3> Models(IReadOnlyList<HeldBody> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> volumes, MoveRegion? region, bool free)
    {
        if (free) return held.ToDictionary(h => h.Id, h => new Point3(h.To.X, h.To.Y, Collision.RestHeight(h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } }, volumes)));
        var ends = Collision.Separate(held.Select(h => (h.At, h.To)).ToList(), region is { PassesModels: true } ? [] : stationary, Walls(volumes, region), volumes);
        return held.Select((h, i) => (h.Id, End: ends[i])).ToDictionary(x => x.Id, x => x.End);
    }

    /// <summary>A setup terrain drag: slides against the other pieces declared Impassable, rests on the Solid tops under its footprints.</summary>
    public static Point3 Piece(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Point2 to, bool free)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        return Collision.Slide(terrain.Single(t => t.Id == pieceId).WorldVolumes(), to, free ? [] : Walls(others, null), others).Position;
    }
}
