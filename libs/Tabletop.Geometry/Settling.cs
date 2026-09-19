namespace Tabletop.Geometry;

/// <summary>Raycast down: the highest Solid top under the point, or 0 (the slab) when nothing is.</summary>
public static class Settling
{
    public static double SurfaceHeightAt(Point2 point, IReadOnlyList<WorldVolume> volumes) =>
        SurfaceHeightAt(new Point3(point.X, point.Y, double.PositiveInfinity), volumes);

    /// <summary>Raycast down from <paramref name="from"/>: the highest Solid top at or below from.Z, or 0. Area volumes are passed through.</summary>
    public static double SurfaceHeightAt(Point3 from, IReadOnlyList<WorldVolume> volumes)
    {
        var best = 0.0;
        foreach (var v in volumes)
        {
            if (v.Volume.Kind != VolumeKind.Solid) continue;
            var top = v.WorldTop();
            if (top <= best || top > from.Z + Tolerance.Distance) continue;
            if (v.WorldOutline().Contains(from.Horizontal)) best = top;
        }
        return best;
    }
}
