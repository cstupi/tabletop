using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>The beast lair (round, 4" across, 2" high, Solid): a stratified rock den, jointed and ledged, its top flat at 2"; an
/// arched cave mouth cut deep into one side under an overhanging lintel of strata, boulders flanking it, a trampled floor before
/// it on a flocked base, and bones strewn there: a skull, a rib cage, long bones; scree round the foot.</summary>
public static class Lair
{
    public static readonly Material Rock = new("rock", 0.85f), Cave = new("cave", 1f), Bone = new("bone", 0.6f), Grass = new("grass", 0.9f, DoubleSided: true),
        Ground = new("ground", 0.95f);
    const float R = 2, Top = 2, Mouth = 0.55f;   // Mouth: the cave faces +x, its half-angle in radians

    public static Model Build()
    {
        var model = new Model("beast-lair", round: true, new Vector3(2 * R, Top, 2 * R));
        var rng = new Random(381);
        var pen = new Pen(model[Rock]);
        var strata = new[] { Rgb(0.44f, 0.41f, 0.37f), Rgb(0.4f, 0.37f, 0.34f), Rgb(0.48f, 0.44f, 0.38f), Rgb(0.37f, 0.35f, 0.33f) };
        var moss = Rgb(0.4f, 0.44f, 0.22f); var dark = Rgb(0.07f, 0.06f, 0.05f); var dirt = Rgb(0.47f, 0.38f, 0.27f);
        var centre = new Vector2(-0.35f, 0);
        const float arch = 1.35f;
        // The cave: in from the +x side under an arch, back to a rounded end; the walls darken going in.
        float Carve(float t, float y, float r)
        {
            var a = MathF.Abs(MathF.IEEERemainder(t, MathF.Tau)) / Mouth;
            if (a >= 1) return r;
            var height = arch * MathF.Sqrt(1 - a * a) * (0.9f + 0.1f * Noise(t * 4, 0, 0, 384));
            return y < height ? MathF.Min(r, 0.35f + 0.3f * a * a) : r;
        }
        Func<Vector3, Vector3, int, Vector4> Paint(int seed, bool den) => (p, n, layer) =>
        {
            var c = strata[(layer + seed) % strata.Length];
            c = Mix(c, moss, Smooth(0.75f, 0.97f, n.Y) * Smooth(0.48f, 0.64f, Fbm(p * 1.6f, seed)) * 0.75f);
            c = Shade(c, 0.92f + 0.14f * Fbm(p * 1.3f, seed + 1));
            if (!den) return c;
            var q = new Vector2(p.X - centre.X, p.Z - centre.Y);
            var inside = Smooth(1.2f, 0.6f, q.Length()) * Smooth(arch + 0.1f, arch - 0.2f, p.Y) * Smooth(0.2f, 0.8f, q.X / MathF.Max(q.Length(), 1e-3f));
            return Mix(c, dark, inside * 0.9f);
        };
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = centre, Base = -0.1f, Height = Top, Radii = new Vector2(1.55f, 1.45f), Layers = 5, Cuts = 9, Cracks = 4, Chips = 10, Columns = 100,
            RowStep = 0.1f, Taper = 0.3f, Lumps = 0.1f, Inset = 0.06f, Groove = 0.05f, Dip = 0.1f, DipToward = 2.5f, Carve = Carve, Seed = 381,
        }, Paint(0, true));
        // Boulders flanking the mouth and a smaller crag behind.
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = new Vector2(-1.2f, -1.0f), Base = -0.2f, Height = 1.1f, Radii = new Vector2(0.6f, 0.5f), Yaw = 0.7f, Pitch = 0.1f, Layers = 3, Cuts = 5,
            Cracks = 1, Chips = 4, Columns = 56, RowStep = 0.1f, Taper = 0.3f, Seed = 385,
        }, Paint(1, false));
        Vector4 Loose(Vector3 p, Vector3 n) => Shade(strata[(int)(Fbm(p * 5, 386) * 9) % strata.Length], 0.82f);
        foreach (var (x, z, s) in new[] { (1.15f, -0.82f, 0.62f), (1.2f, 0.85f, 0.55f), (0.95f, -1.25f, 0.35f), (-0.4f, 1.55f, 0.4f) })
            Crag.Chunk(pen, rng, new Vector3(x, s * 0.35f, z), new Vector3(s * 1.2f, s * 1.1f, s), (float)rng.NextDouble() * 6.3f, 0.15f, -0.1f, Loose, 18);
        Crag.Scree(pen, rng, 120, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 1.45f + 0.45f * (float)r.NextDouble();
            if (MathF.Abs(MathF.IEEERemainder(t, MathF.Tau)) < Mouth + 0.15f && d < 1.7f) return null;
            return (new Vector3(centre.X * 0.3f + d * MathF.Cos(t), 0.05f, d * MathF.Sin(t)), 0.04f + 0.13f * MathF.Pow((float)r.NextDouble(), 2));
        }, Loose);
        // The trampled floor and the flocked base.
        var flock = Rgb(0.38f, 0.42f, 0.2f);
        Crag.Base(model, Ground, Vector3.One, Vector2.Zero, t => R - 0.02f, 12, 96, 0.2f, (x, z) => 0.05f + 0.015f * Fbm(x * 3, z * 3, 0, 387), (p, n) =>
        {
            var c = Mix(dirt, flock, Smooth(0.45f, 0.62f, Fbm(p.X * 1.4f, p.Z * 1.4f, 0, 388)) * Smooth(1.2f, 1.8f, MathF.Abs(MathF.Atan2(p.Z, p.X))));
            return Shade(c, 0.82f + 0.25f * Fbm(p.X * 9, p.Z * 9, 1, 389));
        });

        // Bones on the trampled floor before the mouth.
        var bone = Rgb(0.84f, 0.8f, 0.68f); var old = Rgb(0.68f, 0.62f, 0.48f);
        const float floor = 0.05f;
        void Long(Vector3 a, Vector3 b, float r)
        {
            Shapes.Limb(model[Bone], a, b, r, 0.8f, 6, bone);
            Shapes.Blob(model[Bone], a, new Vector3(r * 1.8f), 0, 0.1f, 400, (_, _) => old);
            Shapes.Blob(model[Bone], b, new Vector3(r * 1.6f), 0, 0.1f, 401, (_, _) => old);
        }
        for (var k = 0; k < 7; k++)
        {
            var t = ((float)rng.NextDouble() - 0.5f) * 1.3f; var r = 1.3f + 0.5f * (float)rng.NextDouble();
            var at = new Vector3(r * MathF.Cos(t), floor + 0.03f, r * MathF.Sin(t));
            var a = (float)rng.NextDouble() * 6.3f; var o = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (0.16f + 0.1f * (float)rng.NextDouble());
            Long(at - o, at + o + Vector3.UnitY * 0.02f, 0.032f);
        }
        // A rib cage: curved ribs off a spine, lying on its side.
        var spine = new Vector3(1.35f, floor + 0.04f, -0.4f);
        Shapes.Limb(model[Bone], spine, spine + new Vector3(0.1f, 0, 0.55f), 0.04f, 0.8f, 6, old);
        for (var i = 0; i < 5; i++)
        {
            var b = spine + new Vector3(0.02f * i, 0, 0.11f * i);
            Shapes.Chain(model[Bone], [b, b + new Vector3(0.14f, 0.18f, 0.02f), b + new Vector3(0.3f, 0.2f, 0.04f), b + new Vector3(0.4f, 0.04f, 0.05f)], 0.024f, 0.014f, 4, bone);
        }
        // The skull: a cranium, a long muzzle, dark eye sockets and two fangs.
        var skull = new Vector3(1.3f, floor + 0.11f, 0.42f);
        Shapes.Blob(model[Bone], skull, new Vector3(0.14f, 0.11f, 0.12f), 1, 0.05f, 410, (_, _) => bone, smooth: true);
        Shapes.Limb(model[Bone], skull + new Vector3(0.08f, -0.02f, 0), skull + new Vector3(0.3f, -0.05f, 0.02f), 0.07f, 0.6f, 6, bone);
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.Blob(model[Cave], skull + new Vector3(0.1f, 0.04f, s * 0.065f), new Vector3(0.032f), 0, 0, 411, (_, _) => Rgb(0.05f, 0.04f, 0.03f));
            Shapes.Limb(model[Bone], skull + new Vector3(0.24f, -0.07f, s * 0.033f), skull + new Vector3(0.25f, -0.12f, s * 0.033f), 0.013f, 0, 4, bone);
        }
        // Tufts of dry grass round the foot, away from the trampled floor.
        Crag.Tufts(model[Grass], Vector3.One, rng, 36, r =>
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 1.5f + 0.42f * (float)r.NextDouble();
            return MathF.Abs(MathF.IEEERemainder(t, MathF.Tau)) < Mouth + 0.5f ? null : new Vector3(d * MathF.Cos(t), floor, d * MathF.Sin(t));
        }, 0.2f, Rgb(0.4f, 0.4f, 0.2f), Rgb(0.7f, 0.66f, 0.4f));
        return model;
    }
}
