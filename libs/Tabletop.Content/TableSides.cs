using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>The categories p.12 balances; a piece counts toward each its volumes declare. Counts, a piece on a dividing line split between its teams.</summary>
public readonly record struct TerrainShare(double SightBlockers, double Cover, double Difficult);

/// <summary>Each team's side of the table: the points nearer its zones' anchor than any other team's.</summary>
public static class TableSides
{
    const double Tie = 1e-6;

    /// <summary>Per team ordinal, the mean of its zone polygons' centroids; Frontline's two when <paramref name="zones"/> is empty.</summary>
    public static IReadOnlyDictionary<int, Point2> Anchors(TableSize size, IReadOnlyList<DeploymentZone> zones)
    {
        var rows = zones.Count > 0 ? zones : ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((s, i) => new DeploymentZone(Guid.Empty, s, i + 1)).ToList();
        var anchors = new Dictionary<int, Point2>();
        foreach (var team in rows.Select(z => z.TeamOrdinal).Distinct().Order())
        {
            var centroids = rows.Where(z => z.TeamOrdinal == team).SelectMany(z => ZoneStyles.Resolve(z.Shape, size))
                .Select(p => new Point2(p.Vertices.Average(v => v.X), p.Vertices.Average(v => v.Y))).ToList();
            anchors[team] = new Point2(centroids.Average(c => c.X), centroids.Average(c => c.Y));
        }
        return anchors;
    }

    /// <summary>The team whose anchor is nearest; null on a tie within 1e-6 (the dividing line).</summary>
    public static int? SideOf(IReadOnlyDictionary<int, Point2> anchors, Point2 point)
    {
        var tied = Nearest(anchors, point);
        return tied.Count == 1 ? tied[0] : null;
    }

    static List<int> Nearest(IReadOnlyDictionary<int, Point2> anchors, Point2 point)
    {
        var best = anchors.Min(a => a.Value.DistanceTo(point));
        return anchors.Where(a => a.Value.DistanceTo(point) <= best + Tie).Select(a => a.Key).Order().ToList();
    }

    /// <summary>Per team, the count of each category among the pieces whose centre lies on its side; a tied centre counts 1/k to each of the k tied teams.</summary>
    public static IReadOnlyDictionary<int, TerrainShare> Shares(TableSize size, IReadOnlyList<DeploymentZone> zones, IReadOnlyList<TerrainPlacement> terrain)
    {
        var anchors = Anchors(size, zones);
        var totals = anchors.Keys.ToDictionary(k => k, _ => new TerrainShare());
        foreach (var piece in terrain)
        {
            var declared = piece.Volumes.Aggregate(TerrainDeclarations.None, (d, v) => d | v.Volume.Declarations);
            var tied = Nearest(anchors, piece.Position.Horizontal);
            var w = 1.0 / tied.Count;
            foreach (var team in tied)
            {
                var t = totals[team];
                totals[team] = new TerrainShare(
                    t.SightBlockers + (declared.HasFlag(TerrainDeclarations.BlocksLineOfSight) ? w : 0),
                    t.Cover + (declared.HasFlag(TerrainDeclarations.Cover) ? w : 0),
                    t.Difficult + (declared.HasFlag(TerrainDeclarations.Difficult) ? w : 0));
            }
        }
        return totals;
    }
}
