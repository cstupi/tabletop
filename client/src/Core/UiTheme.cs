namespace Tabletop.Client.Core;
/// <summary>A colour as four 0–1 channels ("#RRGGBB" or "#RRGGBBAA"). Core cannot name Godot's Color; Ui.Of converts.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba Hex(string hex)
    {
        var h = hex.TrimStart('#'); float C(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255f;
        return h.Length switch { 6 => new(C(0), C(2), C(4)), 8 => new(C(0), C(2), C(4), C(6)), _ => throw new FormatException(hex) };
    }
}
public sealed record ThemeFont(string Path, int Size, int Weight = 400);
/// <summary>Clipped cuts the corners (Godot corner detail 1) of the flat boxes (chips, key caps, team columns).</summary>
public sealed record PanelShape(int CornerRadius, int BorderWidth, int Padding, bool Clipped);
/// <summary>A nine-patch image from scripts/ThemeTextures (spec 2026-11-21-theme-materials-design.md): its 1× path (the 2× beside it,
/// <c>name@2x.png</c>), its corner margin in 1× pixels, and its drawn frame's inset, which a content margin adds outside the padding.</summary>
public sealed record ThemeImage(string Path, int Margin, int Inset)
{
    public string Path2x => UiTheme.At2x(Path);
}
/// <summary>A theme's materials: the panel, the button's five faces, the input field, and the full-screen backdrop (a picture stretched
/// over the page and a tile repeated at TileSize 1× pixels, the tile over the picture when TileOver: stars over the nebula, the lamp's
/// vignette under it over the leather otherwise). FieldText and FieldDim are the text and placeholder colours on the input field.</summary>
public sealed record ThemeMaterials(ThemeImage Panel, ThemeImage Button, ThemeImage ButtonHover, ThemeImage ButtonPressed, ThemeImage ButtonDisabled,
    ThemeImage Primary, ThemeImage Input, string Backdrop, string BackdropTile, int TileSize, bool TileOver, Rgba FieldText, Rgba FieldDim)
{
    public IEnumerable<ThemeImage> NinePatches => [Panel, Button, ButtonHover, ButtonPressed, ButtonDisabled, Primary, Input];
    /// <summary>Every file, 1× and 2×.</summary>
    public IEnumerable<string> Paths => NinePatches.SelectMany(m => new[] { m.Path, m.Path2x })
        .Concat(new[] { Backdrop, BackdropTile }.SelectMany(p => new[] { p, UiTheme.At2x(p) }));
}
public enum DieFaceStyle { Pips, Numerals }
public sealed record DiceStyle(DieFaceStyle Style, Rgba Face, Rgba Mark, string? FaceDirectory = null)
{
    /// <summary>Null for numerals, or for a value off the die.</summary>
    public string? FaceTexture(int value) => FaceDirectory is { } d && value is >= 1 and <= 6 ? $"{d}/{value}.svg" : null;
}
/// <summary>A theme as data (spec → Themes). Icons is case-insensitive, keyed by condition name or marker kind. Chrome is the second
/// hue of the frame lines (sci-fi's cool violet beside the cyan accent).</summary>
public sealed record UiTheme(string Id, string Name, Rgba Background, Rgba Panel, Rgba PanelBorder, Rgba Text, Rgba DimText,
    Rgba ButtonText, Rgba Accent, Rgba Success, Rgba Failure, Rgba Warning, IReadOnlyList<Rgba> Teams,
    ThemeFont Display, ThemeFont Body, ThemeFont Numerals, PanelShape Shape, DiceStyle Dice, IReadOnlyDictionary<string, string> Icons,
    ThemeMaterials Materials, Rgba Chrome)
{
    /// <summary>The Text size percent its fonts are at (UiThemes.Adjusted): the theme builder scales its fixed sizes (banner, log) by it.</summary>
    public int TextSize { get; init; } = 100;
    public Rgba Team(int ordinal) => Teams[(Math.Max(1, ordinal) - 1) % Teams.Count];
    public string? Icon(string name) => Icons.TryGetValue(name, out var path) ? path : null;
    public IEnumerable<string> Paths => new[] { Display.Path, Body.Path, Numerals.Path }.Concat(Materials.Paths)
        .Concat(Enumerable.Range(1, 6).Select(Dice.FaceTexture)).Concat(Icons.Values).OfType<string>().Distinct();
    /// <summary>A 1× image's 2× file: <c>panel.png</c> → <c>panel@2x.png</c>.</summary>
    public static string At2x(string path) => path.EndsWith(".png", StringComparison.Ordinal) ? path[..^4] + "@2x.png" : path;
}
