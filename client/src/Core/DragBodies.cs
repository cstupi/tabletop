using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One held model: its body where it stands now and where the cursor puts it.</summary>
public sealed record HeldBody(Guid Id, PlacedModel At, Point2 To);

/// <summary>What a drag collides with (client UX spec → Physical collision → Client). Alt places freely; the drop still rests.</summary>
public static class DragBodies
{
    /// <summary>Every model not held, where it stands: a ghost (a proposed placement, a disembark ghost) where it is drawn, any other model on the table.</summary>
    public static IReadOnlyList<PlacedModel> Stationary(Projection p, IReadOnlyCollection<Guid> held, IReadOnlyDictionary<Guid, Placement>? ghosts = null) =>
        p.Models.Values.Where(m => !held.Contains(m.Id))
            .Select(m => (m, At: ghosts is not null && ghosts.TryGetValue(m.Id, out var g) ? g : (m.Location as OnTable)?.Placement))
            .Where(x => x.At is not null)
            .Select(x => new PlacedModel(new ModelBody(x.m.Slot.Base, x.m.Slot.Height), x.At!.Value)).ToList();

    /// <summary>Where each picked-up piece starts: a proposed model (a move step's or a placement's ghost) where it is drawn and facing the way it
    /// was turned, so picking it up again does not put it back; anything else, or a ghost with no node, where the projection has it.</summary>
    public static Dictionary<Guid, Placement> Grabs(IReadOnlyList<Guid> models, IReadOnlySet<Guid> proposing, Func<Guid, Placement?> drawn, Func<Guid, Placement> projected) =>
        models.ToDictionary(m => m, m => proposing.Contains(m) && drawn(m) is { } ghost ? ghost : projected(m));

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

    /// <summary>
    /// A setup terrain drag from where the piece is held (at, its yaw included): first moved clear of any piece declared Impassable its
    /// footprint enters (a turn in hand), then straight to the cursor when that spot is free, else slid toward it along what it meets
    /// (Collision.Follow). Rests on the Solid tops under its footprints. Alt places freely; the drop still rests.
    /// </summary>
    public static Point3 Piece(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Placement at, Point2 to, bool free)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        var held = terrain.Single(t => t.Id == pieceId) with { Position = at.Position, Yaw = at.Yaw };
        if (free) return Collision.Slide(held.WorldVolumes(), to, [], others).Position;
        var walls = Walls(others, null);
        var clear = held with { Position = Collision.Clear(held.WorldVolumes(), walls, others).Position };
        return Collision.Follow(clear.WorldVolumes(), to, walls, others).Position;
    }

    /// <summary>A piece turned in place to the yaw: where it stands, or the nearest spot clear of the pieces declared Impassable, rested.</summary>
    public static Point3 Turn(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Yaw yaw)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        var turned = terrain.Single(t => t.Id == pieceId) with { Yaw = yaw };
        return Collision.Clear(turned.WorldVolumes(), Walls(others, null), others).Position;
    }
}
