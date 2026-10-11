using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>A flat-topped hill in the built-ins' unit box: a plateau at y = 1 and a sloped rim inside the footprint (client UX spec → Physical collision). No bottom face.</summary>
public static class Plateau
{
    public const float Rim = 0.15f;

    public static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Round(int segments = 48) =>
        Build(Enumerable.Range(0, segments).Select(i => 2 * MathF.PI * i / segments).Select(t => new Vector2(MathF.Cos(t), MathF.Sin(t)) * 0.5f).ToList());

    public static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Square() => Build([new(0.5f, 0.5f), new(-0.5f, 0.5f), new(-0.5f, -0.5f), new(0.5f, -0.5f)]);

    static List<(Vector3, Vector3)> Build(IReadOnlyList<Vector2> ring)
    {
        var tris = new List<(Vector3, Vector3)>();
        static Vector3 Ground(Vector2 p) => new(p.X, 0, p.Y);
        static Vector3 Top(Vector2 p) => new(p.X * (1 - 2 * Rim), 1, p.Y * (1 - 2 * Rim));
        for (var i = 0; i < ring.Count; i++)
        {
            var (a, b) = (ring[i], ring[(i + 1) % ring.Count]); var mid = (a + b) / 2;
            var n = Vector3.Normalize(Vector3.Cross(Ground(b) - Ground(a), Top(a) - Ground(a)));
            if (n.X * mid.X + n.Z * mid.Y < 0) n = -n;                                   // the slope faces away from the axis
            Tri(tris, Ground(a), Ground(b), Top(b), n);
            Tri(tris, Ground(a), Top(b), Top(a), n);
            Tri(tris, Vector3.UnitY, Top(a), Top(b), Vector3.UnitY);
        }
        return tris;
    }

    /// <summary>One flat-shaded triangle, wound counter-clockwise seen from the side its normal points to.</summary>
    static void Tri(List<(Vector3, Vector3)> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) (b, c) = (c, b);
        tris.AddRange([(a, n), (b, n), (c, n)]);
    }
}
