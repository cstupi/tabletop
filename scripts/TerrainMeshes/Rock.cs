using System.Numerics;
using static Tabletop.TerrainMeshes.Paint;

namespace Tabletop.TerrainMeshes;

/// <summary>A large rock (4" × 4", 4" high, Solid, impassable): a stratified crag cut by joint faces, its strata stepping back in
/// ledges, cracked and chipped, its top flat at 4" (the volume's top); a lower tilted crag and a stub against it, fallen blocks and
/// scree of broken chunks at the foot, on a flocked base with tufts. Grey-brown stone, a tone per stratum and face, lichen on the
/// ledges.</summary>
public static class Rock
{
    public static readonly Material Stone = new("rock", 0.82f), Ground = new("ground", 0.95f), Grass = new("grass", 0.9f, DoubleSided: true);
    const float Size = 4, Top = 4;

    public static Model Build()
    {
        var model = new Model("rock", round: false, new Vector3(Size, Top, Size));
        var rng = new Random(61);
        var pen = new Pen(model[Stone]);
        var strata = new[] { Rgb(0.42f, 0.4f, 0.37f), Rgb(0.45f, 0.41f, 0.36f), Rgb(0.39f, 0.38f, 0.36f), Rgb(0.47f, 0.43f, 0.38f), Rgb(0.41f, 0.39f, 0.35f) };
        var lichen = Rgb(0.46f, 0.48f, 0.3f); var dark = Rgb(0.28f, 0.27f, 0.25f);
        Func<Vector3, Vector3, int, Vector4> Paint(int seed) => (p, n, layer) =>
        {
            var c = strata[(layer + seed) % strata.Length];
            c = Mix(c, dark, Smooth(0.5f, 0.02f, p.Y) * 0.5f);
            c = Mix(c, lichen, Smooth(0.75f, 0.97f, n.Y) * Smooth(0.52f, 0.68f, Fbm(p * 1.6f, seed)) * 0.7f);
            return Shade(c, 0.92f + 0.14f * Fbm(p * 1.2f, seed + 1));
        };
        Vector4 Loose(Vector3 p, Vector3 n) => Shade(Mix(strata[(int)(Fbm(p * 4, 70) * 9) % strata.Length], lichen, Smooth(0.85f, 1, n.Y) * 0.25f), 0.8f);

        // The crag: a tower to the top, a shoulder against it, a tilted low block and a stub.
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = new Vector2(-0.35f, 0.1f), Base = -0.1f, Height = Top, Radii = new Vector2(1.45f, 1.25f), Yaw = 0.3f, Layers = 7, Cuts = 9, Cracks = 5,
            Chips = 16, Columns = 84, RowStep = 0.13f, Taper = 0.42f, Lumps = 0.1f, Inset = 0.07f, Groove = 0.05f, Dip = 0.14f, DipToward = 0.8f, Seed = 3,
        }, Paint(0));
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = new Vector2(0.62f, -0.5f), Base = -0.1f, Height = 2.5f, Radii = new Vector2(1.05f, 0.85f), Yaw = 0.8f, Layers = 4, Cuts = 7, Cracks = 3,
            Chips = 8, Columns = 64, RowStep = 0.13f, Taper = 0.25f, Inset = 0.07f, Groove = 0.05f, Dip = 0.14f, DipToward = 0.8f, Seed = 4,
        }, Paint(1));
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = new Vector2(1.1f, 0.95f), Base = -0.3f, Height = 1.35f, Radii = new Vector2(0.7f, 0.5f), Yaw = 1.1f, Pitch = 0.14f, Roll = -0.12f,
            Layers = 3, Cuts = 5, Cracks = 2, Chips = 5, Columns = 64, RowStep = 0.1f, Taper = 0.25f, Seed = 5,
        }, Paint(2));
        Crag.Stack(pen, new Crag.Strata
        {
            Centre = new Vector2(-1.35f, 1.15f), Base = -0.2f, Height = 0.7f, Radii = new Vector2(0.45f, 0.38f), Yaw = 2.1f, Pitch = -0.15f,
            Layers = 2, Cuts = 4, Cracks = 1, Chips = 3, Columns = 44, RowStep = 0.1f, Taper = 0.25f, Seed = 7,
        }, Paint(4));
        // Fallen blocks leaning at the foot, then scree in a skirt round it.
        foreach (var (x, z, s) in new[] { (0.95f, -1.2f, 0.55f), (-1.45f, -0.7f, 0.45f), (1.5f, -0.1f, 0.4f), (0.1f, 1.45f, 0.5f), (-0.85f, -1.5f, 0.3f) })
            Crag.Chunk(pen, rng, new Vector3(x, s * 0.3f, z), new Vector3(s * 1.3f, s * 0.8f, s), (float)rng.NextDouble() * 6.3f, 0.3f, -0.2f, Loose, 16);
        Vector3? Skirt(Random r)
        {
            var t = (float)r.NextDouble() * MathF.Tau; var d = 1.25f + 0.65f * (float)r.NextDouble();
            var at = new Vector2(-0.1f + MathF.Cos(t) * d * 1.05f, MathF.Sin(t) * d);
            return MathF.Abs(at.X) > 1.85f || MathF.Abs(at.Y) > 1.85f ? null : new Vector3(at.X, 0.06f, at.Y);
        }
        Crag.Scree(pen, rng, 140, r => Skirt(r) is { } at ? (at, 0.05f + 0.16f * MathF.Pow((float)r.NextDouble(), 2)) : null, Loose);

        // The base: earth and static grass, its edge bevelled.
        var earth = Rgb(0.4f, 0.31f, 0.21f); var flock = Rgb(0.42f, 0.48f, 0.22f); var straw = Rgb(0.62f, 0.56f, 0.32f);
        Crag.Base(model, Ground, Vector3.One, Vector2.Zero, t => 1.9f + 0.18f * (Noise(MathF.Cos(t) * 1.5f + 2, MathF.Sin(t) * 1.5f, 0, 62) - 0.5f) + 0.25f * Corner(t),
            14, 96, 0.25f, (x, z) => 0.07f + 0.02f * Fbm(x * 3, z * 3, 0, 63), (p, n) =>
            {
                var c = Mix(earth, flock, Smooth(0.4f, 0.6f, Fbm(p.X * 1.6f, p.Z * 1.6f, 0, 64)));
                c = Mix(c, straw, Smooth(0.6f, 0.75f, Fbm(p.X * 2.5f, p.Z * 2.5f, 3, 65)) * 0.6f);
                return Shade(c, 0.85f + 0.25f * Fbm(p.X * 12, p.Z * 12, 1, 66));
            });
        Crag.Tufts(model[Grass], Vector3.One, rng, 46, r => Skirt(r), 0.22f, Rgb(0.33f, 0.38f, 0.16f), Rgb(0.7f, 0.68f, 0.38f));
        return model;
    }

    /// <summary>0 along the square's sides' middles, 1 toward its corners: lets a round base fill a square footprint.</summary>
    static float Corner(float t) => MathF.Pow(MathF.Abs(MathF.Sin(2 * t)), 3);
}
