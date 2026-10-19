using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>
/// The flat-topped hills (client UX spec → Physical collision): a flocked plateau at y = 1 over the inner <see cref="Top"/> of the
/// footprint, and three terraces down to the footprint's edge, the turf lumpy over buried stone, stratified rock breaking
/// through every riser, rubble on the ledges and at the foot, tufts of static grass along the ledges. <see cref="Round"/> fills
/// the unit circle, <see cref="Square"/> a rounded unit square. The detail is authored in inches at the size each is usually
/// drawn (8" × 2" and 6" × 2") and divided back into the unit box, so it stays in proportion once fitted.
/// </summary>
public static class Hill
{
    /// <summary>The plateau's extent as a fraction of the way from the centre to the edge (a 15% rim each side).</summary>
    public const float Top = 0.7f;
    const int Segments = 128, TopRings = 10, SlopeRings = 30, Terraces = 3;

    public static readonly Material Ground = new("ground", 0.92f), Stone = new("stone", 0.85f), Grass = new("grass", 0.9f, DoubleSided: true);

    public static Model Round() => Build(new Model("hill", round: true), t => new Vector2(MathF.Cos(t), MathF.Sin(t)) * 0.5f, 11, new Vector3(8, 2, 8));

    public static Model Square() => Build(new Model("hill-rect", round: false), t => Superellipse(t, 8) * 0.5f, 12, new Vector3(6, 2, 6));

    static Vector2 Superellipse(float t, float p)
    {
        float c = MathF.Cos(t), s = MathF.Sin(t);
        return new Vector2(MathF.Sign(c) * MathF.Pow(MathF.Abs(c), 2 / p), MathF.Sign(s) * MathF.Pow(MathF.Abs(s), 2 / p));
    }

    /// <summary>Height on the slope, s = 0 at the plateau's edge to 1 at the footprint's: three terraces, each a ledge and a riser,
    /// over a straight fall, so no riser is a cliff.</summary>
    public static float Slope(float s)
    {
        s = Math.Clamp(s, 0, 1);
        var t = s * Terraces; var i = MathF.Min(MathF.Floor(t), Terraces - 1); var u = t - i;
        var terraced = 1 - (i + Smooth(0.3f, 1, u)) / Terraces;
        return 0.65f * terraced + 0.35f * (1 - s);
    }

    static Model Build(Model model, Func<float, Vector2> edge, int seed, Vector3 inches)
    {
        var rng = new Random(seed);
        var pen = new Pen(model[Stone], inches);
        float Theta(int j) => 2 * MathF.PI * j / Segments;
        float TopEdge(float t) => Top - 0.05f * Noise(MathF.Cos(t) * 2.2f + 3, MathF.Sin(t) * 2.2f + 3, 0, seed);   // the plateau never reaches past Top
        float Wander(float t) => 0;   // the terraces stay level: each riser carries a band of cliff
        float Shifted(float t, float s) => Math.Clamp(s + Wander(t) * MathF.Sin(MathF.PI * s), 0, 1);
        // Lumps on the slope, none at the plateau's edge or the foot: the turf over buried stone.
        float Lump(float t, float s) => 0.05f * (Fbm(MathF.Cos(t) * 9, MathF.Sin(t) * 9, s * 4, seed + 20) - 0.5f) * MathF.Sin(MathF.PI * s);
        float Height(float t, float s) => Math.Clamp(Slope(Shifted(t, s)) + Lump(t, s), 0, 0.995f);
        Vector3 At(float t, float s) { var top = TopEdge(t); var e = edge(t) * (top + (1 - top) * s); return new Vector3(e.X, Height(t, s), e.Y) * inches; }

        var grass = Rgb(0.4f, 0.49f, 0.22f); var dryGrass = Rgb(0.58f, 0.56f, 0.32f); var earth = Rgb(0.4f, 0.31f, 0.21f);
        Vector4 Colour(Vector3 p, Vector3 n)
        {
            var q = p * inches;
            var mottle = Fbm(q.X * 1.2f, q.Z * 1.2f, q.Y, seed);
            var turf = Mix(Shade(grass, 0.78f + 0.3f * mottle + 0.16f * Fbm(q.X * 4, q.Z * 4, 2, seed + 5)), dryGrass, Smooth(0.52f, 0.72f, Fbm(q.X * 0.8f, q.Z * 0.8f, 5, seed + 3)) * 0.75f);
            turf = Mix(turf, earth, Smooth(0.66f, 0.76f, Fbm(q.X * 1.1f, q.Z * 1.1f, 9, seed + 6)) * 0.6f);
            var steep = Smooth(0.75f, 0.45f, Vector3.Normalize(n / inches).Y);   // n is the unit box's; judged as drawn
            return Mix(turf, Shade(earth, 0.8f + 0.35f * Fbm(q * 3, seed + 4)), steep * 0.8f);
        }
        model[Ground].Grid(TopRings + SlopeRings + 1, Segments, wrap: true, (i, j) =>
        {
            var t = Theta(j);
            var top = TopEdge(t);
            if (i <= TopRings) { var e0 = edge(t) * top * i / TopRings; return new Vector3(e0.X, 1, e0.Y); }
            var s = (float)(i - TopRings) / SlopeRings;
            var e = edge(t) * (top + (1 - top) * s);
            return new Vector3(e.X, Height(t, s), e.Y);
        }, Colour);

        // Outcrops: stratified rock breaking through each terrace's riser, long along the contour.
        var grey = new[] { Rgb(0.45f, 0.43f, 0.4f), Rgb(0.49f, 0.45f, 0.4f), Rgb(0.41f, 0.4f, 0.38f) };
        var moss = Rgb(0.42f, 0.48f, 0.24f);
        Func<Vector3, Vector3, int, Vector4> Rock(int k) => (p, n, layer) =>
            Shade(Mix(grey[(layer + k) % grey.Length], moss, Smooth(0.7f, 0.95f, n.Y) * Smooth(0.45f, 0.62f, Fbm(p * 1.8f, k)) * 0.75f), 0.92f + 0.14f * Fbm(p * 1.4f, k + 1));
        Vector4 Loose(Vector3 p, Vector3 n) => Shade(grey[(int)(Fbm(p * 5, seed) * 7) % grey.Length], 0.82f);
        // Each riser a band of cliff a little proud of the turf, jointed and stepped, sinking back under it where the turf runs down.
        for (var k = 0; k < Terraces; k++)
        {
            float sLo = (k + 1f) / Terraces, sHi = (k + 0.3f) / Terraces;
            var (yLo, yHi) = (Slope(sLo) * inches.Y, Slope(sHi) * inches.Y);
            float Across(float t, float s) { var top = TopEdge(t); return edge(t).Length() * 2 * inches.X / 2 * (top + (1 - top) * s); }
            var band = k;
            float Outline(float t) => MathF.Min(Across(t, sLo) * (1.075f - 0.1f * Smooth(0.6f, 0.72f, Noise(MathF.Cos(t) * 3.5f + band * 7, MathF.Sin(t) * 3.5f, band, seed + 30))), Across(t, 1) * 0.995f);
            Crag.Stack(pen, new Crag.Strata
            {
                Outline = Outline, Band = true, Base = yLo - 0.12f, Height = yHi + 0.02f, Radii = new Vector2(Across(0, sLo)), Profile = y =>
                {
                    // The turf's own profile: the s where the slope is at height y, as a fraction of the riser's foot.
                    float a = sHi, b = sLo;
                    for (var i = 0; i < 16; i++) { var m = (a + b) / 2; if (Slope(m) * inches.Y > y) a = m; else b = m; }
                    const float top = Top - 0.025f;
                    return (top + (1 - top) * a) / (top + (1 - top) * sLo);
                },
                Layers = 2, Cuts = 40, LayerCuts = 14, Cracks = 12, Chips = 30, ChipScale = 0.04f, CutDepth = 0.1f, Columns = 144, RowStep = 0.09f,
                Lumps = 0.006f, Wobble = 0.008f, Inset = 0.012f, Groove = 0.014f, Lip = 0.006f, Shift = 0.004f, Dip = 0.03f, DipToward = seed + k, Seed = seed * 10 + k,
            }, Rock(k));
        }
        // Boulders fallen onto the ledges.
        for (var k = 0; k < 9; k++)
        {
            var t = MathF.Tau * (k + 0.6f * (float)rng.NextDouble()) / 9;
            var s = (rng.Next(Terraces) + 0.12f) / Terraces;
            var size = 0.22f + 0.2f * (float)rng.NextDouble();
            Crag.Chunk(pen, rng, At(t, s) + Vector3.UnitY * size * 0.2f, new Vector3(size * 1.3f, size * 0.8f, size), t, 0.2f, 0.1f, Loose, 14);
        }
        // Rubble on the ledges and at the foot; tufts of static grass along the ledges.
        Crag.Scree(pen, rng, 110, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau;
            var s = r.NextDouble() < 0.4 ? 0.97f : (r.Next(Terraces) + 0.05f + 0.25f * (float)r.NextDouble()) / Terraces;
            return (At(t, s), 0.05f + 0.12f * MathF.Pow((float)r.NextDouble(), 2));
        }, Loose);
        Crag.Tufts(model[Grass], inches, rng, 140, r => At((float)r.NextDouble() * MathF.Tau, (r.Next(Terraces) + 0.02f + 0.3f * (float)r.NextDouble()) / Terraces),
            0.22f, Rgb(0.3f, 0.38f, 0.15f), Rgb(0.66f, 0.66f, 0.36f));
        return model;
    }
}
