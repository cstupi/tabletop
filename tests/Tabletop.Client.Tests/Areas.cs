using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>A drawn area (MoveRegions.Outline, MovementRegion.Triangles: three points per triangle) holds a point.</summary>
internal static class Areas
{
    public static bool Inside(IReadOnlyList<Point2> triangles, Point2 p)
    {
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var (a, b, c) = (triangles[i], triangles[i + 1], triangles[i + 2]);
            double d1 = (b - a).Cross(p - a), d2 = (c - b).Cross(p - b), d3 = (a - c).Cross(p - c);
            if (Math.Abs(d1) + Math.Abs(d2) + Math.Abs(d3) > 0 && ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0))) return true;
        }
        return false;
    }
}
