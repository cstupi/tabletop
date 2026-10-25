namespace Tabletop.Geometry;

/// <summary>Which volumes a model is in. Computed, never stored (table domain model). Solid or Area alike; standing on a Solid's top counts.</summary>
public static class Containment
{
    /// <summary>Volumes whose footprint contains the base centre and whose vertical range contains the base height.</summary>
    public static IReadOnlyList<Guid> VolumesContaining(PlacedModel model, IReadOnlyList<WorldVolume> volumes)
    {
        var centre = model.Placement.Position.Horizontal;
        var z = model.Bottom();
        return volumes.Where(v => v.VerticalRangeContains(z) && v.WorldOutline().Contains(centre)).Select(v => v.Id).ToList();
    }

    /// <summary>Volumes whose footprint contains the whole base — rulebook p.11 "fully inside" — same vertical rule.</summary>
    public static IReadOnlyList<Guid> VolumesFullyContaining(PlacedModel model, IReadOnlyList<WorldVolume> volumes)
    {
        var outline = model.BaseOutline();
        var z = model.Bottom();
        return volumes.Where(v => v.VerticalRangeContains(z) && v.WorldOutline().ContainsOutline(outline)).Select(v => v.Id).ToList();
    }

    /// <summary>
    /// Rulebook p.15 and p.21: "must stay fully within 6" of it when exiting" — every point of the model's base within
    /// <paramref name="inches"/> of the container's base, measured edge to edge and horizontally, as every distance here is.
    /// Both outlines are convex, so the farthest point of the base is exact without sampling: a vertex for a polygon base,
    /// the centre pushed out by the radius for a circular one. When the bases overlap the circular form is an upper bound,
    /// which can only refuse a placement already touching the container.
    /// </summary>
    public static bool BaseFullyWithin(PlacedModel model, PlacedModel container, double inches)
    {
        var of = container.BaseOutline();
        var limit = inches + Tolerance.Distance;
        return model.BaseOutline() switch
        {
            CircleOutline c => of.DistanceTo(c.Centre) + c.Radius <= limit,
            PolygonOutline p => p.Vertices.All(v => of.DistanceTo(v) <= limit),
            var other => throw new ArgumentOutOfRangeException(nameof(model), other.GetType().Name),
        };
    }

    /// <summary>How many facings a non-circular base is tried at: p.7 lets a model take any facing, so an exit position is legal if some yaw fits.
    /// Eight are 45° apart, finer than the exit region's own 2° rays are deep. A circular base is yaw-invariant and tries one.</summary>
    public const int ExitYaws = 8;

    /// <summary>
    /// A facing this exit position is legal at, or null if none is (p.7, p.15): the base fully within <paramref name="within"/> of the
    /// container's base, on the table and clear of the impassable volumes. The last two are asked through <see cref="Movement.Check"/> with the
    /// candidate as both start and destination: the distance is zero, so Check samples exactly that placement and answers CrossesImpassable
    /// and LeavesTable for it. The candidate takes the container's Z; a circular base answers at the first try, 0°.
    /// </summary>
    public static Yaw? ExitYaw(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var yaws = body.Base.Shape == BaseShape.Circle ? 1 : ExitYaws;
        for (var i = 0; i < yaws; i++)
        {
            var yaw = new Yaw(360.0 * i / yaws);
            var candidate = new PlacedModel(body, new Placement(new Point3(at.X, at.Y, container.Placement.Position.Z), yaw));
            if (!BaseFullyWithin(candidate, container, within)) continue;
            var check = Movement.Check(candidate, at, 0, impassable, [], null, size);
            if (!check.CrossesImpassable && !check.LeavesTable) return yaw;
        }
        return null;
    }
}
