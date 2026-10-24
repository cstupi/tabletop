using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Theming;

/// <summary>Theme type variations; each constant equals its name.</summary>
public static class Variation
{
    public const string Badge = nameof(Badge), Chip = nameof(Chip), DieFace = nameof(DieFace), DieMiss = nameof(DieMiss), FlatButton = nameof(FlatButton);
    public const string Banner = nameof(Banner), BannerDetail = nameof(BannerDetail), Dim = nameof(Dim), Accent = nameof(Accent), Success = nameof(Success),
        Failure = nameof(Failure), Warning = nameof(Warning), Display = nameof(Display), Numerals = nameof(Numerals);
}

/// <summary>The current UiTheme, its built Godot theme, and its colours and icons as Godot types.</summary>
public static class Ui
{
    static readonly Dictionary<string, Texture2D?> Icons = new(StringComparer.OrdinalIgnoreCase);
    public static UiTheme Current { get; private set; } = UiThemes.SciFi;
    static Godot.Theme? _theme;
    public static Godot.Theme Theme => _theme ??= ThemeBuilder.Build(Current);
    public static event Action? Changed;

    public static void Apply(UiTheme t)
    {
        Current = t; _theme = ThemeBuilder.Build(t); Icons.Clear();
        Changed?.Invoke();
    }

    public static Color Of(Rgba c) => new(c.R, c.G, c.B, c.A);
    public static Color Team(int ordinal) => Of(Current.Team(ordinal));
    public static Color Accent => Of(Current.Accent);
    public static Color Success => Of(Current.Success);
    public static Color Failure => Of(Current.Failure);
    public static Color Warning => Of(Current.Warning);
    public static readonly Color Faded = new(1, 1, 1, 0.45f);

    /// <summary>Null when the theme has no icon for the name.</summary>
    public static Texture2D? Icon(string name)
    {
        if (Icons.TryGetValue(name, out var cached)) return cached;
        var path = Current.Icon(name);
        return Icons[name] = path is null ? null : GD.Load<Texture2D>(path);
    }
}
