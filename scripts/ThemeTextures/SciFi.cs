using System.Numerics;
using static Tabletop.TableTextures.Field;
using static Tabletop.ThemeTextures.Shapes;

namespace Tabletop.ThemeTextures;

/// <summary>The sci-fi starship console: brushed gunmetal hull plates with seams, recessed bolts and a faint etched schematic,
/// chamfered corners, a cyan light strip along the top edge and an amber status lamp; backlit console keys; a scanline screen
/// for inputs; deep space (a faint nebula under a tiled starfield) behind full-screen pages.</summary>
public static class SciFi
{
    const string Id = "scifi";

    const int PW = 192, PH = 128, PM = 16, PInset = 6, PCut = 10;
    const int BW = 48, BH = 40, BM = 10, BInset = 4, BCut = 6;
    const int IW = 40, IH = 36, IM = 8, IInset = 4, ICut = 4;

    static readonly Vector3 Cyan = Rgb(0.247f, 0.816f, 0.878f), Amber = Rgb(1f, 0.69f, 0.125f), Chrome = Rgb(0.42f, 0.48f, 0.86f);

    public static IReadOnlyList<Sheet> All { get; } =
    [
        new(Id, "panel", PW, PH, Use.NinePatch, PanelRelief, PanelAt, PM, PInset),
        Button("button", Key.Normal), Button("button-hover", Key.Hover), Button("button-pressed", Key.Pressed),
        Button("button-disabled", Key.Disabled), Button("button-primary", Key.Primary),
        new(Id, "input", IW, IH, Use.NinePatch, InputRelief, InputAt, IM, IInset),
        new(Id, "backdrop-tile", 512, 512, Use.Tile, (_, _) => 0, StarsAt),
        new(Id, "backdrop", 480, 270, Use.Stretch, (_, _) => 0, NebulaAt, Opaque: true),
    ];

    // ---- Panel ------------------------------------------------------------------------------------------------------------

    const int Px = PW - 2 * PM, Py = PH - 2 * PM;

    /// <summary>The etched schematic on each plate: traces with 45° bends and round pads, in plate coordinates.</summary>
    static readonly Vector2[][] Traces =
    [
        [new(18, 28), new(52, 28), new(64, 40), new(112, 40)],
        [new(30, 72), new(70, 72), new(80, 62), new(138, 62), new(138, 82)],
        [new(96, 14), new(96, 22), new(104, 30), new(146, 30)],
    ];

    static (float Seam, float Bolt, float Ring) Plate(float x, float y)
    {
        float lx = Mod(x - PM, Px), ly = Mod(y - PM, Py);
        var seam = MathF.Max(Smooth(1.6f, 0.3f, MathF.Min(lx, Px - lx)), Smooth(1.6f, 0.3f, MathF.Min(ly, Py - ly)));
        var bolt = 99f;
        foreach (var b in new Vector2[] { new(7, 7), new(Px - 7, 7), new(7, Py - 7), new(Px - 7, Py - 7) }) bolt = MathF.Min(bolt, (new Vector2(lx, ly) - b).Length());
        return (seam, bolt, bolt);
    }

    static float PanelRelief(float x, float y)
    {
        var d = Chamfer(x, y, PW, PH, PCut);
        if (d < 1) return 0.6f * Bevel(d, 1);
        if (d < 2.5f) return -0.6f;
        if (d < 6) return 0.9f * MathF.Sin(MathF.PI * (d - 2.5f) / 3.5f);
        var (seam, bolt, _) = Plate(x, y);
        var h = -0.9f * seam;
        if (bolt < 3.4f) h = bolt < 2.2f ? Dome(bolt, 2.2f, 0.9f) - 0.3f : -0.7f;
        return h;
    }

    static Surface PanelAt(float x, float y)
    {
        var d = Chamfer(x, y, PW, PH, PCut);
        if (d <= 0) return Surface.Clear;
        float u = (x - PM) / Px, v = (y - PM) / Py;
        var top = y < MathF.Min(MathF.Min(x, PW - x), PH - y);
        if (d < 1) return new(Chrome * 0.55f, 1, 0.5f, 30);
        if (d < 2.5f)
        {
            var strip = top ? Smooth(PCut, PCut + 14, MathF.Min(x, PW - x)) : 0;
            return new(Rgb(0.025f, 0.032f, 0.04f), 1, 0, 8, Cyan * 0.85f * strip);
        }
        if (d < 6)
        {
            var rail = Rgb(0.15f, 0.175f, 0.205f) * (0.92f + 0.12f * Gradient(u, v, 4, 64, 1300));
            // The amber status lamp on the bottom rail at the right corner.
            var lamp = MathF.Abs(x - (PW - 22)) < 3 && MathF.Abs(PH - y - 4.2f) < 1 ? 1f : 0f;
            return new(rail * (1 - lamp) + Amber * 0.3f * lamp, 1, 0.35f, 24, Amber * 0.7f * lamp);
        }
        var brushed = 0.92f + 0.07f * Gradient(u, v, 3, Py, 1301) + 0.04f * Gradient(u, v, 11, Py / 2, 1302) + 0.05f * (Fbm(u, v, 2, 3, 1303) - 0.5f);
        var c = Rgb(0.098f, 0.118f, 0.142f) * brushed;
        float lx = Mod(x - PM, Px), ly = Mod(y - PM, Py);
        var grid = (Mod(lx, 16) < 0.8f || Mod(ly, 16) < 0.8f) ? 1f : 0f;
        var trace = 0f;
        foreach (var t in Traces)
        {
            for (var i = 0; i + 1 < t.Length; i++) trace = MathF.Max(trace, Smooth(0.9f, 0.3f, Segment(new(lx, ly), t[i], t[i + 1])));
            foreach (var pad in new[] { t[0], t[^1] }) { var r = (new Vector2(lx, ly) - pad).Length(); trace = MathF.Max(trace, Smooth(0.6f, 0.2f, MathF.Abs(r - 2.2f))); }
        }
        c += Rgb(0.01f, 0.013f, 0.016f) * grid + Chrome * 0.03f * trace;
        var (seam, bolt, _) = Plate(x, y);
        c *= 1 - 0.65f * seam;
        if (bolt < 3.4f) c = bolt < 2.2f ? Rgb(0.2f, 0.225f, 0.25f) : Rgb(0.03f, 0.035f, 0.04f);
        var glow = top ? Cyan * 0.07f * MathF.Exp(-(y - 6) / 3f) * Smooth(PCut, PCut + 14, MathF.Min(x, PW - x)) : Vector3.Zero;
        c *= 0.75f + 0.25f * Smooth(6, 11, d);                                                                // the rail's shadow
        return new(c, 0.96f, bolt < 2.2f ? 0.5f : 0.12f, bolt < 2.2f ? 30 : 14, glow);
    }

    // ---- Buttons ----------------------------------------------------------------------------------------------------------

    enum Key { Normal, Hover, Pressed, Disabled, Primary }

    static Sheet Button(string name, Key look) => new(Id, name, BW, BH, Use.NinePatch, (x, y) => ButtonRelief(x, y, look), (x, y) => ButtonAt(x, y, look), BM, BInset);

    static float ButtonRelief(float x, float y, Key look)
    {
        var d = Chamfer(x, y, BW, BH, BCut);
        if (d < 1) return 0;
        if (d < 2.6f) return 0.8f * MathF.Sin(MathF.PI * (d - 1) / 1.6f);
        return look == Key.Pressed ? -0.4f * Bevel(d - 2.6f, 2) : 0;
    }

    static Surface ButtonAt(float x, float y, Key look)
    {
        var d = Chamfer(x, y, BW, BH, BCut);
        if (d <= 0) return Surface.Clear;
        var (line, glow, fill) = look switch
        {
            Key.Hover => (0.65f, 0.3f, 0.07f), Key.Pressed => (0.95f, 0.45f, 0.16f), Key.Primary => (1f, 0.42f, 0.3f),
            Key.Disabled => (0f, 0f, 0f), _ => (0.22f, 0.06f, 0f),
        };
        if (d < 1) return new(Rgb(0.02f, 0.025f, 0.03f));
        if (d < 2.6f)
        {
            var metal = look == Key.Disabled ? Rgb(0.13f, 0.14f, 0.15f) : Rgb(0.24f, 0.28f, 0.33f);
            return new(metal, 1, look == Key.Disabled ? 0.05f : 0.55f, 30);
        }
        if (d < 3.4f) return new(Rgb(0.02f, 0.03f, 0.035f), 1, 0, 8, Cyan * line);
        var face = look == Key.Disabled ? Rgb(0.055f, 0.06f, 0.065f) : Rgb(0.03f, 0.045f, 0.06f);
        var reflection = look == Key.Disabled ? 0 : Smooth(BH * 0.5f, BH * 0.42f, y) * 0.022f;                 // the glass catches the light above
        face += new Vector3(reflection);
        if (look == Key.Pressed) face *= 0.6f + 0.4f * Smooth(3.4f, 9, y);
        var emit = Cyan * (glow * MathF.Exp(-(d - 3.4f) / 3.2f) + fill);
        return new(face, 1, 0.2f, 40, emit);
    }

    // ---- Input ------------------------------------------------------------------------------------------------------------

    static float InputRelief(float x, float y)
    {
        var d = Chamfer(x, y, IW, IH, ICut);
        if (d < 1) return 0.4f;
        if (d < 2.5f) return 0.4f - 1.8f * Smooth(1, 2.5f, d);
        return -1.4f;
    }

    static Surface InputAt(float x, float y)
    {
        var d = Chamfer(x, y, IW, IH, ICut);
        if (d <= 0) return Surface.Clear;
        if (d < 1) return new(Rgb(0.2f, 0.235f, 0.27f), 1, 0.4f, 24);
        if (d < 2.5f) return new(Rgb(0.015f, 0.02f, 0.025f));
        var scan = Mod(y, 2) < 1 ? 0.012f : 0;
        var dot = Mod(x, 2) < 1 && Mod(y, 2) < 1 ? 0.006f : 0;
        var c = Rgb(0.016f, 0.028f, 0.036f) + new Vector3(scan + dot);
        c *= 0.6f + 0.4f * Smooth(2.5f, 7, y);
        return new(c, 1, 0, 8, Cyan * 0.02f);
    }

    // ---- Backdrop ---------------------------------------------------------------------------------------------------------

    /// <summary>One star per cell (with a chance), kept inside its cell with room for its glow: the intensity and colour at a point.</summary>
    static (float, Vector3) Star(float x, float y, int cell, int seed, float chance, float rMin, float rMax, float halo, float spikes)
    {
        int n = 512 / cell, i = (int)MathF.Floor(x / cell), j = (int)MathF.Floor(y / cell);
        int wi = ((i % n) + n) % n, wj = ((j % n) + n) % n;
        if (Unit(wi, wj, seed) > chance) return (0, Vector3.Zero);
        var cx = (i + 0.25f + 0.5f * Unit(wi, wj, seed + 1)) * cell; var cy = (j + 0.25f + 0.5f * Unit(wi, wj, seed + 2)) * cell;
        var b = 0.35f + 0.65f * MathF.Pow(Unit(wi, wj, seed + 3), 2);
        var r = rMin + (rMax - rMin) * Unit(wi, wj, seed + 4);
        float dx = x - cx, dy = y - cy, d = MathF.Sqrt(dx * dx + dy * dy);
        var k = MathF.Exp(-(d * d) / (r * r)) + halo * MathF.Exp(-d / (r * 3));
        if (spikes > 0) k += spikes * (MathF.Exp(-MathF.Abs(dx) / 0.45f) * MathF.Exp(-MathF.Abs(dy) / 7) + MathF.Exp(-MathF.Abs(dy) / 0.45f) * MathF.Exp(-MathF.Abs(dx) / 7));
        var t = Unit(wi, wj, seed + 5);
        var colour = t < 0.6f ? Rgb(0.82f, 0.9f, 1f) : t < 0.85f ? Rgb(1, 1, 1) : Rgb(1, 0.86f, 0.68f);
        return (b * k, colour);
    }

    static Surface StarsAt(float x, float y)
    {
        float total = 0; var colour = Vector3.Zero;
        foreach (var (k, c) in new[]
                 {
                     Star(x, y, 8, 1400, 0.42f, 0.3f, 0.55f, 0, 0), Star(x, y, 16, 1410, 0.3f, 0.45f, 0.8f, 0.08f, 0),
                     Star(x, y, 64, 1420, 0.4f, 0.7f, 1.1f, 0.18f, 0), Star(x, y, 128, 1430, 0.45f, 0.9f, 1.3f, 0.25f, 0.4f),
                 })
        { total += k; colour += c * k; }
        if (total <= 0.004f) return Surface.Clear;
        return new(colour / total, MathF.Min(1, total));
    }

    /// <summary>Deep space under the stars: a faint violet and teal nebula along a diagonal band, darker at the edges.</summary>
    static Surface NebulaAt(float x, float y)
    {
        float u = x / 480, v = y / 270;
        var qx = Fbm(u * 2, v * 1.2f, 2, 4, 1500); var qy = Fbm(u * 2 + 5.2f, v * 1.2f + 1.3f, 2, 4, 1501);
        var n = Fbm(u * 2 + 0.9f * qx, v * 1.2f + 0.9f * qy, 3, 5, 1502);
        var band = MathF.Exp(-MathF.Pow((v - 0.25f - 0.5f * u) / 0.33f, 2));
        var density = Smooth(0.4f, 0.85f, n) * (0.35f + 0.65f * band);
        var hue = Smooth(0.35f, 0.65f, Fbm(u * 1.5f + 3, v + 2, 2, 3, 1503));
        var cloud = Mix(Rgb(0.20f, 0.06f, 0.30f), Rgb(0.03f, 0.17f, 0.24f), hue);
        var r = new Vector2((u - 0.5f) * 1.3f, v - 0.5f).Length();
        var c = Rgb(0.008f, 0.011f, 0.024f) + cloud * 0.55f * density * density + Rgb(0.02f, 0.025f, 0.05f) * band * 0.5f;
        return new(c * (1 - 0.55f * Smooth(0.35f, 0.9f, r)));
    }
}
