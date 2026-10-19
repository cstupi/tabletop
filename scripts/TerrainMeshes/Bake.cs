using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>
/// The painter's finish over a built model's vertex colours, judged in inches at the size the piece is drawn: a dark wash in
/// the recesses (ambient occlusion: cosine-weighted rays against the model's opaque triangles and the table, up to
/// <see cref="Reach"/>), and a pale drybrush on convex edges (a vertex whose welded neighbours fold away below its face) and,
/// fainter, on faces that look up. Translucent parts (water, smog) are left as built.
/// </summary>
public static class Bake
{
    /// <summary>How far a ray looks for an occluder, in inches.</summary>
    public const float Reach = 1.6f;
    const int Rays = 20;
    /// <summary>A drybrush stroke's width across an edge, in inches.</summary>
    const float Brush = 0.25f;

    /// <summary>Paints <paramref name="model"/> in place, judged at <paramref name="inches"/> (its authored inches, or for a unit-box model
    /// the size it is usually drawn at); returns it.</summary>
    public static Model Apply(Model model, Vector3 inches)
    {
        var opaque = model.Parts.Where(p => !p.Material.Blend).ToList();
        var tris = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        foreach (var p in opaque)
            for (var i = 0; i < p.Indices.Count; i += 3)
                tris.Add((p.Positions[p.Indices[i]] * inches, p.Positions[p.Indices[i + 1]] * inches, p.Positions[p.Indices[i + 2]] * inches));
        var bvh = new Bvh(tris);
        var edges = Edges(opaque, inches);
        var directions = Hemisphere();
        foreach (var p in opaque)
        {
            var colours = p.Colours;
            var cache = new Dictionary<(Vector3, Vector3), float>();
            var keys = Enumerable.Range(0, p.Positions.Count).Select(i => (p.Positions[i] * inches, Normal(p.Normals[i], inches))).ToArray();
            var unique = keys.Distinct().ToArray();
            var occlusion = new float[unique.Length];
            Parallel.For(0, unique.Length, k => occlusion[k] = Occlusion(bvh, unique[k].Item1, unique[k].Item2, directions));
            for (var k = 0; k < unique.Length; k++) cache[unique[k]] = occlusion[k];
            // A drybrush catches an edge a brush-width wide: a vertex's highlight spreads over its triangles, so a large face's
            // corner is lifted only as much as a brush stroke would cover of it.
            var span = new float[p.Positions.Count];
            for (var i = 0; i < p.Indices.Count; i += 3)
            {
                var (a, b, c) = (keys[p.Indices[i]].Item1, keys[p.Indices[i + 1]].Item1, keys[p.Indices[i + 2]].Item1);
                var longest = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                for (var j = 0; j < 3; j++) span[p.Indices[i + j]] = MathF.Max(span[p.Indices[i + j]], longest);
            }
            for (var i = 0; i < p.Positions.Count; i++)
            {
                var (at, n) = keys[i];
                var ao = cache[(at, n)];
                var edge = (edges.TryGetValue((Weld(at), Weld(n)), out var e) ? e : 0) * Math.Clamp(Brush / Math.Max(span[i], 1e-3f), 0, 1);
                colours[i] = Finish(colours[i], ao, edge, n.Y, p.Material.DoubleSided);
            }
        }
        return model;
    }

    /// <summary>One vertex's paint: base colour washed darker and browner with occlusion, lifted toward a pale highlight on convex edges and up-facing faces.</summary>
    public static Vector4 Finish(Vector4 c, float occlusion, float edge, float up, bool foliage)
    {
        var light = Math.Clamp(1 - occlusion, 0, 1);
        var wash = 0.32f + 0.68f * MathF.Pow(light, 1.4f);
        var tint = new Vector3(1f, 0.93f, 0.85f);                               // a brown wash darkens warm, not grey
        var washed = new Vector3(c.X, c.Y, c.Z) * Vector3.Lerp(tint * wash, Vector3.One, wash);
        var dry = Math.Clamp(edge * 1.6f, 0, 1) * (foliage ? 0.35f : 0.75f) + Math.Max(up, 0) * 0.08f;
        var pale = Vector3.Min(new Vector3(c.X, c.Y, c.Z) * 1.55f + new Vector3(0.10f, 0.10f, 0.09f), Vector3.One);
        var o = Vector3.Lerp(washed, pale, Math.Clamp(dry * light, 0, 0.7f));
        return new Vector4(Math.Clamp(o.X, 0, 1), Math.Clamp(o.Y, 0, 1), Math.Clamp(o.Z, 0, 1), c.W);
    }

    /// <summary>A unit-box normal as drawn at <paramref name="inches"/>: normals scale by the inverse.</summary>
    static Vector3 Normal(Vector3 n, Vector3 inches) { var m = n / inches; return m.LengthSquared() > 1e-12f && float.IsFinite(m.X + m.Y + m.Z) ? Vector3.Normalize(m) : Vector3.UnitY; }

    static Vector3 Weld(Vector3 v) => new(MathF.Round(v.X * 2000) / 2000, MathF.Round(v.Y * 2000) / 2000, MathF.Round(v.Z * 2000) / 2000);

    /// <summary>Fraction of the hemisphere above the face that meets the model or the table within <see cref="Reach"/>, nearer hits counting more.</summary>
    static float Occlusion(Bvh bvh, Vector3 at, Vector3 n, Vector3[] directions)
    {
        // A frame about n; the fixed directions are rotated about it by a per-vertex angle, so neighbours do not band.
        var t = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n)) : Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
        var b = Vector3.Cross(n, t);
        var spin = Hash(at) * MathF.Tau;
        var (cs, sn) = (MathF.Cos(spin), MathF.Sin(spin));
        var origin = at + n * 0.004f;
        var hit = 0f;
        foreach (var d in directions)
        {
            var (x, z) = (d.X * cs - d.Z * sn, d.X * sn + d.Z * cs);
            var dir = Vector3.Normalize(t * x + n * d.Y + b * z);
            var dist = Reach;
            if (dir.Y < -1e-4f && origin.Y > -1e-3f) dist = Math.Min(dist, Math.Max(origin.Y, 0) / -dir.Y);   // the table under the piece
            var found = bvh.Nearest(origin, dir, dist);
            if (found < Reach) hit += 1 - found / Reach * 0.6f;
        }
        return hit / directions.Length;
    }

    static float Hash(Vector3 p)
    {
        unchecked
        {
            var h = (uint)((int)(p.X * 977) * 374761393 + (int)(p.Y * 977) * 668265263 + (int)(p.Z * 977) * 1440662683);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65536f;
        }
    }

    /// <summary>Cosine-weighted directions about +Y (a Fibonacci spiral on the disc, lifted).</summary>
    static Vector3[] Hemisphere()
    {
        var dirs = new Vector3[Rays];
        for (var i = 0; i < Rays; i++)
        {
            var r = MathF.Sqrt((i + 0.5f) / Rays); var a = i * 2.399963f;
            dirs[i] = new Vector3(r * MathF.Cos(a), MathF.Sqrt(Math.Max(0, 1 - r * r)), r * MathF.Sin(a));
        }
        return dirs;
    }

    /// <summary>Per welded (position, normal): how sharply the surface folds away at that point, 0 flat or concave, up to 1 at a right angle and beyond.</summary>
    static Dictionary<(Vector3, Vector3), float> Edges(List<Part> parts, Vector3 inches)
    {
        var faces = new List<(Vector3 N, Vector3 Centre)>();
        var at = new Dictionary<Vector3, List<int>>();
        foreach (var p in parts)
            for (var i = 0; i < p.Indices.Count; i += 3)
            {
                var (a, b, c) = (p.Positions[p.Indices[i]] * inches, p.Positions[p.Indices[i + 1]] * inches, p.Positions[p.Indices[i + 2]] * inches);
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-12f) continue;
                faces.Add((Vector3.Normalize(cross), (a + b + c) / 3));
                foreach (var v in new[] { a, b, c })
                {
                    var w = Weld(v);
                    if (!at.TryGetValue(w, out var list)) at[w] = list = [];
                    list.Add(faces.Count - 1);
                }
            }
        var result = new Dictionary<(Vector3, Vector3), float>();
        foreach (var (w, list) in at)
            foreach (var f in list.Select(i => faces[i]).DistinctBy(f => Weld(f.N)))
            {
                var key = (w, Weld(f.N));
                var fold = 0f;
                foreach (var g in list)
                {
                    var o = faces[g];
                    var turn = 1 - Vector3.Dot(f.N, o.N);
                    if (turn < 0.05f) continue;
                    if (Vector3.Dot(o.Centre - w, f.N) < -1e-4f) fold = Math.Max(fold, Math.Min(turn, 1));   // the neighbour falls away: a convex edge
                }
                result[key] = fold;
            }
        return result;
    }

    /// <summary>A bounding-volume hierarchy over triangles, median split on the longest axis; nearest hit along a ray.</summary>
    sealed class Bvh
    {
        readonly (Vector3 A, Vector3 B, Vector3 C)[] _tris;
        readonly int[] _order;
        readonly List<(Vector3 Min, Vector3 Max, int Left, int Right, int Start, int Count)> _nodes = [];

        public Bvh(List<(Vector3 A, Vector3 B, Vector3 C)> tris)
        {
            _tris = [.. tris];
            _order = Enumerable.Range(0, _tris.Length).ToArray();
            if (_tris.Length > 0) Build(0, _tris.Length);
        }

        Vector3 Centre(int i) => (_tris[i].A + _tris[i].B + _tris[i].C) / 3;

        int Build(int start, int count)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            for (var i = start; i < start + count; i++)
            {
                var t = _tris[_order[i]];
                min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C))); max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
            }
            var index = _nodes.Count;
            _nodes.Add((min, max, -1, -1, start, count));
            if (count <= 6) return index;
            var size = max - min;
            Func<Vector3, float> axis = size.X >= size.Y && size.X >= size.Z ? v => v.X : size.Y >= size.Z ? v => v.Y : v => v.Z;
            Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => axis(Centre(a)).CompareTo(axis(Centre(b)))));
            var half = count / 2;
            var left = Build(start, half); var right = Build(start + half, count - half);
            _nodes[index] = (min, max, left, right, start, count);
            return index;
        }

        public float Nearest(Vector3 origin, Vector3 dir, float reach)
        {
            if (_nodes.Count == 0) return reach;
            var inv = new Vector3(1 / (MathF.Abs(dir.X) < 1e-9f ? 1e-9f : dir.X), 1 / (MathF.Abs(dir.Y) < 1e-9f ? 1e-9f : dir.Y), 1 / (MathF.Abs(dir.Z) < 1e-9f ? 1e-9f : dir.Z));
            var best = reach;
            Span<int> stack = stackalloc int[64]; var top = 0; stack[top++] = 0;
            while (top > 0)
            {
                var n = _nodes[stack[--top]];
                var t0 = (n.Min - origin) * inv; var t1 = (n.Max - origin) * inv;
                var near = Vector3.Min(t0, t1); var far = Vector3.Max(t0, t1);
                var enter = MathF.Max(MathF.Max(near.X, near.Y), near.Z); var exit = MathF.Min(MathF.Min(far.X, far.Y), far.Z);
                if (exit < Math.Max(enter, 0) || enter > best) continue;
                if (n.Left < 0)
                {
                    for (var i = n.Start; i < n.Start + n.Count; i++) { var d = Hit(_tris[_order[i]], origin, dir); if (d > 0 && d < best) best = d; }
                    continue;
                }
                if (top < 62) { stack[top++] = n.Left; stack[top++] = n.Right; }
            }
            return best;
        }

        /// <summary>Möller–Trumbore, both faces; the distance or −1.</summary>
        static float Hit((Vector3 A, Vector3 B, Vector3 C) t, Vector3 o, Vector3 d)
        {
            var e1 = t.B - t.A; var e2 = t.C - t.A;
            var p = Vector3.Cross(d, e2); var det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-10f) return -1;
            var f = 1 / det; var s = o - t.A;
            var u = f * Vector3.Dot(s, p); if (u < 0 || u > 1) return -1;
            var q = Vector3.Cross(s, e1); var v = f * Vector3.Dot(d, q); if (v < 0 || u + v > 1) return -1;
            var dist = f * Vector3.Dot(e2, q);
            return dist > 1e-4f ? dist : -1;
        }
    }
}
