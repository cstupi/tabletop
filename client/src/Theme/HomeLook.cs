using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Theming;

/// <summary>Home's cards and rows (UX redesign part 1 → Home), in the current theme's colours: a game's status colour as the band on a
/// card's left edge, the primary button filled in the accent.</summary>
public static partial class HomeLook
{
    /// <summary>Lobby and ended dim, setup the warning colour, playing the accent.</summary>
    public static Color Status(GameStatus status) => status switch
    {
        GameStatus.Setup => Ui.Warning,
        GameStatus.Playing => Ui.Accent,
        _ => Ui.DimText,
    };

    static int Radius => Ui.Current.Shape.CornerRadius;

    /// <summary>A card: the panel with a 6 px band in the status colour on its left edge and a 16 px inset.</summary>
    public static StyleBoxFlat Card(GameStatus status)
    {
        var edge = Status(status);
        var b = new StyleBoxFlat { BgColor = Ui.Of(Ui.Current.Panel) with { A = 1 }, BorderColor = edge with { A = 0.55f }, CornerDetail = Ui.Current.Shape.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(1); b.BorderWidthLeft = 6; b.SetCornerRadiusAll(Radius);
        b.ContentMarginLeft = 22; b.ContentMarginRight = 16; b.ContentMarginTop = b.ContentMarginBottom = 14;
        return b;
    }

    /// <summary>A row of a list: the panel, a band on the left in the status colour, a thin rule.</summary>
    public static StyleBoxFlat Row(GameStatus status)
    {
        var b = new StyleBoxFlat { BgColor = Ui.Of(Ui.Current.Panel) with { A = 1 }, BorderColor = Status(status) with { A = 0.9f } };
        b.BorderWidthLeft = 4; b.SetCornerRadiusAll(Math.Min(Radius, 4));
        b.ContentMarginLeft = 14; b.ContentMarginRight = 10; b.ContentMarginTop = b.ContentMarginBottom = 6;
        return b;
    }

    /// <summary>The primary action: filled in the accent, the background colour as ink.</summary>
    public static void Primary(Button b)
    {
        foreach (var (state, mix) in new[] { ("normal", 0f), ("hover", 0.18f), ("pressed", 0.32f), ("focus", 0f) })
        {
            var s = new StyleBoxFlat { BgColor = Ui.Accent.Lerp(Ui.Text, mix) };
            s.SetCornerRadiusAll(Math.Min(Radius, 6)); s.ContentMarginLeft = s.ContentMarginRight = 14; s.ContentMarginTop = s.ContentMarginBottom = 6;
            if (state == "focus") { s.DrawCenter = false; s.BorderColor = Ui.Text; s.SetBorderWidthAll(1); }
            b.AddThemeStyleboxOverride(state, s);
        }
        var disabled = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.35f } };
        disabled.SetCornerRadiusAll(Math.Min(Radius, 6)); disabled.ContentMarginLeft = disabled.ContentMarginRight = 14; disabled.ContentMarginTop = disabled.ContentMarginBottom = 6;
        b.AddThemeStyleboxOverride("disabled", disabled);
        var ink = Ink;
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            b.AddThemeColorOverride(name, ink);
    }

    /// <summary>The ink on the accent: the background colour on a light accent (Sci-fi), the button text on a dark one (Fantasy).</summary>
    static Color Ink => Ui.Accent.Luminance > 0.5f ? Ui.Background with { A = 1 } : Ui.Of(Ui.Current.ButtonText);

    /// <summary>An icon-only flat button's icon in the text colour, so it reads on every theme's background.</summary>
    public static void IconInk(Button b)
    {
        foreach (var name in new[] { "icon_normal_color", "icon_focus_color", "icon_pressed_color" }) b.AddThemeColorOverride(name, Ui.Text);
        b.AddThemeColorOverride("icon_hover_color", Ui.Accent);
        b.AddThemeColorOverride("icon_disabled_color", Ui.DimText);
    }

    /// <summary>A status chip's mark: ○ lobby, ◐ setup, ● playing, ✓ ended, drawn in the status colour.</summary>
    public sealed partial class StatusMark : Control
    {
        readonly GameStatus _status;
        public StatusMark(GameStatus status, int size = 14)
        {
            _status = status; CustomMinimumSize = new Vector2(size, size); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var c = Status(_status); var r = Size.X / 2 - 1.5f; var o = Size / 2;
            switch (_status)
            {
                case GameStatus.Lobby: DrawArc(o, r, 0, Mathf.Tau, 32, c, 2, true); break;
                case GameStatus.Setup:
                    DrawArc(o, r, 0, Mathf.Tau, 32, c, 2, true);
                    var half = new List<Vector2> { o + new Vector2(0, -r) };
                    for (var i = 0; i <= 16; i++) { var a = -Mathf.Pi / 2 + Mathf.Pi * i / 16; half.Add(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                    DrawColoredPolygon(half.ToArray(), c);
                    break;
                case GameStatus.Playing: DrawCircle(o, r + 1, c, true, -1, true); break;
                default: DrawPolyline([o + new Vector2(-r * 0.8f, 0), o + new Vector2(-r * 0.2f, r * 0.6f), o + new Vector2(r * 0.85f, -r * 0.7f)], c, 2.2f, true); break;
            }
        }
    }
}
