namespace Tabletop.Client.Core;

public static class UiThemes
{
    public const string DefaultId = "scifi";
    const string F = "res://content/themes/fantasy", S = "res://content/themes/scifi";
    static Rgba H(string hex) => Rgba.Hex(hex);
    static Dictionary<string, string> IconsIn(string dir) => new[] { "activated", "objective", "shaken", "fatigued", "settings", "team", "player", "army", "watch", "warning", "ready", "editing", "charge", "shoot", "activation", "hold", "advance", "rush", "melee", "cast", "wound", "key", "terrain", "table", "zone", "layout", "remove", "dice", "add", "blocks-sight", "impassable", "dangerous", "cover", "difficult", "open-ground", "round", "turn", "wait", "quality", "defense", "log", "undo", "copy", "home", "skip-back", "step-back", "step", "skip", "play", "pause", "camera", "measure", "unstable", "steep", "see-in-out", "lock", "unlock", "replay", "menu", "search", "sign-out", "refresh", "display", "sound", "accessibility", "game-menu" }.ToDictionary(n => n, n => $"{dir}/icons/{n}.svg", StringComparer.OrdinalIgnoreCase);
    static ThemeImage M(string dir, string name, (int Margin, int Inset) m) => new($"{dir}/{name}.png", m.Margin, m.Inset);
    /// <summary>The images scripts/ThemeTextures draws; the margins and insets are its sheets' (ThemeTextureTests checks them).</summary>
    static ThemeMaterials Materials(string d, (int, int) panel, (int, int) button, (int, int) input, int tile, bool tileOver, bool stretch, Rgba fieldText, Rgba fieldDim) =>
        new(M(d, "panel", panel), M(d, "button", button), M(d, "button-hover", button), M(d, "button-pressed", button), M(d, "button-disabled", button),
            M(d, "button-primary", button), M(d, "input", input), $"{d}/backdrop.png", $"{d}/backdrop-tile.png", tile, tileOver, stretch, fieldText, fieldDim);
    /// <summary>A war table: dark walnut panels framed in iron with brass brackets, brass-rimmed wooden plaques (the primary red lacquer),
    /// parchment fields, a green leather table top under a lamp.</summary>
    public static UiTheme Fantasy { get; } = new("fantasy", "Fantasy",
        H("#170F09"), H("#21150C"), H("#9C7A3C"), H("#EADCB8"), H("#B3A07E"), H("#F3EAD3"),
        H("#C9A04E"), H("#79A84F"), H("#D2513E"), H("#E08A3A"),
        [H("#D2513E"), H("#5C8FD8"), H("#CF72A0"), H("#D9A441"), H("#A27AD6"), H("#45B0A6")],
        new($"{F}/fonts/Cinzel-Variable.ttf", 20, 700), new($"{F}/fonts/EBGaramond-Variable.ttf", 18, 500), new($"{F}/fonts/Cinzel-Variable.ttf", 18, 700),
        new(4, 2, 12, false),
        new(DieFaceStyle.Pips, H("#E8DCC0"), H("#2B2118"), $"{F}/dice"), IconsIn(F),
        Materials(F, (24, 8), (12, 4), (8, 4), 256, false, false, H("#2B2118"), H("#6E5E48")), H("#9C7A3C"));
    /// <summary>A starship console: gunmetal hull plates with a cyan light strip, backlit keys, scanline screens, deep space; violet chrome
    /// beside the cyan accent, amber warnings.</summary>
    public static UiTheme SciFi { get; } = new("scifi", "Sci-fi",
        H("#070B10"), H("#0F1720F2"), H("#4F5BA8"), H("#DCEBF2"), H("#8A9CA6"), H("#DCEBF2"),
        H("#3FD0E0"), H("#4BE08A"), H("#FF4D5E"), H("#FFB020"),
        [H("#FF5A4E"), H("#3F8CFF"), H("#FF6FB5"), H("#FFC63F"), H("#B07CFF"), H("#3FD0E0")],
        new($"{S}/fonts/Orbitron-Variable.ttf", 18, 600), new($"{S}/fonts/Rajdhani-SemiBold.ttf", 20), new($"{S}/fonts/JetBrainsMono-Variable.ttf", 16, 500),
        new(8, 1, 10, true),
        new(DieFaceStyle.Numerals, H("#101A24"), H("#3FD0E0")), IconsIn(S),
        Materials(S, (16, 6), (10, 4), (8, 4), 512, true, true, H("#DCEBF2"), H("#8A9CA6")), H("#8C9CFF"));
    public static IReadOnlyList<UiTheme> All { get; } = [Fantasy, SciFi];
    /// <summary>The detailed log's monospace face, the same in every theme: its columns line up.</summary>
    public static ThemeFont Mono { get; } = new($"{S}/fonts/JetBrainsMono-Variable.ttf", 14, 400);
    /// <summary>Team colours for colour-blind players (Options → Accessibility): each pair stays apart (CIE76 ΔE ≥ 15) under
    /// deuteranopia, protanopia and tritanopia, each reads at 3:1 on both themes' panels, none near the success green.</summary>
    public static IReadOnlyList<Rgba> ColourBlindTeams { get; } = [H("#C7490A"), H("#506EE6"), H("#D62BA9"), H("#7A6A3D"), H("#7632FC"), H("#B85C74")];

    /// <summary>A size scaled by the Text size percent.</summary>
    public static int Scaled(int size, int percent) => (int)Math.Round(size * percent / 100.0);

    /// <summary>The theme with its fonts at the Text size (percent) and, when asked, the colour-blind team colours; the theme itself when neither changes it.</summary>
    public static UiTheme Adjusted(UiTheme t, int textSize, bool colourBlind) => textSize == 100 && !colourBlind ? t : t with
    {
        Display = t.Display with { Size = Scaled(t.Display.Size, textSize) }, Body = t.Body with { Size = Scaled(t.Body.Size, textSize) },
        Numerals = t.Numerals with { Size = Scaled(t.Numerals.Size, textSize) }, Teams = colourBlind ? ColourBlindTeams : t.Teams, TextSize = textSize,
    };

    public static UiTheme Resolve(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? SciFi;
}
