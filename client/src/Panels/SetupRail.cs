using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The tool rail down the left edge of setup (spec 2026-11-21-setup-screen-layout-design.md → Tool rail): an icon button per tool in
/// SetupTools.Order with its name as the tooltip, the open one lit, a badge with its count where it has one. A press raises <see cref="Pressed"/>.</summary>
public partial class SetupRail : PanelContainer
{
    readonly Dictionary<SetupTool, (Button Button, PanelContainer Badge, Label Count)> _buttons = new();
    Session _s = null!; Action? _onBoard;
    public event Action<SetupTool>? Pressed;

    public override void _Ready()
    {
        _s = Main.Instance.Session;
        AddThemeStyleboxOverride("panel", Band());
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", Space.S); AddChild(column);
        foreach (var tool in SetupTools.Order)
        {
            var b = new Button { Icon = Ui.Icon(SetupTools.Icon(tool)), TooltipText = SetupTools.Name(tool), ToggleMode = true, FocusMode = FocusModeEnum.None,
                ExpandIcon = true, CustomMinimumSize = new Vector2(40, 40), Flat = true, ThemeTypeVariation = Variation.FlatButton, IconAlignment = HorizontalAlignment.Center };
            if (b.Icon is null) b.Text = SetupTools.Name(tool)[..1];   // never an empty button
            b.AddThemeConstantOverride("icon_max_width", 24);
            b.AddThemeColorOverride("icon_normal_color", Ui.DimText); b.AddThemeColorOverride("icon_hover_color", Ui.Text);
            b.AddThemeColorOverride("icon_pressed_color", Ui.Accent); b.AddThemeColorOverride("icon_hover_pressed_color", Ui.Accent);
            b.AddThemeStyleboxOverride("pressed", Lit()); b.AddThemeStyleboxOverride("hover_pressed", Lit());
            var t = tool;
            b.Pressed += () => Pressed?.Invoke(t);
            var badge = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            badge.AddThemeStyleboxOverride("panel", BadgeBox());
            var count = new Label { MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            count.AddThemeFontSizeOverride("font_size", 11); count.AddThemeColorOverride("font_color", Ui.Background with { A = 1 });
            badge.AddChild(count); b.AddChild(badge);
            badge.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize);
            column.AddChild(b);
            _buttons[tool] = (b, badge, count);
        }
        _onBoard = ShowBadges; _s.BoardChanged += _onBoard;
        ShowBadges();
    }

    public override void _ExitTree() { if (_s is not null && _onBoard is not null) _s.BoardChanged -= _onBoard; }

    /// <summary>Lights the open tool's button.</summary>
    public void Show(SetupTool? open) { foreach (var (tool, b) in _buttons) b.Button.SetPressedNoSignal(tool == open); }

    void ShowBadges()
    {
        foreach (var (tool, b) in _buttons)
        {
            if (!IsInstanceValid(b.Badge)) continue;
            var n = SetupTools.Badge(tool, _s.Draft);
            b.Badge.Visible = n is not null; b.Count.Text = n?.ToString() ?? "";
            b.Badge.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize);
        }
    }

    /// <summary>The quiet band with even padding a step wide: a 40 px button and its padding fill the rail's 56 px.</summary>
    internal static StyleBox Band()
    {
        var box = (StyleBox)Ui.Theme.GetStylebox("panel", Variation.Quiet).Duplicate();
        box.SetContentMarginAll(Space.S);
        return box;
    }

    /// <summary>The count's pill: accent filled, a half-step either side of the digits.</summary>
    static StyleBoxFlat BadgeBox()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent };
        b.SetCornerRadiusAll(8); b.ContentMarginLeft = b.ContentMarginRight = Space.XS;
        return b;
    }

    /// <summary>The open tool's face: a faint accent fill with an accent edge on the left.</summary>
    static StyleBoxFlat Lit()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.18f }, BorderColor = Ui.Accent };
        b.BorderWidthLeft = 3; b.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 4)); b.SetContentMarginAll(Space.S);
        return b;
    }
}
