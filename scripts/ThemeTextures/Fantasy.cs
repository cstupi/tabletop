using System.Numerics;
using static Tabletop.TableTextures.Field;
using static Tabletop.ThemeTextures.Shapes;

namespace Tabletop.ThemeTextures;

/// <summary>The fantasy war table: dark oiled walnut panels framed in a pitted iron strip with brass corner brackets, wooden
/// plaques with a brass rim for buttons (the primary red lacquer), a parchment field cut into the wood for inputs, and a
/// dark green leather table top under a lamp-lit vignette behind full-screen pages.</summary>
public static class Fantasy
{
    const string Id = "fantasy";

    // The panel: 3 planks of 40 a repeat down, a 240 repeat along; frame 8 wide, brackets inside the 24 corner.
    const int PW = 288, PH = 168, PM = 24, PInset = 8;
    // A button plaque and an input groove.
    const int BW = 84, BH = 40, BM = 12, BInset = 4;
    const int IW = 48, IH = 36, IM = 8, IInset = 4;

    public static IReadOnlyList<Sheet> All { get; } =
    [
        new(Id, "panel", PW, PH, Use.NinePatch, PanelRelief, PanelAt, PM, PInset),
        Button("button", Plaque.Normal), Button("button-hover", Plaque.Hover), Button("button-pressed", Plaque.Pressed),
        Button("button-disabled", Plaque.Disabled), Button("button-primary", Plaque.Primary),
        new(Id, "input", IW, IH, Use.NinePatch, InputRelief, InputAt, IM, IInset),
        new(Id, "backdrop-tile", 256, 256, Use.Tile, LeatherRelief, LeatherAt, Opaque: true),
        new(Id, "backdrop", 384, 216, Use.Stretch, (_, _) => 0, LampAt),
    ];

    // ---- Wood -------------------------------------------------------------------------------------------------------------

    /// <summary>Oiled walnut: planks <paramref name="plank"/> tall (0: one board), grain lines along x that wander, long streaks,
    /// pores, a tone per plank; periodic over px × py from (ox, oy). Returns the albedo and a relief (seams sunk, grain faint).</summary>
    static (Vector3 Albedo, float Relief) Wood(float x, float y, float ox, float oy, int px, int py, float plank, int seed, Vector3 dark, Vector3 light)
    {
        float lx = x - ox, ly = y - oy, u = lx / px, v = ly / py;
        var planks = plank > 0 ? (int)MathF.Round(py / plank) : 1;
        var p = plank > 0 ? ((int)MathF.Floor(ly / plank) % planks + planks) % planks : 0;
        var fy = plank > 0 ? Mod(ly, plank) : 99;
        var tone = Unit(p, 0, seed);
        var lines = py / 3.4f;                                                                              // a grain line every ~3 px
        var phase = MathF.Tau * (v * MathF.Round(lines) + 1.4f * Gradient(u, v, 3, 2, seed + 11 * p) + 0.45f * Gradient(u, v, 9, 5, seed + 3 + p));
        var line = 0.5f + 0.5f * MathF.Sin(phase);
        var late = Smooth(0.62f, 0.98f, line);                                                               // the dark latewood
        var streak = Gradient(u, v, 2, Math.Max(2, py / 6), seed + 5 + p);
        var figure = Fbm(u, v, 2, 3, seed + 7 + p);
        var pore = Unit(((int)MathF.Floor(lx / 5) % (px / 5) + px / 5) % (px / 5), ((int)MathF.Floor(ly) % py + py) % py, seed + 9) < 0.07f ? 1f : 0f;
        var t = 0.5f + 0.22f * streak + 0.28f * (tone - 0.5f) + 0.22f * (figure - 0.5f) - 0.3f * late;
        var c = Mix(dark, light, t) * (1 - 0.3f * pore);
        var relief = 0.25f * (1 - late) - 0.25f * pore;
        if (plank > 0)
        {
            var joint = Unit(p, 1, seed) * px;                                                               // an end joint in each plank
            var along = MathF.Abs(Mod(lx - joint + px / 2f, px) - px / 2f);
            var seam = MathF.Max(Smooth(1.6f, 0.2f, MathF.Min(fy, plank - fy)), Smooth(1.2f, 0.2f, along));
            c *= 1 - 0.6f * seam; relief -= 1.2f * seam;
        }
        return (c, relief);
    }

    static readonly Vector3 WalnutDark = Rgb(0.075f, 0.045f, 0.026f), WalnutLight = Rgb(0.25f, 0.155f, 0.085f);

    // ---- Metal ------------------------------------------------------------------------------------------------------------

    /// <summary>Wrought iron, darkened in its recesses (grime toward its edges), rusted in patches, pitted and scratched.
    /// a runs along the strip (period pa), b across it (0 to width).</summary>
    static (Vector3 Albedo, float Relief, float Spec) Iron(float x, float y, float u, float v, float a, float b, float pa, float width, int seed)
    {
        var c = Rgb(0.20f, 0.195f, 0.185f) * (0.8f + 0.4f * Fbm(u, v, 6, 3, seed));
        var rust = Smooth(0.58f, 0.72f, Fbm(u, v, 3, 4, seed + 1));
        c = Mix(c, Rgb(0.24f, 0.12f, 0.06f), 0.75f * rust);
        var (f1, _, id) = Cells(u, v, 48, seed + 2);
        var pit = (id & 3) == 0 ? Smooth(0.28f, 0.08f, f1) : 0;
        var scratch = Scratches(a, b, pa, 24, width, seed + 3);
        var grime = Smooth(width * 0.3f, 0, MathF.Min(b, width - b));
        c = c * (1 - 0.55f * pit) * (1 - 0.45f * grime) + new Vector3(0.16f) * scratch * (1 - pit);
        return (c, -0.5f * pit + 0.1f * scratch, 0.25f * (1 - rust) * (1 - grime));
    }

    /// <summary>Aged brass: dark patina in the recesses and near its edge, verdigris in spots, pits and bright scuffs.</summary>
    static (Vector3 Albedo, float Relief) Brass(float u, float v, float edge, float a, float b, float pa, float width, int seed)
    {
        var c = Rgb(0.56f, 0.41f, 0.18f) * (0.82f + 0.3f * Fbm(u, v, 5, 3, seed));
        var patina = Smooth(1.6f, 0, edge);
        c = Mix(c, Rgb(0.17f, 0.13f, 0.06f), 0.8f * patina);
        var verdigris = Smooth(0.64f, 0.78f, Fbm(u, v, 4, 4, seed + 1));
        c = Mix(c, Rgb(0.16f, 0.24f, 0.17f), 0.6f * verdigris);
        var (f1, _, id) = Cells(u, v, 40, seed + 2);
        var pit = (id & 3) == 1 ? Smooth(0.3f, 0.08f, f1) : 0;
        var scuff = Scratches(a, b, pa, 16, width, seed + 3, 0.6f);
        c = c * (1 - 0.5f * pit) + Rgb(0.22f, 0.19f, 0.12f) * scuff * (1 - patina);
        return (c, -0.4f * pit + 0.08f * scuff);
    }

    // ---- Panel ------------------------------------------------------------------------------------------------------------

    static float PU(float x) => (x - PM) / (PW - 2 * PM);
    static float PV(float y) => (y - PM) / (PH - 2 * PM);

    /// <summary>The bracket at a corner: in corner coordinates (cx, cy from the nearest corner along each axis), an L of two arms
    /// over the frame with leaf tips and a round boss at the corner. Distance inside (negative outside).</summary>
    static float Bracket(float cx, float cy)
    {
        float Arm(float along, float across)
        {
            var len = 21f; var half = 4.6f;
            var body = MathF.Min(half - MathF.Abs(across - 4.4f), len - along);
            var tip = 3.2f - (new Vector2(along - len, across - 4.4f)).Length();                              // the leaf end
            return MathF.Max(MathF.Min(body, along + 1), tip);
        }
        var boss = 8.5f - new Vector2(cx - 5.5f, cy - 5.5f).Length();
        return MathF.Max(MathF.Max(Arm(cx, cy), Arm(cy, cx)), boss);
    }

    static (float Cx, float Cy, bool In) Corner(float x, float y, float w, float h, float m)
    {
        float cx = x < w / 2 ? x : w - x, cy = y < h / 2 ? y : h - y;
        return (cx, cy, cx < m && cy < m);
    }

    /// <summary>Rivets on the strip every 40 along the panel's edges (never in a corner), and on each bracket.</summary>
    static float PanelRivet(float x, float y)
    {
        var best = 99f;
        var (cx, cy, inCorner) = Corner(x, y, PW, PH, PM);
        if (inCorner)
        {
            foreach (var c in new Vector2[] { new(5.5f, 5.5f), new(19.5f, 4.4f), new(4.4f, 19.5f) }) best = MathF.Min(best, (new Vector2(cx, cy) - c).Length());
            return best;
        }
        if (x >= PM && x <= PW - PM) best = MathF.Min(best, new Vector2(Mod(x - PM - 20, 40) - 20, MathF.Min(y, PH - y) - 4.4f).Length());
        if (y >= PM && y <= PH - PM) best = MathF.Min(best, new Vector2(Mod(y - PM - 20, 40) - 20, MathF.Min(x, PW - x) - 4.4f).Length());
        return best;
    }

    static float PanelRelief(float x, float y)
    {
        var d = RoundRect(x, y, PW, PH, 4);
        var (_, woodRelief) = Wood(x, y, PM, PM, PW - 2 * PM, PH - 2 * PM, 40, 100, WalnutDark, WalnutLight);
        float h;
        if (d < 1.2f) h = -1.5f + 1.5f * Bevel(d, 1.2f);
        else if (d < 7.6f) h = 1.4f + 0.7f * MathF.Sin(MathF.PI * (d - 1.2f) / 6.4f);
        else h = woodRelief + 1.4f * Smooth(8.6f, 7.6f, d);
        var (cx, cy, inCorner) = Corner(x, y, PW, PH, PM);
        if (inCorner) { var b = Bracket(cx, cy); if (b > 0) h = MathF.Max(h, 2.6f + 1.0f * Bevel(b, 1.4f)); }
        h += Dome(PanelRivet(x, y), 2.1f, 1.6f);
        return h;
    }

    static Surface PanelAt(float x, float y)
    {
        var d = RoundRect(x, y, PW, PH, 4);
        if (d <= 0) return Surface.Clear;
        float u = PU(x), v = PV(y);
        var horizontal = MathF.Min(y, PH - y) < MathF.Min(x, PW - x);
        var along = horizontal ? x - PM : y - PM; var pa = horizontal ? PW - 2 * PM : PH - 2 * PM;
        var (cx, cy, inCorner) = Corner(x, y, PW, PH, PM);
        var rivet = PanelRivet(x, y);
        if (inCorner && Bracket(cx, cy) > 0)
        {
            var (brass, _) = Brass(u, v, Bracket(cx, cy), along, MathF.Min(cx, cy), pa, 9, 300);
            return rivet < 2.1f ? new(brass * 1.1f, 1, 0.5f, 30) : new(brass, 1, 0.35f, 26);
        }
        if (d < 1.2f) return new(Rgb(0.05f, 0.03f, 0.018f));
        if (d < 7.6f)
        {
            var (iron, _, spec) = Iron(x, y, u, v, along, d - 1.2f, pa, 6.4f, 200);
            return rivet < 2.1f ? new(Rgb(0.23f, 0.22f, 0.2f), 1, 0.45f, 28) : new(iron, 1, spec, 22);
        }
        var (wood, _) = Wood(x, y, PM, PM, PW - 2 * PM, PH - 2 * PM, 40, 100, WalnutDark, WalnutLight);
        wood *= 0.5f + 0.5f * Smooth(7.6f, 15, d);                                                        // the frame's shadow
        return new(wood, 1, 0.1f, 16);
    }

    // ---- Buttons ----------------------------------------------------------------------------------------------------------

    enum Plaque { Normal, Hover, Pressed, Disabled, Primary }

    static Sheet Button(string name, Plaque look) => new(Id, name, BW, BH, Use.NinePatch, (x, y) => ButtonRelief(x, y, look), (x, y) => ButtonAt(x, y, look), BM, BInset);

    const float RimIn = 1, RimOut = 3.2f, Groove = 4.2f;

    static float ButtonRelief(float x, float y, Plaque look)
    {
        var d = RoundRect(x, y, BW, BH, 4);
        float rimOut = look == Plaque.Primary ? RimOut + 0.8f : RimOut, groove = rimOut + 1;
        if (d < RimIn) return -1 + Bevel(d, RimIn);
        if (d < rimOut) return 0.9f * MathF.Sin(MathF.PI * (d - RimIn) / (rimOut - RimIn));
        var (_, grain) = Wood(x, y, BM, BM, BW - 2 * BM, BH - 2 * BM, 0, 400, WalnutDark, WalnutLight);
        var face = look switch { Plaque.Pressed => -0.9f, Plaque.Disabled => 0.6f, _ => 1.3f } * Bevel(d - groove, 3.5f);
        var h = face + (look is Plaque.Primary ? 0 : 0.6f * grain) - 0.5f * Smooth(groove, d < groove ? rimOut : groove, d);
        if (look == Plaque.Primary) { var (cx, cy, _) = Corner(x, y, BW, BH, BM); h += Dome(new Vector2(cx - 7.5f, cy - 7.5f).Length(), 1.8f, 1.2f); }
        return h;
    }

    static Surface ButtonAt(float x, float y, Plaque look)
    {
        var d = RoundRect(x, y, BW, BH, 4);
        if (d <= 0) return Surface.Clear;
        float u = (x - BM) / (BW - 2 * BM), v = (y - BM) / (BH - 2 * BM);
        float rimOut = look == Plaque.Primary ? RimOut + 0.8f : RimOut, groove = rimOut + 1;
        if (d < RimIn) return new(Rgb(0.05f, 0.03f, 0.018f));
        var horizontal = MathF.Min(y, BH - y) < MathF.Min(x, BW - x);
        if (d < rimOut)
        {
            var (brass, _) = Brass(u, v, MathF.Min(d - RimIn, rimOut - d) * 2, horizontal ? x : y, d, horizontal ? BW - 2 * BM : BH - 2 * BM, 3, 500);
            return look switch
            {
                Plaque.Disabled => new(Mix(brass, new Vector3(Luminance(brass) * 0.6f + 0.12f), 0.85f) * 0.8f, 1, 0.05f, 10),
                Plaque.Hover => new(brass * 1.18f, 1, 0.5f, 30),
                _ => new(brass, 1, 0.4f, 28),
            };
        }
        if (d < groove) return new(Rgb(0.04f, 0.025f, 0.015f));
        if (look == Plaque.Primary)
        {
            var (cx, cy, _) = Corner(x, y, BW, BH, BM);
            if (new Vector2(cx - 7.5f, cy - 7.5f).Length() < 1.8f) return new(Rgb(0.62f, 0.47f, 0.22f), 1, 0.6f, 30);
            // Red lacquer: deep at the edge, a glossy top, a fine crackle.
            var (f1, f2, _) = Cells(u, v, 6, 610);
            var crackle = Smooth(0.06f, 0, f2 - f1);
            var lacquer = Mix(Rgb(0.30f, 0.055f, 0.035f), Rgb(0.50f, 0.10f, 0.06f), 0.55f + 0.25f * Fbm(u, v, 3, 3, 611) - 0.35f * Smooth(groove + 4, groove, d));
            return new(lacquer * (1 - 0.25f * crackle), 1, 0.55f, 40);
        }
        var (light, dark) = look switch
        {
            Plaque.Hover => (Rgb(0.42f, 0.25f, 0.12f), Rgb(0.15f, 0.085f, 0.04f)),
            Plaque.Pressed => (Rgb(0.20f, 0.12f, 0.065f), Rgb(0.06f, 0.035f, 0.02f)),
            Plaque.Disabled => (Rgb(0.30f, 0.27f, 0.23f), Rgb(0.20f, 0.18f, 0.155f)),
            _ => (Rgb(0.34f, 0.21f, 0.11f), Rgb(0.11f, 0.065f, 0.035f)),
        };
        var (wood, _) = Wood(x, y, BM, BM, BW - 2 * BM, BH - 2 * BM, 0, 400, dark, light);
        if (look == Plaque.Pressed) wood *= 0.6f + 0.4f * Smooth(groove, groove + 5, MathF.Min(y, x));          // sunk: the rim shades the top
        return new(wood, 1, look == Plaque.Disabled ? 0 : 0.14f, 18);
    }

    // ---- Input ------------------------------------------------------------------------------------------------------------

    static float InputRelief(float x, float y)
    {
        var d = RoundRect(x, y, IW, IH, 3);
        if (d < 1) return Bevel(d, 1) * 0.5f;
        if (d < 3.5f) return 0.5f - 2.2f * Smooth(1, 3.5f, d);
        return -1.7f;
    }

    static Surface InputAt(float x, float y)
    {
        var d = RoundRect(x, y, IW, IH, 3);
        if (d <= 0) return Surface.Clear;
        float u = (x - IM) / (IW - 2 * IM), v = (y - IM) / (IH - 2 * IM);
        if (d < 1) return new(Rgb(0.20f, 0.125f, 0.07f), 1, 0.1f, 16);
        if (d < 3.5f) return new(Rgb(0.045f, 0.028f, 0.016f));
        var fibre = Fbm(u, v, 4, 4, 700) - 0.5f; var fleck = Grain(u, v, (IW - 2 * IM) * 2, 701) - 0.5f;
        var paper = Rgb(0.89f, 0.83f, 0.68f) * (1 + 0.08f * fibre + 0.03f * fleck);
        var shade = 0.72f + 0.28f * Smooth(3.5f, 7.5f, y) * (0.85f + 0.15f * Smooth(3.5f, 6, x));               // the groove's top wall shades it
        return new(paper * shade);
    }

    // ---- Backdrop ---------------------------------------------------------------------------------------------------------

    static float LeatherRelief(float x, float y)
    {
        float u = x / 256, v = y / 256;
        var (f1, f2, _) = Cells(u, v, 80, 800);
        var crease = Smooth(0.025f, 0, MathF.Abs(Gradient(u, v, 5, 4, 801) + 0.4f * Gradient(u, v, 11, 9, 802)));
        return 0.7f * Smooth(0.7f, 0, f1) + 0.4f * Smooth(0, 0.25f, f2 - f1) - 0.45f * crease;
    }

    static Surface LeatherAt(float x, float y)
    {
        float u = x / 256, v = y / 256;
        var (f1, f2, id) = Cells(u, v, 80, 800);
        var crevice = Smooth(0.2f, 0, f2 - f1);
        var mottle = Fbm(u, v, 4, 4, 803);
        var crease = Smooth(0.025f, 0, MathF.Abs(Gradient(u, v, 5, 4, 801) + 0.4f * Gradient(u, v, 11, 9, 802)));
        var c = Mix(Rgb(0.05f, 0.08f, 0.055f), Rgb(0.095f, 0.135f, 0.09f), 0.35f + 0.5f * mottle + 0.15f * ((id & 0xFF) / 255f - 0.5f));
        c *= (1 - 0.35f * crevice) * (1 - 0.12f * crease);
        return new(c, 1, 0.1f, 10);
    }

    /// <summary>The lamp over the table: a warm pool in the middle, the edges falling to near black, worn patches in the leather.</summary>
    static Surface LampAt(float x, float y)
    {
        float u = x / 384, v = y / 216;
        var r = new Vector2((u - 0.5f) * 1.25f, (v - 0.45f) * 1.0f).Length() * 1.7f;
        var k = Smooth(0.35f, 1.15f, r);
        var worn = Smooth(0.6f, 0.85f, Fbm(u * 1.7f, v, 2, 3, 900)) * (1 - k);
        var colour = Mix(Rgb(0.38f, 0.24f, 0.09f), Rgb(0.012f, 0.008f, 0.004f), k);
        var alpha = 0.07f + 0.86f * k;
        colour = Mix(colour, Rgb(0.30f, 0.30f, 0.22f), worn * 0.6f); alpha += 0.06f * worn;
        return new(colour, alpha);
    }
}
