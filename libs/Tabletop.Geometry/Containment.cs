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
}
