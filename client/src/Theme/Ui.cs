using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Theming;

/// <summary>Theme type variations; each constant equals its name.</summary>
public static class Variation
{
    public const string Badge = nameof(Badge), Chip = nameof(Chip), DieFace = nameof(DieFace), DieMiss = nameof(DieMiss), FlatButton = nameof(FlatButton);
    public const string Banner = nameof(Banner), BannerDetail = nameof(BannerDetail), Dim = nameof(Dim), Accent = nameof(Accent), Success = nameof(Success),
        Failure = nameof(Failure), Warning = nameof(Warning), Display = nameof(Display), Numerals = nameof(Numerals), Mono = nameof(Mono);
    /// <summary>The UX redesign's shared vocabulary (spec 2026-11-07-ux-redesign-design.md → Shared vocabulary): a key boxed in the numeral font,
    /// an icon-over-word button, a panel with a 16 px inset, a one-line dim band, and the one lit button of a card.</summary>
    public const string KeyCap = nameof(KeyCap), Tile = nameof(Tile), Card = nameof(Card), Quiet = nameof(Quiet), Primary = nameof(Primary);
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
    public static Color Text => Of(Current.Text);
    public static Color DimText => Of(Current.DimText);
    public static Color Background => Of(Current.Background);

    /// <summary>A team column (spec 2026-11-07-lobby-and-setup-redesign-design.md): the panel, a thick left edge and a border in the team's
    /// colour; ordinal 0 (Watching) in the dim text colour. Highlighted: a drop would land here.</summary>
    public static StyleBoxFlat TeamColumn(int ordinal, bool highlighted = false)
    {
        var edge = ordinal > 0 ? Team(ordinal) : DimText;
        var b = new StyleBoxFlat { BgColor = highlighted ? Of(Current.Panel).Lerp(edge, 0.22f) with { A = 1 } : Of(Current.Panel) with { A = 1 },
            BorderColor = edge with { A = ordinal > 0 ? 0.9f : 0.5f }, CornerDetail = Current.Shape.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(2); b.BorderWidthLeft = 7; b.SetCornerRadiusAll(Current.Shape.CornerRadius); b.SetContentMarginAll(0);
        return b;
    }

    /// <summary>The header band across the top of a team column: the team's colour, solid; Watching's a faint band.</summary>
    public static StyleBoxFlat TeamBand(int ordinal, bool compact = false)
    {
        var b = new StyleBoxFlat { BgColor = ordinal > 0 ? Team(ordinal) : DimText with { A = 0.25f } };
        b.ContentMarginLeft = b.ContentMarginRight = compact ? 8 : 14; b.ContentMarginTop = b.ContentMarginBottom = compact ? 4 : 10;
        return b;
    }

    /// <summary>The text colour on a team's band: the background colour, which reads on every team colour.</summary>
    public static Color OnBand(int ordinal) => ordinal > 0 ? Background with { A = 1 } : Text;

    /// <summary>A player row: my own rows lit faintly in the accent colour.</summary>
    public static StyleBoxFlat MemberRow(bool mine)
    {
        var b = new StyleBoxFlat { BgColor = mine ? Accent with { A = 0.10f } : Accent with { A = 0 } };
        b.SetCornerRadiusAll(Math.Min(Current.Shape.CornerRadius, 4)); b.ContentMarginLeft = b.ContentMarginRight = 8; b.ContentMarginTop = b.ContentMarginBottom = 6;
        return b;
    }

    /// <summary>A terrain effect's chip (TerrainEffects): its colour faintly behind, a border in it.</summary>
    public static StyleBoxFlat EffectChip(Rgba colour)
    {
        var c = Of(colour);
        var b = new StyleBoxFlat { BgColor = c with { A = 0.2f }, BorderColor = c, CornerDetail = 6 };
        b.SetBorderWidthAll(1); b.SetCornerRadiusAll(Math.Max(3, Math.Min(Current.Shape.CornerRadius, 8)));
        b.ContentMarginLeft = 6; b.ContentMarginRight = 10; b.ContentMarginTop = b.ContentMarginBottom = 3;
        return b;
    }

    /// <summary>A card's title band: a thick left edge in the colour (a team's, or a terrain piece's strongest effect).</summary>
    public static StyleBoxFlat EdgeBand(Color edge)
    {
        var b = new StyleBoxFlat { BgColor = edge with { A = 0.12f }, BorderColor = edge };
        b.BorderWidthLeft = 6; b.ContentMarginLeft = 12; b.ContentMarginRight = 8; b.ContentMarginTop = b.ContentMarginBottom = 4;
        return b;
    }

    /// <summary>A floating panel over the table (the terrain hover): the theme's panel, opaque, with an accent border.</summary>
    public static StyleBoxFlat FloatingPanel()
    {
        var b = new StyleBoxFlat { BgColor = Of(Current.Panel) with { A = 0.97f }, BorderColor = Of(Current.PanelBorder), CornerDetail = 6 };
        b.SetBorderWidthAll(Math.Max(1, Current.Shape.BorderWidth)); b.SetCornerRadiusAll(Current.Shape.CornerRadius); b.SetContentMarginAll(12);
        b.ShadowColor = Background with { A = 0.5f }; b.ShadowSize = 6;
        return b;
    }

    /// <summary>Null when the theme has no icon for the name.</summary>
    public static Texture2D? Icon(string name)
    {
        if (Icons.TryGetValue(name, out var cached)) return cached;
        var path = Current.Icon(name);
        return Icons[name] = path is null ? null : GD.Load<Texture2D>(path);
    }
}
