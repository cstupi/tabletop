namespace Tabletop.Client.Core;

/// <summary>Low quality's model detail: an uploaded model over <see cref="TriangleCap"/> loads with one of its generated levels of detail
/// as its base, and the full mesh and the vertices only it drew are freed. The frame's LOD threshold already draws coarse levels at a
/// distance; this saves the memory (an integrated GPU's is the CPU's) and the triangles of a close-up.</summary>
public static class LowDetail
{
    /// <summary>A model's triangles on Low, summed over its surfaces: about a quarter of a 150,000-triangle miniature.</summary>
    public const long TriangleCap = 40_000;

    /// <summary>The share of each surface's triangles to keep; none when the model is within the cap.</summary>
    public static double? Ratio(long modelTriangles) => modelTriangles > TriangleCap ? (double)TriangleCap / modelTriangles : null;

    /// <summary>The first level (finest first) with at most <paramref name="ratio"/> of the base's triangles, else the coarsest; none without levels.</summary>
    public static int? PickLevel(long baseTriangles, IReadOnlyList<int> levelTriangles, double ratio)
    {
        if (levelTriangles.Count == 0) return null;
        for (var i = 0; i < levelTriangles.Count; i++) if (levelTriangles[i] <= ratio * baseTriangles) return i;
        return levelTriangles.Count - 1;
    }

    /// <summary>The vertices the new base and the coarser levels draw, in first-use order, and each index list renumbered to them.</summary>
    public static (int[] Used, int[] Base, int[][] Levels) Compact(int[] baseIndices, IReadOnlyList<int[]> levels)
    {
        var map = new Dictionary<int, int>();
        var used = new List<int>();
        int[] Remap(int[] indices)
        {
            var o = new int[indices.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                if (!map.TryGetValue(indices[i], out var n)) { map[indices[i]] = n = used.Count; used.Add(indices[i]); }
                o[i] = n;
            }
            return o;
        }
        var b = Remap(baseIndices);
        var l = levels.Select(Remap).ToArray();
        return (used.ToArray(), b, l);
    }

    /// <summary>The used vertices' entries of a per-vertex array of <paramref name="width"/> components each.</summary>
    public static T[] Gather<T>(T[] source, int width, int[] used)
    {
        var o = new T[used.Length * width];
        for (var i = 0; i < used.Length; i++) Array.Copy(source, used[i] * width, o, i * width, width);
        return o;
    }
}
