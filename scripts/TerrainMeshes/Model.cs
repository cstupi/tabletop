using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>A glTF PBR material. <see cref="Colour"/> multiplies the vertex colours; colours are sRGB here and written linear.</summary>
public sealed record Material(string Name, float Roughness, float Metallic = 0, float Alpha = 1, bool DoubleSided = false, Vector3? Emissive = null)
{
    public bool Blend => Alpha < 1;
}

/// <summary>One primitive: one material, indexed triangles with per-vertex position, normal and sRGB colour.</summary>
public sealed class Part(Material material, Func<Vector3, Vector3> clamp)
{
    public Material Material { get; } = material;
    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector4> Colours { get; } = [];
    public List<int> Indices { get; } = [];
    public int Triangles => Indices.Count / 3;
    public Vector3 Clamp(Vector3 v) => clamp(v);

    public int Vertex(Vector3 p, Vector3 n, Vector4 c) { Positions.Add(p); Normals.Add(n); Colours.Add(c); return Positions.Count - 1; }

    /// <summary>A flat triangle, counter-clockwise seen from its front (glTF's front face). Clamped into the piece's footprint
    /// first; a triangle the clamp collapses is dropped.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        (a, b, c) = (clamp(a), clamp(b), clamp(c));
        var cross = Vector3.Cross(b - a, c - a);
        if (cross.LengthSquared() < 1e-14f) return;
        var n = Vector3.Normalize(cross);
        Indices.AddRange([Vertex(a, n, ca), Vertex(b, n, cb), Vertex(c, n, cc)]);
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector4 colour) => Tri(a, b, c, colour, colour, colour);

    /// <summary>Two flat triangles over a, b, c, d in counter-clockwise order.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 colour) { Tri(a, b, c, colour); Tri(a, c, d, colour); }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Func<Vector3, Vector4> colour)
    {
        Tri(a, b, c, colour(a), colour(b), colour(c)); Tri(a, c, d, colour(a), colour(c), colour(d));
    }

    /// <summary>A smooth-shaded surface over a grid of points: rows × columns, column index wrapping when <paramref name="wrap"/>.
    /// Normals average the faces around each vertex; colours are asked for once the normals are known.</summary>
    public void Grid(int rows, int columns, bool wrap, Func<int, int, Vector3> at, Func<Vector3, Vector3, Vector4> colour, bool flipped = false)
    {
        var cols = columns;
        var p = new Vector3[rows, cols];
        for (var i = 0; i < rows; i++) for (var j = 0; j < cols; j++) p[i, j] = clamp(at(i, j));
        var n = new Vector3[rows, cols];
        var tris = new List<((int, int) A, (int, int) B, (int, int) C)>();
        var lastColumn = wrap ? cols : cols - 1;
        for (var i = 0; i < rows - 1; i++)
            for (var j = 0; j < lastColumn; j++)
            {
                var j1 = (j + 1) % cols;
                (int, int) a = (i, j), b = (i, j1), c = (i + 1, j1), d = (i + 1, j);
                foreach (var t in flipped ? new[] { (a, c, b), (a, d, c) } : new[] { (a, b, c), (a, c, d) })
                {
                    var cross = Vector3.Cross(p[t.Item2.Item1, t.Item2.Item2] - p[t.Item1.Item1, t.Item1.Item2], p[t.Item3.Item1, t.Item3.Item2] - p[t.Item1.Item1, t.Item1.Item2]);
                    if (cross.LengthSquared() < 1e-14f) continue;
                    tris.Add(t);
                    foreach (var (vi, vj) in new[] { t.Item1, t.Item2, t.Item3 }) n[vi, vj] += cross;
                }
            }
        var index = new int[rows, cols];
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var normal = n[i, j].LengthSquared() > 0 ? Vector3.Normalize(n[i, j]) : Vector3.UnitY;
                index[i, j] = Vertex(p[i, j], normal, colour(p[i, j], normal));
            }
        foreach (var (a, b, c) in tris) Indices.AddRange([index[a.Item1, a.Item2], index[b.Item1, b.Item2], index[c.Item1, c.Item2]]);
    }
}

/// <summary>What one built-in mesh may cost, and all of them together (the built-in content ships in every client and is
/// fetched by every player): spec 2026-10-19-tabletop-look-design.md → Budgets.</summary>
public static class Budget
{
    public const int Triangles = 50_000;
    public const int Bytes = 3 << 20;
    public const long TotalBytes = 36L << 20;
}

/// <summary>A built-in terrain mesh in the unit box: footprint on x and z in [−0.5, 0.5] (inside the unit circle's r = 0.5 when
/// <see cref="Round"/>), height on y in [0, 1], glTF Y-up. Every vertex is clamped into it. A model built with <paramref name="inches"/>
/// is authored in inches, centred on its footprint (x across a, z along b, y up to the top), and each vertex is divided by them
/// before the clamp; a round one's inches are its diameter, its diameter and its top.</summary>
public sealed class Model(string name, bool round, Vector3? inches = null)
{
    public string Name { get; } = name;
    public bool Round { get; } = round;
    public Vector3 Inches { get; } = inches ?? Vector3.One;

    /// <summary>A point of the unit box (as a Grid's colour function sees it) back in the authored inches.</summary>
    public Vector3 Inch(Vector3 unit) => unit * Inches;
    readonly Dictionary<Material, Part> _parts = new();
    public IEnumerable<Part> Parts => _parts.Values.Where(p => p.Triangles > 0);
    public int Triangles => Parts.Sum(p => p.Triangles);

    public Part this[Material m] => _parts.TryGetValue(m, out var p) ? p : _parts[m] = new Part(m, Clamp);

    public Vector3 Clamp(Vector3 v)
    {
        v /= Inches;
        v = new Vector3(Math.Clamp(v.X, -0.5f, 0.5f), Math.Clamp(v.Y, 0f, 1f), Math.Clamp(v.Z, -0.5f, 0.5f));
        if (!Round) return v;
        var r = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        return r <= 0.5f ? v : v with { X = v.X * 0.5f / r, Z = v.Z * 0.5f / r };
    }
}
