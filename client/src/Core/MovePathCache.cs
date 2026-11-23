using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// An open move Ask's paths (MovePaths.Proposed) kept per model while its ghost stays put: a drag frame sweeps again only the models whose ghost
/// moved (or whose cap changed), and hands back the same ModelPath for the rest, so a drawing that compares them (MovePathNode) is not refilled.
/// One per Ask: the regions, starts, Ask and volumes it was made for stand for its life.
/// </summary>
public sealed class MovePathCache(MoveRegions regions, IReadOnlyDictionary<Guid, PlacedModel> starts, MoveRegion legal, IReadOnlyList<WorldVolume> volumes)
{
    readonly Dictionary<Guid, (Point2 To, bool Capped, Yaw? Facing, ModelPath? Path)> _kept = new();

    /// <summary>MovePaths.Proposed for these ghosts, in the Ask's order, each ending at its facing in `facings`.</summary>
    public IReadOnlyList<ModelPath> Proposed(IReadOnlyDictionary<Guid, Point2> to, bool capped = false, IReadOnlyDictionary<Guid, Yaw>? facings = null)
    {
        var paths = new List<ModelPath>();
        foreach (var id in legal.ModelIds)
        {
            if (!starts.TryGetValue(id, out var start) || !to.TryGetValue(id, out var end)) continue;
            var facing = facings?.GetValueOrDefault(id);
            if (!_kept.TryGetValue(id, out var kept) || kept.To != end || kept.Capped != capped || kept.Facing != facing)
            {
                var path = start.Placement.Position.Horizontal.DistanceTo(end) <= 1e-6 || regions.Route(id, end, capped) is not { } route
                    ? null : MovePaths.Of(id, start, route.Points, route.Distance, legal, volumes, facing);
                _kept[id] = kept = (end, capped, facing, path);
            }
            if (kept.Path is { } p) paths.Add(p);
        }
        return paths;
    }
}
