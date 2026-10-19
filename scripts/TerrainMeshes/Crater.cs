using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A blast crater (round, 6" across, 1" high, Area): a scorched floor near the table, a rim of churned earth thrown up
/// round it, plates of the old ground torn up and tilted outward along the crest, clods and broken stone sprayed out across the
/// outer slope, and twisted shards of metal in the floor.</summary>
public static class Crater
{
    public static readonly Material Metal = new("metal", 0.45f, Metallic: 0.7f), Earth = new("earth", 0.95f), Stone = new("debris", 0.88f),
        Grass = new("grass", 0.9f, DoubleSided: true);
    const float R = 3, Top = 1;

    public static Model Build()
    {
        var model = new Model("crater", round: true, new Vector3(2 * R, Top, 2 * R));
        var rng = new Random(111);
        var pen = new Pen(model[Stone]);
        float Rim(float t) => 1.95f + 0.25f * (Noise(MathF.Cos(t) * 2.5f + 5, MathF.Sin(t) * 2.5f + 5, 0, 111) - 0.5f);
        float Crest(float t) => 0.42f + 0.22f * Fbm(MathF.Cos(t) * 2 + 1, MathF.Sin(t) * 2 + 1, 0, 112);
        float Height(float x, float z)
        {
            var r = MathF.Sqrt(x * x + z * z); var t = MathF.Atan2(z, x);
            var rim = Rim(t); var crest = Crest(t);
            var y = r >= rim ? crest * MathF.Pow(Smooth(R, rim, r), 1.5f) : 0.05f + (crest - 0.05f) * MathF.Pow(Smooth(0.3f, rim, r), 2.4f);
            var churn = 0.14f * (Fbm(x * 2.2f, z * 2.2f, 0, 113) - 0.5f) * Smooth(0.2f, rim, r) * Smooth(R, rim + 0.4f, r);
            return MathF.Max(0.02f, y + churn + 0.03f * (Fbm(x * 5, z * 5, 0, 118) - 0.5f));
        }
        var soil = Rgb(0.38f, 0.3f, 0.21f); var churned = Rgb(0.44f, 0.35f, 0.25f); var scorched = Rgb(0.25f, 0.21f, 0.18f); var ash = Rgb(0.17f, 0.16f, 0.15f);
        var turf = Rgb(0.34f, 0.4f, 0.2f); var ember = Rgb(0.32f, 0.2f, 0.12f);
        Shapes.Disc(model, Earth, 40, 120, Height, (p, n) =>
        {
            var r = MathF.Sqrt(p.X * p.X + p.Z * p.Z); var t = MathF.Atan2(p.Z, p.X);
            var streak = Smooth(0.45f, 0.7f, Noise(t * 5, r * 0.3f, 0, 115));   // burn streaks thrown out from the middle
            var c = Mix(ash, scorched, Smooth(0.3f, 1.5f, r));
            c = Mix(c, ember, Smooth(0.6f, 0.75f, Fbm(p.X * 2, p.Z * 2, 0, 116)) * Smooth(1.6f, 0.4f, r) * 0.6f);
            c = Mix(c, churned, Smooth(1.5f, 2.1f, r));
            c = Mix(c, soil, Smooth(2.2f, 2.8f, r) * (1 - streak * 0.6f));
            c = Mix(c, turf, Smooth(2.55f, 2.95f, r) * Smooth(0.4f, 0.6f, Fbm(p.X * 1.5f, p.Z * 1.5f, 3, 117)) * 0.8f);
            c = Mix(c, scorched, streak * Smooth(3, 1.8f, r) * 0.5f);
            return Shade(c, 0.85f + 0.25f * Fbm(p.X * 6, p.Z * 6, p.Y * 4, 114));
        });
        float Y(float x, float z) => Height(x, z);
        Vector3 Polar(float t, float r) { var (x, z) = (r * MathF.Cos(t), r * MathF.Sin(t)); return new Vector3(x, Y(x, z), z); }
        var stone = new[] { Rgb(0.44f, 0.41f, 0.37f), Rgb(0.38f, 0.35f, 0.31f), Rgb(0.5f, 0.46f, 0.4f) };
        Vector4 Clod(Vector3 p, Vector3 n) => Shade(Mix(churned, soil, Fbm(p * 3, 120)), 0.68f + 0.15f * n.Y);
        Vector4 Rubble(Vector3 p, Vector3 n) => Shade(Mix(stone[(int)(Fbm(p * 5, 121) * 9) % 3], scorched, Smooth(1.2f, 0.3f, MathF.Sqrt(p.X * p.X + p.Z * p.Z)) * 0.7f), 0.85f);
        // Torn-up plates of the old ground along the crest: slabs tilted outward, turf still on their tops.
        var earthMat = new Pen(model[Earth]);
        for (var k = 0; k < 34; k++)
        {
            var t = MathF.Tau * (k + 0.8f * (float)rng.NextDouble()) / 34;
            var r = Rim(t) + 0.15f * ((float)rng.NextDouble() - 0.3f);
            var at = Polar(t, r);
            var s = 0.35f + 0.35f * (float)rng.NextDouble();
            var size = new Vector3(s * 0.9f, s * 0.16f, s * (0.8f + 0.6f * (float)rng.NextDouble()));
            var pitch = 0.5f + 0.5f * (float)rng.NextDouble();
            Crag.Chunk(earthMat, rng, at + Vector3.UnitY * s * 0.05f, size, MathF.PI / 2 - t, 0, -pitch, (p, n) =>
                n.Y > 0.88f ? Shade(turf, 0.7f + 0.25f * Fbm(p * 4, 122)) : Shade(Mix(soil, churned, Fbm(p * 4, 123)), 0.72f), 12);
        }
        Crag.Scree(pen, rng, 50, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = Rim(t) + 0.5f * ((float)r.NextDouble() - 0.4f);
            return (Polar(t, d), 0.12f + 0.2f * (float)r.NextDouble());
        }, Rubble);
        // Clods and broken stone thrown out over the rim and down the outer slope, thinning outward; scorched rubble in the floor.
        Crag.Scree(earthMat, rng, 130, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = Rim(t) - 0.2f + 1.2f * MathF.Pow((float)r.NextDouble(), 1.6f);
            return d > R - 0.08f ? null : (Polar(t, d), 0.06f + 0.16f * MathF.Pow((float)r.NextDouble(), 2));
        }, Clod);
        Crag.Scree(pen, rng, 120, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 0.2f + (R - 0.3f) * MathF.Sqrt((float)r.NextDouble());
            return (Polar(t, d), 0.04f + 0.12f * MathF.Pow((float)r.NextDouble(), 2));
        }, Rubble);
        // Twisted shards of metal standing in the floor.
        for (var k = 0; k < 6; k++)
        {
            var t = (float)rng.NextDouble() * MathF.Tau; var d = 0.2f + 1.1f * (float)rng.NextDouble();
            var at = Polar(t, d);
            var len = 0.35f + 0.35f * (float)rng.NextDouble();
            Crag.Chunk(new Pen(model[Metal]), rng, at + Vector3.UnitY * len * 0.25f, new Vector3(0.05f, len, 0.18f + 0.1f * (float)rng.NextDouble()),
                t, 0.3f + 0.5f * (float)rng.NextDouble(), 0.4f * ((float)rng.NextDouble() - 0.5f), (p, n) => Shade(Rgb(0.3f, 0.27f, 0.25f), 0.8f + 0.3f * n.Y), 10);
        }
        // Grass tufts surviving on the outer edge.
        Crag.Tufts(model[Grass], Vector3.One, rng, 40, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 2.5f + 0.42f * (float)r.NextDouble();
            return Polar(t, d);
        }, 0.2f, Rgb(0.32f, 0.38f, 0.16f), Rgb(0.66f, 0.64f, 0.36f));
        return model;
    }
}
