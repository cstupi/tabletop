using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The large sight blocker (6" × 6", 6" high, Solid): a ruined bastion tower on a stepped plinth. A square keep of
/// chamfered dark ashlar, full-thickness walls on a solid lower storey, stepped corner buttresses, a string course, slit windows
/// low and pointed-arch windows high in stone frames, its top broken off in a jagged, stepped line that stands to the full 6" at
/// one corner, blocks missing and slipped along the break, a breach in one face, rebar and a fallen floor inside, and rubble of
/// individual blocks heaped at its feet.</summary>
public static class Tower
{
    public static readonly Material Stone = new("stone", 0.88f), Trim = new("trim", 0.8f), Dark = new("openings", 1f), Bronze = new("bronze", 0.45f, Metallic: 0.7f);
    const float Size = 6, Top = 6, Half = 1.85f, Base = 0.75f, Course = 0.34f, Thick = 0.42f, Floor = 3.3f;
    static readonly Vector4 Ashlar = Rgb(0.52f, 0.5f, 0.48f), Band = Rgb(0.42f, 0.41f, 0.4f), Grime = Rgb(0.28f, 0.25f, 0.21f);

    static Vector4 Weathered(Vector4 c, Vector3 p) => Shade(Mix(c, Grime, Smooth(1.6f, 0.2f, p.Y) * 0.55f), 0.92f + 0.14f * Fbm(p * 1.2f, 441));

    /// <summary>Where the broken top stands at (x, z): the full height at the (−x, −z) corner, falling away across the keep in
    /// stepped, jagged breaks.</summary>
    static float Broken(float x, float z)
    {
        var corner = Vector2.Distance(new(x, z), new(-Half, -Half)) / (2 * Half * 1.41f);
        var y = Top + 0.05f - 2.3f * MathF.Pow(corner, 0.75f);
        return y - 0.55f * MathF.Floor(Noise(x * 1.9f, z * 1.9f, 0, 442) * 3) / 3 - 0.25f * Noise(x * 5, z * 5, 1, 443);
    }

    readonly record struct Side(Vector3 Normal, Vector3 Along)
    {
        public Vector3 P(float u, float y, float d) => Normal * (Half + d) + Along * u + Vector3.UnitY * y;
        public Matrix4x4 Rot => Masonry.Basis(Along, Vector3.UnitY, Normal);
    }

    public static Model Build()
    {
        var model = new Model("tower", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(441);
        float R() => (float)rng.NextDouble();
        var stone = model[Stone]; var trim = model[Trim];
        // The plinth: three steps of long blocks, each on a filled core.
        foreach (var (w, y0) in new[] { (2.92f, 0f), (2.5f, 0.25f), (2.1f, 0.5f) })
        {
            Masonry.Span(model[Dark], new(-w + 0.1f, y0, -w + 0.1f), new(w - 0.1f, y0 + 0.24f, w - 0.1f), 0, Rgb(0.2f, 0.19f, 0.18f));
            foreach (var side in Sides())
                for (var u = -w; u < w - 0.05f;)
                {
                    var len = MathF.Min(0.8f + 0.4f * R(), w - u);
                    var col = Shade(Band, 0.85f + 0.2f * R());
                    Masonry.Block(trim, side.Normal * (w - 0.2f) + side.Along * (u + len / 2) + Vector3.UnitY * (y0 + 0.125f), new(len - 0.03f, 0.25f, 0.4f), side.Rot, 0.04f, p => Weathered(col, p), rng);
                    u += len;
                }
        }
        // The keep's solid lower storey and the fallen floor on it, seen from above through the broken top.
        Masonry.Span(model[Dark], new(-Half + Thick - 0.02f, Base, -Half + Thick - 0.02f), new(Half - Thick + 0.02f, Floor, Half - Thick - 0.55f), 0, p => Shade(Rgb(0.2f, 0.19f, 0.17f), 0.8f + 0.4f * Fbm(p * 3, 5)));   // set back behind the breach
        Masonry.Rubble(stone, model[Dark], rng, new(0.1f, Floor, 0.15f), new(0.75f, 0.7f), 0.4f, 22, 0.16f, r => Shade(Ashlar, 0.7f + 0.3f * (float)r.NextDouble()), Rgb(0.3f, 0.28f, 0.25f));
        foreach (var (a, b) in new[] { (new Vector3(-1.2f, Floor + 0.1f, -0.6f), new Vector3(0.8f, Floor + 0.5f, -0.2f)), (new Vector3(-0.9f, Floor + 0.08f, 0.7f), new Vector3(0.7f, Floor + 0.25f, 0.9f)) })
            Masonry.Block(model[Bronze], (a + b) / 2, new(0.12f, Vector3.Distance(a, b), 0.16f), Masonry.Along(b - a), 0.015f, p => Mix(Rgb(0.32f, 0.31f, 0.3f), Rgb(0.45f, 0.25f, 0.13f), Fbm(p * 3, 9)));
        var index = 0;
        foreach (var side in Sides()) Wall(model, rng, side, index++);
        // Corner buttresses, stepping in as they rise; the (−x, −z) one stands to the top.
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = new Vector3(sx * Half, 0, sz * Half);
            var tall = sx < 0 && sz < 0;
            var cap = tall ? Top : MathF.Min(Broken(corner.X, corner.Z) - 0.1f, 4.3f);
            foreach (var (y0, y1, w) in new[] { (Base, 2.2f, 0.42f), (2.2f, 3.9f, 0.35f), (3.9f, Top, 0.28f) })
            {
                if (y0 >= cap) break;
                var top = MathF.Min(y1, cap);
                for (var y = y0; y < top - 0.02f;)
                {
                    var h = MathF.Min(0.42f, top - y);
                    var last = y + h >= cap - 0.02f && !tall;
                    var col = Shade(Band, 0.88f + 0.18f * R());
                    var turn = last ? Masonry.Turn((R() - 0.5f) * 0.3f, (R() - 0.5f) * 0.15f, (R() - 0.5f) * 0.15f) : Matrix4x4.Identity;
                    Masonry.Block(trim, corner + new Vector3(0, y + h / 2, 0), new(2 * w - (int)((y - y0) / 0.42f) % 2 * 0.04f, h - 0.03f, 2 * w), turn, 0.045f, p => Weathered(col, p), rng, last ? 0.15f : 0);
                    y += h;
                }
                if (top == y1 && y1 < cap)   // the weathering slope over the step
                    Masonry.Block(trim, corner + new Vector3(0, y1 + 0.03f, 0), new(2 * w + 0.1f, 0.08f, 2 * w + 0.1f), Matrix4x4.Identity, 0.025f, Shade(Band, 1.1f));
            }
            if (!tall && R() < 0.9f) Masonry.Rebar(model[Bronze], rng, corner with { Y = cap - 0.05f }, Vector3.UnitY, 0.4f, 0.03f);
        }
        // The capstone on the standing corner.
        Masonry.Block(trim, new(-Half, Top - 0.1f, -Half), new(0.72f, 0.2f, 0.72f), Matrix4x4.Identity, 0.04f, Shade(Band, 1.1f));
        // Rubble at the foot, heaviest under the breach on the +z face.
        foreach (var (at, r, h, n) in new[] { (new Vector3(0.2f, 0.3f, 2.3f), new Vector2(1.1f, 0.6f), 0.55f, 50), (new Vector3(2.35f, 0, -1.3f), new Vector2(0.45f, 0.8f), 0.4f, 26), (new Vector3(-2.35f, 0, 1.2f), new Vector2(0.45f, 0.7f), 0.35f, 22) })
            Masonry.Rubble(stone, model[Dark], rng, at, r, h, n, 0.2f, rr => Weathered(Shade(Ashlar, 0.7f + 0.35f * (float)rr.NextDouble()), Vector3.UnitY), Rgb(0.36f, 0.33f, 0.3f));
        return model;
    }

    static IEnumerable<Side> Sides() => [new(Vector3.UnitZ, Vector3.UnitX), new(Vector3.UnitX, -Vector3.UnitZ), new(-Vector3.UnitZ, -Vector3.UnitX), new(-Vector3.UnitX, Vector3.UnitZ)];

    /// <summary>One face of the keep: full-thickness ashlar courses up to the broken top (blocks lost and slipped near the break), the
    /// breach on the +z face, a string course, and its windows.</summary>
    static void Wall(Model model, Random rng, Side side, int index)
    {
        float R() => (float)rng.NextDouble();
        var stone = model[Stone];
        var breach = index == 0;
        float Breach(float u) => breach ? 1.5f + 1.5f * Smooth(0.85f, 0.2f, MathF.Abs(u - 0.1f)) + 0.3f * Noise(u * 4, 2, 0, 444) : 0;
        var windows = new List<(float U, float Half, float Sill, float Spring)>();
        foreach (var u in new[] { -0.85f, 0.85f })
        {
            if (!breach) windows.Add((u, 0.12f, 1.5f, 2.15f));
            var at = side.P(u, 0, 0);
            if (Broken(at.X, at.Z) > 4.6f && !(breach && MathF.Abs(u - 0.1f) < 1.1f)) windows.Add((u, 0.27f, 3.55f, 4.0f));
        }
        var row = 0;
        for (var y = Base; y < Top; y += Course, row++)
        {
            for (var u = -Half + Thick * 0.5f + (row % 2 == 0 ? 0 : -0.3f); u < Half - Thick * 0.5f;)
            {
                var len = 0.5f + 0.35f * R();
                var (a, b) = (MathF.Max(u, -Half + 0.36f), MathF.Min(u + len, Half - 0.36f));
                u += len;
                var pieces = new List<(float, float)> { (a, b) };
                foreach (var w in windows)
                {
                    if (y + Course <= w.Sill || y >= w.Spring + 1.732f * w.Half) continue;
                    var reach = w.Half + 0.16f;
                    pieces = pieces.SelectMany(p => p.Item2 <= w.U - reach || p.Item1 >= w.U + reach ? [p] : new[] { (p.Item1, w.U - reach), (w.U + reach, p.Item2) }).ToList();
                }
                foreach (var (p0, p1) in pieces.Where(p => p.Item2 - p.Item1 > 0.12f))
                {
                    var mid = (p0 + p1) / 2;
                    var at = side.P(mid, 0, 0);
                    var top = Broken(at.X, at.Z);
                    if (y >= top - Course * 0.4f) continue;
                    if (breach && y + Course > 0.9f && y < Breach(mid)) continue;
                    var near = MathF.Min(top - y, breach && y > Breach(mid) - 0.8f && MathF.Abs(mid - 0.1f) < 1.2f ? 0 : 9);
                    if (near < 0.7f && R() < 0.22f) continue;   // lost from the break
                    var col = Shade(Ashlar, 0.8f + 0.26f * R()) + new Vector4(0.02f * (R() - 0.5f), 0, -0.02f * (R() - 0.5f), 0);
                    var turn = near < 0.45f ? Masonry.Turn((R() - 0.5f) * 0.2f, (R() - 0.5f) * 0.1f, (R() - 0.5f) * 0.14f) * side.Rot : side.Rot;
                    var depth = y < Floor ? 0.2f : Thick;
                    Masonry.Block(stone, side.P(mid, y + Course / 2, -depth / 2 + 0.02f * R()), new(p1 - p0 - 0.035f, Course - 0.035f, depth), turn, 0.035f, p => Weathered(col, p), rng,
                        near < 0.6f ? 0.1f : 0, corners: near < 0.6f);
                    if (near < 0.4f && R() < 0.1f) Masonry.Rebar(model[Bronze], rng, side.P(mid, y + Course, -Thick / 2), Vector3.UnitY + side.Normal * 0.3f, 0.35f, 0.025f);
                }
            }
        }
        // The string course, where the wall stands above it.
        for (var u = -Half + 0.4f; u < Half - 0.4f;)
        {
            var len = 0.7f; var mid = u + len / 2; var at = side.P(mid, 0, 0);
            if (Broken(at.X, at.Z) > 2.9f && !(breach && Breach(mid) > 2.5f))
                Masonry.Block(model[Trim], side.P(mid, 2.6f, 0.06f), new(len - 0.03f, 0.16f, 0.24f), side.Rot, 0.035f, p => Weathered(Shade(Band, 1.05f), p), corners: false);
            u += len;
        }
        foreach (var w in windows)
            Building.Window(model, rng, side.P, side.Rot, w.U, w.Half, w.Sill, w.Spring, broken: R() < 0.5f, lit: false);
        if (breach)   // the dark inside of the breach
        {
            Masonry.Rubble(stone, model[Dark], rng, side.P(0.15f, 0.75f, -0.35f), new(0.9f, 0.5f), 0.55f, 30, 0.16f, r => Shade(Ashlar, 0.7f + 0.3f * (float)r.NextDouble()), Rgb(0.32f, 0.3f, 0.27f));
        }
    }
}
