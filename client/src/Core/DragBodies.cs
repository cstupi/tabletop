using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One held model: its body where it stands now and where the cursor puts it.</summary>
public sealed record HeldBody(Guid Id, PlacedModel At, Point2 To);

/// <summary>Where a held piece is drawn and where it lands (held pieces lift over spec): apart only while it is lifted, hovering at its target.</summary>
public sealed record HeldEnd(Point3 Drawn, Point3 Lands);

/// <summary>What a drag collides with (client UX spec → Physical collision → Client; held pieces lift over spec). Alt places freely; the drop still rests.</summary>
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

    /// <summary>A charge's landing pull (Collision.Abut): the Ask's ContactWith models where they stand, its ContactGrace and its ContactVertical (a
    /// landing resting farther above or below a model's base than that is not drawn against it); null for any other move (and a disembark, which the
    /// server reads as it is).</summary>
    public static Abutment? Abutment(Projection p, MoveRegion? region) =>
        region is { ContactWith: { Count: > 0 } contact, ContactGrace: { } grace } && region.Containment?.ExitingId is null
            ? new Abutment(contact.Where(p.Models.ContainsKey).Select(id => p.Models[id]).Where(m => m.Location is OnTable)
                .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList(), grace, region.ContactVertical)
            : null;

    /// <summary>
    /// Held pieces lift over spec → Held. Lifted (level null): each held model goes straight to its target, drawn <see cref="Collision.Lift"/> over
    /// the highest top under it, models included (Collision.Hover), and lands at the nearest spot where its base meets no other model's (the held
    /// ones already landed included; a landing on a base is never right, so a region that passes models still counts them) and enters no wall,
    /// inside a move step only where `within` takes it (Collision.Land); in a charge (`abut`, <see cref="Abutment"/>) a landing within the grace of a
    /// target model is drawn flush against it, as the server ends the charge. At a level: today's drag at that height (Collision.Separate). Alt
    /// (`free`): straight to the target, resting on the open surface nearest the level (the highest when lifted).
    /// </summary>
    public static IReadOnlyDictionary<Guid, HeldEnd> Models(IReadOnlyList<HeldBody> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> volumes, MoveRegion? region,
        bool free, double? level, Func<Guid, Point2, bool>? within = null, Abutment? abut = null)
    {
        static PlacedModel At(HeldBody h) => h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } };
        if (free)
            return held.ToDictionary(h => h.Id, h => { var end = new Point3(h.To.X, h.To.Y, Collision.RestNear(At(h), volumes, level ?? double.PositiveInfinity)); return new HeldEnd(end, end); });
        if (level is { } l)
        {
            var ends = Collision.Separate(held.Select(h => (h.At, h.To)).ToList(), region is { PassesModels: true } ? [] : stationary, Walls(volumes, region), volumes, l);
            return held.Select((h, i) => (h.Id, End: ends[i])).ToDictionary(x => x.Id, x => new HeldEnd(x.End, x.End));
        }
        var lands = Collision.Land(held.Select(h => (h.At, h.To, within is null ? null : (Func<Point2, bool>)(p => within(h.Id, p)))).ToList(), stationary, Walls(volumes, region), volumes, abut);
        return held.Select((h, i) => (h, Lands: lands[i])).ToDictionary(x => x.h.Id, x => new HeldEnd(new Point3(x.h.To.X, x.h.To.Y, Collision.Hover(At(x.h), volumes, stationary)), x.Lands));
    }

    /// <summary>
    /// A setup terrain drag from where the piece is held (at, its yaw included). Lifted (level null): straight to the cursor, drawn
    /// <see cref="Collision.Lift"/> over the highest top under it, landing where its footprint enters no piece declared Impassable, else at the
    /// nearest spot that does (Collision.Land). At a level: first moved clear of what its footprint meets there (a turn in hand), then straight
    /// to the cursor when that spot is free, else slid toward it (Collision.Follow), so it passes under a bridge with room. Alt places freely:
    /// straight, resting on the open surface nearest the level.
    /// </summary>
    public static HeldEnd Piece(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Placement at, Point2 to, bool free, double? level)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        var held = terrain.Single(t => t.Id == pieceId) with { Position = at.Position, Yaw = at.Yaw };
        if (held.Volumes.Count == 0) { var flat = new Point3(to.X, to.Y, 0); return new HeldEnd(flat, flat); }   // nothing to collide or rest
        var there = held with { Position = new Point3(to.X, to.Y, 0) };
        if (free) { var end = new Point3(to.X, to.Y, Collision.RestNear(there.WorldVolumes(), others, level ?? double.PositiveInfinity)); return new HeldEnd(end, end); }
        var walls = Walls(others, null);
        if (level is { } l)
        {
            var clear = held with { Position = Collision.Clear(held.WorldVolumes(), walls, others, l).Position };
            var end = Collision.Follow(clear.WorldVolumes(), to, walls, others, l).Position;
            return new HeldEnd(end, end);
        }
        return new HeldEnd(new Point3(to.X, to.Y, Collision.Hover(there.WorldVolumes(), others)), Collision.Land(held.WorldVolumes(), to, walls, others).Position);
    }

    /// <summary>The held piece's open surfaces where it stands (Collision.Surfaces): what PageUp and PageDown step through (HoldLevel).</summary>
    public static IReadOnlyList<double> Surfaces(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Placement at)
    {
        var held = terrain.Single(t => t.Id == pieceId) with { Position = at.Position, Yaw = at.Yaw };
        return held.Volumes.Count == 0 ? [0] : Collision.Surfaces(held.WorldVolumes(), terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList());
    }

    /// <summary>A piece turned in place to the yaw: where it stands, or the nearest spot clear of the pieces declared Impassable, rested.</summary>
    public static Point3 Turn(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Yaw yaw)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        var turned = terrain.Single(t => t.Id == pieceId) with { Yaw = yaw };
        if (turned.Volumes.Count == 0) return turned.Position;
        return Collision.Clear(turned.WorldVolumes(), Walls(others, null), others).Position;
    }
}
