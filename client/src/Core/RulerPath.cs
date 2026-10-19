using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A move's path on the table (MoveRegions.Route) as the points a ruler draws through: from where the model stood to where it is, each
/// corner's height taken in proportion to the length walked, so the line climbs as the model does.</summary>
public static class RulerPath
{
    public static IReadOnlyList<Point3> Lift(IReadOnlyList<Point2> path, Point3 from, Point3 to)
    {
        if (path.Count < 2) return [from, to];
        var total = 0.0;
        for (var i = 1; i < path.Count; i++) total += path[i - 1].DistanceTo(path[i]);
        var lifted = new List<Point3>(path.Count) { from };
        var walked = 0.0;
        for (var i = 1; i < path.Count - 1; i++)
        {
            walked += path[i - 1].DistanceTo(path[i]);
            var z = from.Z + (to.Z - from.Z) * (total > 0 ? walked / total : 0);
            lifted.Add(new Point3(path[i].X, path[i].Y, z));
        }
        lifted.Add(to);
        return lifted;
    }
}
