using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>
/// The flat-topped hills (client UX spec → Physical collision): a grass plateau at y = 1 over the inner <see cref="Top"/> of the
/// footprint, and a terraced slope of earth and rock strata down to the footprint's edge, with rock outcrops, bushes and grass
/// tufts. <see cref="Round"/> fills the unit circle, <see cref="Square"/> a rounded unit square.
/// </summary>
public static class Hill
{
    /// <summary>The plateau's extent as a fraction of the way from the centre to the edge (a 15% rim each side).</summary>
    public const float Top = 0.7f;
    const int Segments = 96, TopRings = 5, SlopeRings = 22, Terraces = 3;

    public static readonly Material Ground = new("ground", 0.92f), Stone = new("stone", 0.85f), Grass = new("grass", 0.9f, DoubleSided: true), Shrub = new("shrub", 0.85f);

    public static Model Round() => Build(new Model("hill", round: true), t => new Vector2(MathF.Cos(t), MathF.Sin(t)) * 0.5f, 11);

    public static Model Square() => Build(new Model("hill-rect", round: false), t => Superellipse(t, 8) * 0.5f, 12);

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

    static Model Build(Model model, Func<float, Vector2> edge, int seed)
    {
        var rng = new Random(seed);
        float Theta(int j) => 2 * MathF.PI * j / Segments;
        float TopEdge(float t) => Top - 0.05f * Noise(MathF.Cos(t) * 2.2f + 3, MathF.Sin(t) * 2.2f + 3, 0, seed);   // the plateau never reaches past Top
        float Wander(float t) => 0.12f * (Noise(MathF.Cos(t) * 1.7f + 9, MathF.Sin(t) * 1.7f + 9, 1, seed) - 0.5f);  // the terraces' lines drift around the hill
        (float Fraction, float Y, float S) Ring(int i, float t)
        {
            var top = TopEdge(t);
            if (i <= TopRings) return (top * i / TopRings, 1, -1);
            var s = (float)(i - TopRings) / SlopeRings;
            var shifted = Math.Clamp(s + Wander(t) * MathF.Sin(MathF.PI * s), 0, 1);
            return (top + (1 - top) * s, Slope(shifted), s);
        }
        var grass = Rgb(0.42f, 0.52f, 0.24f); var dryGrass = Rgb(0.55f, 0.55f, 0.3f); var earth = Rgb(0.42f, 0.32f, 0.22f); var rock = Rgb(0.46f, 0.44f, 0.41f); var darkRock = Rgb(0.36f, 0.34f, 0.32f);
        Vector4 Colour(Vector3 p, Vector3 n)
        {
            var mottle = Fbm(p.X * 9, p.Z * 9, p.Y, seed);
            var turf = Mix(Shade(grass, 0.8f + 0.4f * mottle), dryGrass, Smooth(0.6f, 0.8f, Fbm(p.X * 4, p.Z * 4, 5, seed + 3)) * 0.6f);
            var band = Fbm(p.Y * 14, p.X * 2, p.Z * 2, seed + 7);
            var strata = Mix(Mix(earth, rock, Smooth(0.35f, 0.65f, band)), darkRock, Smooth(0.15f, 0, p.Y) * 0.6f);
            var flat = Smooth(0.2f, 0.42f, n.Y);
            return Mix(Shade(strata, 0.85f + 0.3f * mottle), turf, flat);
        }
        model[Ground].Grid(TopRings + SlopeRings + 1, Segments, wrap: true, (i, j) =>
        {
            var t = Theta(j); var (f, y, _) = Ring(i, t); var e = edge(t) * f;
            return new Vector3(e.X, y, e.Y);
        }, Colour);

        // Outcrops on the risers: tall in the unit box, since a hill is fitted three or four times wider than it is high.
        for (var k = 0; k < 9; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI);
            var terrace = rng.Next(Terraces);
            var s = (terrace + 0.62f + 0.12f * (float)rng.NextDouble()) / Terraces;
            var top = TopEdge(t); var e = edge(t) * (top + (1 - top) * s * 0.97f);
            var y = Slope(s);
            var r = 0.025f + 0.025f * (float)rng.NextDouble();
            var centre = new Vector3(e.X, y - 0.02f, e.Y);
            var cap = y + 0.06f;
            Shapes.Blob(model[Stone], centre, new Vector3(r, r * 2.6f, r * 1.2f), 1, 0.3f, seed * 31 + k, (p, n) =>
                Mix(Shade(rock, 0.8f + 0.3f * Fbm(p * 30, k)), Rgb(0.5f, 0.55f, 0.32f), Smooth(0.7f, 0.95f, n.Y) * 0.7f), yaw: t,
                shape: q => q with { Y = MathF.Min(q.Y, cap) });
        }
        // Bushes on the ledges and tufts of grass along the plateau's edge and the ledges.
        for (var k = 0; k < 7; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI);
            var s = (rng.Next(Terraces) + 0.12f) / Terraces;
            var top = TopEdge(t); var e = edge(t) * (top + (1 - top) * s);
            var r = 0.018f + 0.012f * (float)rng.NextDouble();
            Shapes.Blob(model[Shrub], new Vector3(e.X, Slope(s) + r * 0.8f, e.Y), new Vector3(r, r * 2.2f, r), 1, 0.35f, seed * 17 + k,
                (p, n) => Shade(Rgb(0.3f, 0.42f, 0.18f), 0.75f + 0.4f * Fbm(p * 40, k)));
        }
        for (var k = 0; k < 70; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI);
            var onTop = k < 30;
            var top = TopEdge(t);
            var s = onTop ? 0 : (rng.Next(Terraces) + 0.1f * (float)rng.NextDouble()) / Terraces;
            var f = onTop ? top * (0.75f + 0.24f * (float)rng.NextDouble()) : top + (1 - top) * s;
            var e = edge(t) * f;
            var root = new Vector3(e.X, onTop ? 1 : Slope(s), e.Y);
            Shapes.Tuft(model[Grass], rng, root, 4, 0.05f, 0.008f, 0.4f, Rgb(0.3f, 0.4f, 0.16f), Rgb(0.62f, 0.66f, 0.34f));
        }
        return model;
    }
}
