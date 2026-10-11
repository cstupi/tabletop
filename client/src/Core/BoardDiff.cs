using Tabletop.Content;

namespace Tabletop.Client.Core;

/// <summary>
/// One table layer between what the table last drew and the board now. Added: a new id. Removed: an id no longer on the board.
/// Reshaped: same id, but what its node is built from differs, so the node is rebuilt. Kept: same id and shape; its placement may
/// have moved, which costs only a transform.
/// </summary>
public sealed record BoardDiff<T>(IReadOnlyList<T> Added, IReadOnlyList<Guid> Removed, IReadOnlyList<T> Reshaped, IReadOnlyList<T> Kept);

public static class BoardDiff
{
    /// <summary>Added, reshaped and kept follow <paramref name="wanted"/>'s order. A duplicate id in <paramref name="wanted"/> throws.</summary>
    public static BoardDiff<T> Of<T>(IReadOnlyDictionary<Guid, T> drawn, IEnumerable<T> wanted, Func<T, Guid> id, Func<T, T, bool> sameShape)
    {
        var want = wanted.ToList();
        var ids = new HashSet<Guid>();
        List<T> added = [], reshaped = [], kept = [];
        foreach (var item in want)
        {
            var key = id(item);
            if (!ids.Add(key)) throw new ArgumentException($"Duplicate id {key}.", nameof(wanted));
            if (!drawn.TryGetValue(key, out var old)) added.Add(item);
            else if (sameShape(old, item)) kept.Add(item);
            else reshaped.Add(item);
        }
        return new(added, drawn.Keys.Where(k => !ids.Contains(k)).ToList(), reshaped, kept);
    }

    /// <summary>Terrain is built from its mesh and its volumes; position, yaw and scale are its transform.</summary>
    public static bool SameShape(TerrainPlacement a, TerrainPlacement b) => a.Mesh == b.Mesh && a.Volumes.SequenceEqual(b.Volumes);

    /// <summary>A marker is built from its kind, diameter, label and visual; position and yaw are its transform.</summary>
    public static bool SameShape(Marker a, Marker b) => a.Kind == b.Kind && a.Diameter == b.Diameter && a.Label == b.Label && a.Visual == b.Visual;
}
