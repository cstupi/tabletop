namespace Tabletop.Client.Core;

public static class UiThemes
{
    public const string DefaultId = "scifi";
    const string F = "res://content/themes/fantasy", S = "res://content/themes/scifi";
    static Rgba H(string hex) => Rgba.Hex(hex);
    static Dictionary<string, string> IconsIn(string dir) => new[] { "activated", "objective", "shaken", "fatigued", "settings", "charge", "shoot" }.ToDictionary(n => n, n => $"{dir}/icons/{n}.svg", StringComparer.OrdinalIgnoreCase);
    public static UiTheme Fantasy { get; } = new("fantasy", "Fantasy",
        H("#C9B68E"), H("#E9DBB8"), H("#4A2F1B"), H("#2B2118"), H("#6E5E48"), H("#F3EAD3"),
        H("#8C6A2F"), H("#3F6B2A"), H("#8E2A1E"), H("#A3561A"),
        [H("#8E2A1E"), H("#2C4F86"), H("#3F6B2A"), H("#8C6A2F"), H("#5B3A78"), H("#2A6F6B")],
        new($"{F}/fonts/Cinzel-Variable.ttf", 20, 700), new($"{F}/fonts/EBGaramond-Variable.ttf", 18, 500), new($"{F}/fonts/Cinzel-Variable.ttf", 18, 700),
        new(4, 3, 12, false, $"{F}/panel.svg", $"{F}/button.svg"),
        new(DieFaceStyle.Pips, H("#E8DCC0"), H("#2B2118"), $"{F}/dice"), IconsIn(F));
    public static UiTheme SciFi { get; } = new("scifi", "Sci-fi",
        H("#0A0F14"), H("#0D1620D9"), H("#3FD0E0"), H("#DCEBF2"), H("#7A8C96"), H("#DCEBF2"),
        H("#3FD0E0"), H("#4BE08A"), H("#FF4D5E"), H("#FFB020"),
        [H("#FF5A4E"), H("#3F8CFF"), H("#4BE08A"), H("#FFC63F"), H("#B07CFF"), H("#3FD0E0")],
        new($"{S}/fonts/Orbitron-Variable.ttf", 18, 600), new($"{S}/fonts/Rajdhani-SemiBold.ttf", 20), new($"{S}/fonts/JetBrainsMono-Variable.ttf", 16, 500),
        new(8, 1, 10, true),
        new(DieFaceStyle.Numerals, H("#101A24"), H("#3FD0E0")), IconsIn(S));
    public static IReadOnlyList<UiTheme> All { get; } = [Fantasy, SciFi];
    /// <summary>The detailed log's monospace face, the same in every theme: its columns line up.</summary>
    public static ThemeFont Mono { get; } = new($"{S}/fonts/JetBrainsMono-Variable.ttf", 14, 400);
    public static UiTheme Resolve(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? SciFi;
}
