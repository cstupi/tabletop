using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The death-planet growths, each round, 6" across and 6" high. <see cref="Deadly"/>: carnivorous and thorned alien plants
/// spaced on a sickly purple mat: pitcher plants, snapping traps on stalks with teeth round their jaws, barbed tendrils, glowing
/// spore pods. <see cref="Jungle"/>: a thick jungle: palms with drooping fronds and broad-crowned trees, hanging vines, and a
/// floor packed with big-leaved undergrowth.</summary>
public static class Vegetation
{
    public static readonly Material Mat = new("mat", 0.9f), Flesh = new("plant", 0.6f), Leaf = new("leaf", 0.75f, DoubleSided: true),
        Thorn = new("thorn", 0.7f), Glow = new("spores", 0.4f, Emissive: new Vector3(0.55f, 0.75f, 0.15f)), Bark = new("bark", 0.9f), Canopy = new("canopy", 0.8f);
    const float D = 6;

    static Vector3 At(float r, float t, float y = 0) => new(r * MathF.Cos(t), y, r * MathF.Sin(t));

    public static Model Deadly()
    {
        var model = new Model("deadly-vegetation", round: true, new Vector3(D, D, D));
        var rng = new Random(281);
        var purple = Rgb(0.3f, 0.2f, 0.26f); var bruise = Rgb(0.42f, 0.24f, 0.3f); var decay = Rgb(0.28f, 0.3f, 0.16f);
        Shapes.Disc(model, Mat, 10, 48, (x, z) => 0.05f + 0.04f * Fbm(x, z, 0, 281) - 0.04f * MathF.Max(0, MathF.Sqrt(x * x + z * z) - 2.6f),
            (p, n) => Shade(Mix(Mix(purple, bruise, Fbm(p.X, p.Z, 1, 282)), decay, Smooth(0.55f, 0.7f, Fbm(p.X * 1.5f + 3, p.Z * 1.5f, 2, 283))), 0.8f + 0.3f * Fbm(p.X * 4, p.Z * 4, 3, 284)));
        var skin = Rgb(0.36f, 0.5f, 0.18f); var lip = Rgb(0.55f, 0.1f, 0.18f); var maw = Rgb(0.2f, 0.03f, 0.06f); var tooth = Rgb(0.9f, 0.86f, 0.72f);
        // Pitcher plants: flared tubes, green at the foot, blood red at the lip, dark inside, with a hooded lid.
        foreach (var (r, t, h, k) in new[] { (1.7f, 0.3f, 1.9f, 0), (2.1f, 1.0f, 1.2f, 1), (0.4f, 3.4f, 2.4f, 2), (2.2f, 4.6f, 1.5f, 3), (0.8f, 5.2f, 1.0f, 4) })
        {
            var foot = At(r, t, 0.05f);
            var w = 0.2f + 0.02f * k;
            Shapes.Prism(model[Flesh], Shapes.At(foot, new Vector3(w, h * 0.7f, w)), 12, 1.25f, p => Mix(skin, lip, Smooth(foot.Y + h * 0.3f, foot.Y + h * 0.7f, p.Y)));
            Shapes.Prism(model[Flesh], Shapes.At(foot + Vector3.UnitY * h * 0.7f, new Vector3(w * 1.25f, h * 0.3f, w * 1.25f)), 12, 1.5f, p => Mix(lip, Rgb(0.85f, 0.3f, 0.25f), Smooth(foot.Y + h * 0.75f, foot.Y + h, p.Y)));
            Shapes.Prism(model[Flesh], Shapes.At(foot + Vector3.UnitY * h, new Vector3(w * 1.7f, 0.01f, w * 1.7f)), 12, 1, maw);
            Shapes.Box(model[Flesh], Shapes.At(foot + new Vector3(0, h + 0.25f, 0) - At(w * 1.5f, t, 0), new Vector3(w * 1.6f, 0.04f, w * 1.4f), -t, 0, 0.9f), Mix(skin, lip, 0.4f));
            for (var v = 0; v < (int)(h / 0.3f); v++) Shapes.Box(model[Flesh], Shapes.At(foot + Vector3.UnitY * (h * 0.1f + 0.25f * v), new Vector3(w * 2.06f, 0.05f, w * 2.06f), v * 0.5f), Shade(skin, 0.7f));
        }
        // Snapping traps: a curved stalk up to a head of two jaws open toward the viewer, teeth round each rim.
        foreach (var (r, t, h, k) in new[] { (0.9f, 1.6f, 3.6f, 0), (2.3f, 2.6f, 2.8f, 1), (1.4f, 5.6f, 4.4f, 2) })
        {
            var foot = At(r, t, 0.05f); var lean = At(0.5f, t + 0.8f);
            var head = foot + lean + Vector3.UnitY * h;
            Shapes.Chain(model[Flesh], [foot, foot + lean * 0.3f + Vector3.UnitY * h * 0.4f, foot + lean * 0.8f + Vector3.UnitY * h * 0.8f, head], 0.13f, 0.07f, 7, skin);
            for (var l = 0; l < 3; l++)   // leaves at the stalk's foot
            {
                var a = t + l * 2.1f; var o = At(1, a);
                Shapes.Box(model[Flesh], Shapes.At(foot + o * 0.45f + Vector3.UnitY * 0.12f, new Vector3(0.7f, 0.05f, 0.3f), -a, 0, 0.35f), Shade(skin, 0.85f));
            }
            var face = -t;   // the jaws open on the plane facing out from the plant's foot
            foreach (var j in new[] { -1f, 1f })
            {
                var rot = Matrix4x4.CreateRotationZ(j * 0.55f) * Matrix4x4.CreateRotationY(face);
                var centre = head + Vector3.Transform(new Vector3(0, j * 0.18f, 0), Matrix4x4.CreateRotationY(face));
                Shapes.Blob(model[Flesh], Vector3.Zero, new Vector3(0.55f, 0.18f, 0.45f), 1, 0.15f, 300 + k * 2 + (int)j,
                    (q, n) => n.Y * j > 0 ? Mix(skin, Rgb(0.5f, 0.55f, 0.2f), 0.4f) : lip, shape: q => centre + Vector3.Transform(q with { Y = j > 0 ? MathF.Max(q.Y, 0) : MathF.Min(q.Y, 0) }, rot));
                for (var i = 0; i < 9; i++)
                {
                    var a = MathF.PI * (i + 0.5f) / 9 - MathF.PI / 2;
                    var rim = Vector3.Transform(new Vector3(0.52f * MathF.Cos(a), 0, 0.42f * MathF.Sin(a)), rot);
                    var tip = Vector3.Transform(new Vector3(0.52f * MathF.Cos(a), -j * 0.22f, 0.42f * MathF.Sin(a)), rot);
                    Shapes.Limb(model[Thorn], centre + rim, centre + tip, 0.035f, 0, 4, tooth);
                }
            }
            Shapes.Blob(model[Flesh], head, new Vector3(0.38f, 0.12f, 0.32f), 1, 0.1f, 320 + k, (_, _) => maw, yaw: face);
        }
        // Barbed tendrils: curling up out of the mat, thorns along them.
        var vine = Rgb(0.3f, 0.12f, 0.16f);
        for (var k = 0; k < 7; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.6f + 2f * (float)rng.NextDouble();
            var foot = At(r, t, 0.04f); var h = 1.6f + 2f * (float)rng.NextDouble(); var spin = (float)rng.NextDouble() * 6;
            var pts = Enumerable.Range(0, 9).Select(i => { var u = i / 8f; return foot + At(0.35f * u + 0.15f * MathF.Sin(u * 5), spin + u * 4, h * MathF.Pow(u, 0.8f)); }).ToList();
            Shapes.Chain(model[Thorn], pts, 0.09f, 0.025f, 6, vine);
            for (var i = 1; i < 8; i++)
            {
                var dir = Vector3.Normalize(pts[i + 1] - pts[i - 1]); var o = Vector3.Normalize(Vector3.Cross(dir, At(1, i * 2.3f)));
                Shapes.Limb(model[Thorn], pts[i], pts[i] + o * 0.22f + dir * 0.05f, 0.04f, 0, 4, Rgb(0.75f, 0.68f, 0.5f));
            }
        }
        // Spore pods: glowing bulbs on short stems, in clusters.
        for (var k = 0; k < 6; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 0.4f + 2.2f * (float)rng.NextDouble();
            for (var b = 0; b < 4; b++)
            {
                var foot = At(r, t, 0.05f) + At(0.25f, b * 1.7f); var h = 0.3f + 0.5f * (float)rng.NextDouble();
                Shapes.Limb(model[Flesh], foot, foot + Vector3.UnitY * h, 0.04f, 0.6f, 5, skin);
                Shapes.Blob(model[Glow], foot + Vector3.UnitY * (h + 0.12f), new Vector3(0.14f, 0.18f, 0.14f), 1, 0.1f, 340 + k * 4 + b, (_, n) => Rgb(0.82f, 0.95f, 0.4f), smooth: true);
            }
        }
        return model;
    }

    public static Model Jungle()
    {
        var model = new Model("jungle", round: true, new Vector3(D, D, D));
        var rng = new Random(301);
        var loam = Rgb(0.22f, 0.18f, 0.11f); var moss = Rgb(0.2f, 0.34f, 0.12f);
        Shapes.Disc(model, Mat, 10, 48, (x, z) => 0.06f + 0.05f * Fbm(x, z, 0, 301) - 0.05f * MathF.Max(0, MathF.Sqrt(x * x + z * z) - 2.6f),
            (p, n) => Shade(Mix(loam, moss, Smooth(0.4f, 0.6f, Fbm(p.X * 1.2f, p.Z * 1.2f, 1, 302))), 0.8f + 0.3f * Fbm(p.X * 4, p.Z * 4, 3, 303)));
        var bark = Rgb(0.4f, 0.32f, 0.22f);
        // Palms: a ringed trunk curving up, a crown of long fronds drooping from it.
        foreach (var (r, t, h, k) in new[] { (2.1f, 0.3f, 5.6f, 0), (1.0f, 2.2f, 5.0f, 1), (2.3f, 3.7f, 4.6f, 2), (1.9f, 5.2f, 5.8f, 3), (0.2f, 4.4f, 4.2f, 4) })
        {
            var foot = At(r, t, 0.05f); var lean = At(0.5f + 0.3f * (float)rng.NextDouble(), t + MathF.PI + 0.5f * ((float)rng.NextDouble() - 0.5f));
            var pts = Enumerable.Range(0, 6).Select(i => { var u = i / 5f; return foot + lean * u * u + Vector3.UnitY * (h - 0.6f) * u; }).ToList();
            Shapes.Chain(model[Bark], pts, 0.16f, 0.1f, 7, p => Shade(bark, 0.8f + 0.25f * MathF.Floor(Noise(0, p.Y * 6, 0, k) * 2)));
            var crown = pts[^1];
            for (var f = 0; f < 9; f++)
            {
                var a = 2 * MathF.PI * f / 9 + (float)rng.NextDouble() * 0.4f; var o = At(1, a); var side = new Vector3(-o.Z, 0, o.X);
                var length = 1.5f + 0.5f * (float)rng.NextDouble();
                var spine = Enumerable.Range(0, 6).Select(i => { var u = i / 5f; return crown + o * length * u + Vector3.UnitY * (0.5f * u - 1.4f * u * u); }).ToList();
                var green = Shade(Rgb(0.22f, 0.5f, 0.16f), 0.8f + 0.35f * (float)rng.NextDouble());
                for (var i = 0; i < 5; i++)
                {
                    var w0 = 0.3f * MathF.Sin(MathF.PI * (i + 0.2f) / 5.4f); var w1 = 0.3f * MathF.Sin(MathF.PI * (i + 1.2f) / 5.4f);
                    model[Leaf].Quad(spine[i] - side * w0 - Vector3.UnitY * w0 * 0.3f, spine[i + 1] - side * w1 - Vector3.UnitY * w1 * 0.3f, spine[i + 1], spine[i], Shade(green, 0.9f));
                    model[Leaf].Quad(spine[i], spine[i + 1], spine[i + 1] + side * w1 - Vector3.UnitY * w1 * 0.3f, spine[i] + side * w0 - Vector3.UnitY * w0 * 0.3f, green);
                }
            }
            Shapes.Blob(model[Bark], crown, new Vector3(0.25f, 0.2f, 0.25f), 1, 0.2f, 310 + k, (_, _) => Rgb(0.45f, 0.36f, 0.2f));
        }
        // Broad crowns: big lumpy canopies on buttressed trunks, filling the space between the palms high up.
        foreach (var (r, t, h, k) in new[] { (1.3f, 1.1f, 4.4f, 0), (1.6f, 4.3f, 5.0f, 1), (0.8f, 6.0f, 4.8f, 2) })
        {
            var foot = At(r, t, 0.05f);
            Shapes.Limb(model[Bark], foot, foot + Vector3.UnitY * h * 0.85f, 0.2f, 0.6f, 8, p => Shade(Rgb(0.46f, 0.4f, 0.32f), 0.8f + 0.2f * Fbm(p * 3, k)));
            for (var b = 0; b < 4; b++) { var o = At(1, b * 1.6f + k); Shapes.Limb(model[Bark], foot + o * 0.45f, foot + Vector3.UnitY * 0.8f, 0.09f, 0.4f, 4, Rgb(0.42f, 0.36f, 0.28f)); }
            for (var c = 0; c < 4; c++)
            {
                var o = At(0.7f * (c == 0 ? 0 : 1), c * 2.1f + k);
                var centre = foot + o + Vector3.UnitY * (h - 0.6f - 0.3f * (c == 0 ? 0 : 1));
                var size = c == 0 ? 1.1f : 0.85f;
                Shapes.Blob(model[Canopy], centre, new Vector3(size, size * 0.6f, size), 2, 0.3f, 330 + k * 4 + c,
                    (q, n) => Shade(Rgb(0.22f, 0.5f, 0.16f), 0.7f + 0.35f * Fbm(q * 12, k + c) + 0.25f * MathF.Max(n.Y, 0)));
            }
            for (var v = 0; v < 5; v++)   // hanging vines
            {
                var from = foot + At(0.6f + 0.4f * (float)rng.NextDouble(), (float)rng.NextDouble() * 6) + Vector3.UnitY * (h - 1.1f);
                var drop = 1.2f + 2f * (float)rng.NextDouble();
                Shapes.Chain(model[Bark], [from, from + At(0.1f, v) - Vector3.UnitY * drop * 0.5f, from + At(0.15f, v + 1) - Vector3.UnitY * drop], 0.035f, 0.025f, 4, Rgb(0.25f, 0.35f, 0.14f));
            }
        }
        // Undergrowth: broad heart-shaped leaves on stems, packed over the floor, and ferns.
        for (var k = 0; k < 70; k++)
        {
            var t = (float)(rng.NextDouble() * 2 * MathF.PI); var r = 2.75f * MathF.Sqrt((float)rng.NextDouble());
            var foot = At(r, t, 0.08f);
            var green = Shade(Mix(Rgb(0.2f, 0.46f, 0.14f), Rgb(0.36f, 0.56f, 0.18f), (float)rng.NextDouble()), 0.75f + 0.3f * (float)rng.NextDouble());
            for (var l = 0; l < 3; l++)
            {
                var a = (float)rng.NextDouble() * 6.3f; var o = At(1, a); var side = new Vector3(-o.Z, 0, o.X);
                var h = 0.4f + 0.9f * (float)rng.NextDouble(); var size = 0.35f + 0.35f * (float)rng.NextDouble();
                var stem = foot + o * 0.15f + Vector3.UnitY * h;
                Shapes.Limb(model[Flesh], foot, stem, 0.025f, 0.7f, 3, Rgb(0.26f, 0.4f, 0.14f));
                var tip = stem + o * size * 1.6f - Vector3.UnitY * size * 0.6f; var mid = stem + o * size * 0.7f + Vector3.UnitY * 0.08f;
                model[Leaf].Tri(stem, mid - side * size * 0.6f, tip, Shade(green, 0.85f));
                model[Leaf].Tri(stem, tip, mid + side * size * 0.6f, green);
            }
        }
        return model;
    }
}
