using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The beast lair (round, 4" across, 2" high, Solid): a rocky mound with its top flat at 2", a dark cave mouth cut into one
/// side under a lintel of boulders, a trampled floor before it, and bones strewn there: a skull, ribs, long bones.</summary>
public static class Lair
{
    public static readonly Material Rock = new("rock", 0.85f), Cave = new("cave", 1f), Bone = new("bone", 0.6f), Grass = new("grass", 0.9f, DoubleSided: true);
    const float R = 2, Top = 2, Mouth = 0.5f;   // Mouth: the cave faces +x, its half-angle in radians

    public static Model Build()
    {
        var model = new Model("beast-lair", round: true, new Vector3(2 * R, Top, 2 * R));
        var rng = new Random(381);
        float Height(float x, float z)
        {
            var r = MathF.Sqrt(x * x + z * z); var t = MathF.Atan2(z, x);
            var s = Smooth(0.75f, 1.95f, r);                                             // 0 on the plateau, 1 at the rim
            var y = Top * (1 - MathF.Pow(s, 1.6f)) + 0.12f * Fbm(x * 1.5f, z * 1.5f, 0, 381) * Smooth(0, 0.3f, s) * Smooth(1, 0.7f, s);
            var notch = Smooth(Mouth + 0.15f, Mouth - 0.1f, MathF.Abs(t)) * Smooth(0.75f, 1.1f, r);   // the gully up to the mouth
            return MathF.Min(y, Top) * (1 - notch) + MathF.Min(y, 0.15f) * notch;
        }
        var grey = Rgb(0.55f, 0.52f, 0.47f); var dark = Rgb(0.36f, 0.34f, 0.31f); var moss = Rgb(0.34f, 0.36f, 0.2f); var dirt = Rgb(0.36f, 0.29f, 0.2f);
        Shapes.Disc(model, Rock, 26, 72, Height, (p, n) =>
        {
            var c = Mix(grey, dark, Smooth(0.4f, 0.7f, Fbm(p.X * 1.5f, p.Y * 6, p.Z * 1.5f, 382)));
            c = Mix(c, moss, Smooth(0.75f, 0.95f, n.Y) * Smooth(1.8f, 2, p.Y) * 0.7f);
            var t = MathF.Atan2(p.Z, p.X);
            c = Mix(c, dirt, Smooth(0.3f, 0.12f, p.Y) * Smooth(Mouth + 0.4f, Mouth, MathF.Abs(t)));
            return Shade(c, 0.8f + 0.3f * Fbm(p * 6, 383));
        });
        // The mouth: a black arch at the gully's head, framed by two boulders and a lintel slab over it.
        var head = 0.8f;
        var arch = new List<Vector3>();
        for (var i = 0; i <= 12; i++) { var a = MathF.PI * i / 12; arch.Add(new Vector3(head, 0.12f + 1.05f * MathF.Sin(a), -0.62f * MathF.Cos(a))); }
        for (var i = 0; i < 12; i++)
            Shapes.Oriented(model[Cave], [new Vector3(head, 0.12f, arch[i].Z), new Vector3(head, 0.12f, arch[i + 1].Z), arch[i + 1], arch[i]], Vector3.UnitX, _ => Rgb(0.03f, 0.025f, 0.02f));
        Shapes.Box(model[Cave], new Vector3(0.3f, 0, -0.6f), new Vector3(head, 0.13f, 0.6f), Rgb(0.05f, 0.04f, 0.03f));   // the dark floor going in
        Func<Vector3, Vector3, Vector4> Stone(int seed) => (q, n) => Shade(Mix(grey, dark, Fbm(q * 4, seed)), 0.8f + 0.3f * Fbm(q * 9, seed + 1));
        Shapes.Blob(model[Rock], new Vector3(1.05f, 0.5f, -0.78f), new Vector3(0.4f, 0.6f, 0.35f), 1, 0.3f, 390, Stone(390), yaw: 0.4f);
        Shapes.Blob(model[Rock], new Vector3(1.05f, 0.45f, 0.78f), new Vector3(0.38f, 0.55f, 0.36f), 1, 0.3f, 391, Stone(391), yaw: 1.1f);
        Shapes.Blob(model[Rock], new Vector3(0.95f, 1.28f, 0), new Vector3(0.38f, 0.22f, 0.85f), 1, 0.2f, 392, Stone(392));
        // Bones on the trampled floor before the mouth and down the gully.
        var bone = Rgb(0.86f, 0.82f, 0.7f); var old = Rgb(0.7f, 0.64f, 0.5f);
        void Long(Vector3 a, Vector3 b, float r)
        {
            Shapes.Limb(model[Bone], a, b, r, 0.8f, 6, bone);
            Shapes.Blob(model[Bone], a, new Vector3(r * 1.8f), 0, 0.1f, 400, (_, _) => old);
            Shapes.Blob(model[Bone], b, new Vector3(r * 1.6f), 0, 0.1f, 401, (_, _) => old);
        }
        for (var k = 0; k < 7; k++)
        {
            var t = ((float)rng.NextDouble() - 0.5f) * 1.2f; var r = 1.2f + 0.65f * (float)rng.NextDouble();
            var at = new Vector3(r * MathF.Cos(t), 0, r * MathF.Sin(t)); at.Y = Height(at.X, at.Z) + 0.03f;
            var a = (float)rng.NextDouble() * 6.3f; var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (0.18f + 0.12f * (float)rng.NextDouble());
            Long(at - o, at + o + Vector3.UnitY * 0.02f, 0.035f);
        }
        // A rib cage: curved ribs off a spine, lying on its side.
        var spine = new Vector3(1.45f, 0.08f, -0.35f);
        Shapes.Limb(model[Bone], spine, spine + new Vector3(0.1f, 0, 0.6f), 0.04f, 0.8f, 6, old);
        for (var i = 0; i < 5; i++)
        {
            var b = spine + new Vector3(0.02f * i, 0, 0.12f * i);
            Shapes.Chain(model[Bone], [b, b + new Vector3(0.15f, 0.2f, 0.02f), b + new Vector3(0.35f, 0.22f, 0.04f), b + new Vector3(0.45f, 0.05f, 0.05f)], 0.025f, 0.015f, 4, bone);
        }
        // The skull: a cranium, a long muzzle, dark eye sockets and two fangs.
        var skull = new Vector3(1.35f, 0.14f, 0.45f);
        Shapes.Blob(model[Bone], skull, new Vector3(0.15f, 0.12f, 0.13f), 1, 0.05f, 410, (_, _) => bone, smooth: true);
        Shapes.Limb(model[Bone], skull + new Vector3(0.08f, -0.02f, 0), skull + new Vector3(0.32f, -0.05f, 0.02f), 0.075f, 0.6f, 6, bone);
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.Blob(model[Cave], skull + new Vector3(0.1f, 0.04f, s * 0.07f), new Vector3(0.035f), 0, 0, 411, (_, _) => Rgb(0.05f, 0.04f, 0.03f));
            Shapes.Limb(model[Bone], skull + new Vector3(0.26f, -0.07f, s * 0.035f), skull + new Vector3(0.27f, -0.13f, s * 0.035f), 0.014f, 0, 4, bone);
        }
        // Tufts of dry grass on the plateau and ledges.
        for (var k = 0; k < 30; k++)
        {
            var t = (float)rng.NextDouble() * 6.3f; var r = 1.9f * MathF.Sqrt((float)rng.NextDouble());
            var x = r * MathF.Cos(t); var z = r * MathF.Sin(t);
            if (MathF.Abs(t) < Mouth + 0.2f && r > 0.7f || Height(x, z) > Top - 0.2f) continue;
            Shapes.Tuft(model[Grass], rng, new Vector3(x, Height(x, z), z), 5, 0.14f, 0.03f, 0.6f, Rgb(0.4f, 0.4f, 0.2f), Rgb(0.7f, 0.66f, 0.4f));
        }
        return model;
    }
}
