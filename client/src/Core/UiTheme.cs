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
/// <summary>Clipped cuts the corners (Godot corner detail 1); a nine-patch replaces the flat box.</summary>
public sealed record PanelShape(int CornerRadius, int BorderWidth, int Padding, bool Clipped, string? PanelNinePatch = null,
    string? ButtonNinePatch = null, int PanelMargin = 12, int ButtonMargin = 10);
public enum DieFaceStyle { Pips, Numerals }
public sealed record DiceStyle(DieFaceStyle Style, Rgba Face, Rgba Mark, string? FaceDirectory = null)
{
    /// <summary>Null for numerals, or for a value off the die.</summary>
    public string? FaceTexture(int value) => FaceDirectory is { } d && value is >= 1 and <= 6 ? $"{d}/{value}.svg" : null;
}
/// <summary>A theme as data (spec → Themes). Icons is case-insensitive, keyed by condition name or marker kind.</summary>
public sealed record UiTheme(string Id, string Name, Rgba Background, Rgba Panel, Rgba PanelBorder, Rgba Text, Rgba DimText,
    Rgba ButtonText, Rgba Accent, Rgba Success, Rgba Failure, Rgba Warning, IReadOnlyList<Rgba> Teams,
    ThemeFont Display, ThemeFont Body, ThemeFont Numerals, PanelShape Shape, DiceStyle Dice, IReadOnlyDictionary<string, string> Icons)
{
    /// <summary>The Text size percent its fonts are at (UiThemes.Adjusted): the theme builder scales its fixed sizes (banner, log) by it.</summary>
    public int TextSize { get; init; } = 100;
    public Rgba Team(int ordinal) => Teams[(Math.Max(1, ordinal) - 1) % Teams.Count];
    public string? Icon(string name) => Icons.TryGetValue(name, out var path) ? path : null;
    public IEnumerable<string> Paths => new[] { Display.Path, Body.Path, Numerals.Path, Shape.PanelNinePatch, Shape.ButtonNinePatch }
        .Concat(Enumerable.Range(1, 6).Select(Dice.FaceTexture)).Concat(Icons.Values).OfType<string>().Distinct();
}
