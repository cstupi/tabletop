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

    /// <summary>A card inside Home's framed list: a flat inset box (a material inside the material's frame would double it), its status as a
    /// band on its left edge, M inside (a box of several lines).</summary>
    public static StyleBox Card(GameStatus status) => Inset(status, Space.M, Space.M);

    /// <summary>A row of the framed list: the same inset box, M across and S down.</summary>
    public static StyleBox Row(GameStatus status) => Inset(status, Space.M, Space.S);

    static StyleBoxFlat Inset(GameStatus status, int x, int y)
    {
        var b = new StyleBoxFlat { BgColor = Ui.Background with { A = 0.55f }, BorderColor = Status(status) with { A = 0.7f }, CornerDetail = Ui.Current.Shape.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(1); b.BorderWidthLeft = 4; b.SetCornerRadiusAll(Math.Min(Radius, 4));
        b.ContentMarginLeft = b.ContentMarginRight = x; b.ContentMarginTop = b.ContentMarginBottom = y;
        return b;
    }

    /// <summary>The primary action: the theme's primary face (Variation.Primary), with a focus frame in the text colour.</summary>
    public static void Primary(Button b)
    {
        b.ThemeTypeVariation = Variation.Primary;
        var focus = new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Text }; focus.SetBorderWidthAll(1); focus.SetCornerRadiusAll(Math.Min(Radius, 6));
        b.AddThemeStyleboxOverride("focus", focus);
    }

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
