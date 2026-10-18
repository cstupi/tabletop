using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The large sight blocker (6" × 6", 6" high, Solid): a ruined bastion tower on a stepped plinth. A square keep of coursed
/// dark stone with stepped corner buttresses, bands at each floor, arrow-slit and arched windows, its top broken off in a jagged
/// line that stands to the full 6" at one corner, a breach in one face, and rubble heaped at its feet.</summary>
public static class Tower
{
    public static readonly Material Stone = new("stone", 0.88f), Trim = new("trim", 0.8f), Dark = new("openings", 1f), Bronze = new("bronze", 0.45f, Metallic: 0.7f);
    const float Size = 6, Top = 6, Half = 1.55f;   // Half: the keep's half-width

    public static Model Build()
    {
        var model = new Model("tower", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(441);
        var stone = Rgb(0.46f, 0.45f, 0.43f); var band = Rgb(0.34f, 0.33f, 0.32f); var grime = Rgb(0.3f, 0.29f, 0.27f);
        // The plinth: three steps, the lowest filling the footprint.
        foreach (var (h, w, y0) in new[] { (2.95f, 2.95f, 0f), (2.45f, 2.45f, 0.2f), (2.0f, 2.0f, 0.4f) })
            Shapes.Box(model[Trim], new Vector3(-w, y0, -h), new Vector3(w, y0 + 0.2f, h), p => Shade(band, 0.9f + 0.15f * Fbm(p * 2, 441)));
        // The keep: courses of stone up to a broken top, the jag highest at the (−x, −z) corner.
        float Broken(float x, float z)
        {
            var corner = Vector2.Distance(new(x, z), new(-Half, -Half)) / (2 * Half * 1.41f);   // 0 at the high corner
            var y = Top - 2.4f * MathF.Pow(corner, 0.8f);
            return y - 0.5f * MathF.Floor(Noise(x * 2.2f, z * 2.2f, 0, 442) * 3) / 3;
        }
        const float course = 0.3f, column = 0.26f;
        foreach (var (from, to, n) in new[] { (new Vector2(-Half, -Half), new Vector2(Half, -Half), -Vector3.UnitZ), (new Vector2(Half, -Half), new Vector2(Half, Half), Vector3.UnitX), (new Vector2(Half, Half), new Vector2(-Half, Half), Vector3.UnitZ), (new Vector2(-Half, Half), new Vector2(-Half, -Half), -Vector3.UnitX) })
        {
            var length = Vector2.Distance(from, to); var columns = (int)MathF.Round(length / column);
            var dir = (to - from) / length;
            for (var c = 0; c < columns; c++)
            {
                var u = (c + 0.5f) / columns; var mid = Vector2.Lerp(from, to, u);
                var top = Broken(mid.X, mid.Y);
                var breach = n == Vector3.UnitZ && u is > 0.3f and < 0.62f;   // the breach in the +z face
                var openingBottom = breach ? 0.6f : 99f; var openingTop = breach ? 1.6f + 1.2f * (1 - MathF.Abs(u - 0.46f) / 0.16f) : 0;
                for (var y = 0.6f; y < top; y += course)
                {
                    var y1 = MathF.Min(y + course - 0.02f, top);
                    if (y < openingTop && y1 > openingBottom) continue;
                    var shade = 0.85f + 0.18f * (float)rng.NextDouble();
                    var centre = new Vector3(mid.X, (y + y1) / 2, mid.Y) - n * 0.15f;
                    var size = new Vector3(dir.X == 0 ? 0.32f : length / columns + 0.002f, y1 - y, dir.Y == 0 ? 0.32f : length / columns + 0.002f);
                    Shapes.Box(model[Stone], Shapes.At(centre, size), p => Shade(Mix(grime, stone, Smooth(0.6f, 2f, p.Y)), shade));
                }
            }
            // Bands at each floor, and windows: slits low, arched windows high.
            foreach (var y in new[] { 2.1f, 3.6f })
            {
                var a = new Vector3(from.X, y, from.Y) + n * 0.05f; var b = new Vector3(to.X, y + 0.14f, to.Y) + n * 0.05f - n * 0.2f;
                if (Broken((from.X + to.X) / 2, (from.Y + to.Y) / 2) > y + 0.3f && !(n == Vector3.UnitZ && y < 3))
                    Shapes.Box(model[Trim], Vector3.Min(a, b), Vector3.Max(a, b), band);
            }
            foreach (var u in new[] { 0.25f, 0.75f })
            {
                if (n == Vector3.UnitZ) continue;
                var mid = Vector2.Lerp(from, to, u); var at = new Vector3(mid.X, 0, mid.Y) + n * 0.02f;
                var along = new Vector3(dir.X, 0, dir.Y);
                Slit(model, at, along, n, 1.2f, 1.8f, 0.08f);
                if (Broken(mid.X, mid.Y) > 3.4f) Slit(model, at, along, n, 2.6f, 3.3f, 0.2f);
                if (Broken(mid.X, mid.Y) > 4.6f) Slit(model, at, along, n, 4.0f, 4.6f, 0.2f);
            }
        }
        // Corner buttresses, stepping in as they rise.
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = new Vector3(sx * Half, 0, sz * Half);
            var cap = MathF.Min(Broken(corner.X, corner.Z), sx < 0 && sz < 0 ? Top : 4.4f);
            foreach (var (y0, y1, w) in new[] { (0.6f, 2.0f, 0.42f), (2.0f, 3.4f, 0.34f), (3.4f, cap, 0.27f) })
            {
                if (y0 >= cap) break;
                Shapes.Box(model[Trim], corner + new Vector3(-w, y0, -w), corner + new Vector3(w, MathF.Min(y1, cap), w), p => Shade(band, 0.9f + 0.15f * Fbm(p * 3, 443)));
                Shapes.Box(model[Trim], corner + new Vector3(-w - 0.04f, MathF.Min(y1, cap) - 0.08f, -w - 0.04f), corner + new Vector3(w + 0.04f, MathF.Min(y1, cap), w + 0.04f), Shade(band, 0.75f));
            }
        }
        // A bronze finial on the standing corner, and the dark floor visible through the breach.
        Shapes.Prism(model[Bronze], Shapes.At(new Vector3(-Half, Top - 0.35f, -Half), new Vector3(0.1f, 0.35f, 0.1f)), 6, 0, Rgb(0.6f, 0.45f, 0.25f));
        Shapes.Box(model[Dark], new Vector3(-Half + 0.3f, 0.6f, -Half + 0.3f), new Vector3(Half - 0.3f, 0.62f, Half - 0.3f), Rgb(0.08f, 0.07f, 0.06f));
        // Rubble at the foot, heaviest under the breach.
        foreach (var (pile, count, spread) in new[] { (new Vector2(0.1f, 2.3f), 40, 0.9f), (new Vector2(2.2f, -1.6f), 14, 0.5f), (new Vector2(-2.3f, 1.4f), 12, 0.5f) })
            for (var k = 0; k < count; k++)
            {
                var a = (float)(rng.NextDouble() * 2 * MathF.PI); var r = spread * MathF.Sqrt((float)rng.NextDouble());
                var s = 0.1f + 0.15f * (float)rng.NextDouble();
                var p = new Vector3(pile.X + MathF.Cos(a) * r, 0.2f + s * 0.4f + (spread - r) * 0.5f, pile.Y + MathF.Sin(a) * r);
                Shapes.Box(model[Stone], Shapes.At(p, new Vector3(s * 1.4f, s, s), (float)rng.NextDouble() * 3, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), Shade(stone, 0.75f + 0.3f * (float)rng.NextDouble()));
            }
        return model;
    }

    /// <summary>A window on the face at <paramref name="at"/>: a dark recess, a sill and an arched or flat head.</summary>
    static void Slit(Model model, Vector3 at, Vector3 along, Vector3 n, float y0, float y1, float half)
    {
        Vector3 P(float a, float y, float d) => at + along * a + Vector3.UnitY * y + n * d;
        void Slab(Material m, float a0, float a1, float b0, float b1, float d0, float d1, Vector4 c) { var (p, q) = (P(a0, b0, d0), P(a1, b1, d1)); Shapes.Box(model[m], Vector3.Min(p, q), Vector3.Max(p, q), c); }
        Slab(Dark, -half, half, y0, y1, -0.01f, 0.02f, Rgb(0.05f, 0.045f, 0.04f));
        Slab(Trim, -half - 0.06f, half + 0.06f, y0 - 0.07f, y0, -0.01f, 0.08f, Rgb(0.36f, 0.35f, 0.33f));
        for (var i = 0; i < 4; i++)   // a stepped arch over the opening
        {
            var w = half + 0.06f - i * (half / 4);
            Slab(Trim, -w, w, y1 + i * 0.05f, y1 + (i + 1) * 0.05f, -0.01f, 0.06f, Rgb(0.36f, 0.35f, 0.33f));
        }
    }
}
