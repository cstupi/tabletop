using Godot;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>The play Hud's shared pieces (UX redesign → Shared vocabulary): a tinted theme icon, a team band bar, a key cap, and a button
/// that draws its icon, its word and its key cap.</summary>
public static class HudBits
{
    /// <summary>The theme's icon tinted; an empty square of the size when the theme has none, so rows keep their columns.</summary>
    public static TextureRect Icon(string name, Color tint, int size, string tooltip = "") => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tooltip,
        MouseFilter = tooltip.Length > 0 ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
    };

    /// <summary>No tint: for the theme's coloured icons (activated, objective, the conditions), drawn as they are.</summary>
    public static Color Plain => Ui.Faded with { A = 1 };

    /// <summary>The team band: a bar in the team's colour down the left edge of a card or row (6 px); ordinal 0 draws nothing but the gap.</summary>
    public static ColorRect Band(int ordinal, int width = 6) => new()
    {
        Color = ordinal > 0 ? Ui.Team(ordinal) : Ui.DimText with { A = 0 }, CustomMinimumSize = new Vector2(width, 0), MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.Fill,
    };

    /// <summary>A key cap: the key boxed in the chip's frame, in dim text.</summary>
    public static PanelContainer KeyCap(string key)
    {
        var cap = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var box = Ui.Theme.GetStylebox("panel", Variation.Chip).Duplicate() as StyleBoxFlat;
        if (box is not null) { box.BorderColor = Ui.DimText; box.ContentMarginTop = box.ContentMarginBottom = 0; box.ContentMarginLeft = box.ContentMarginRight = 5; cap.AddThemeStyleboxOverride("panel", box); }
        cap.AddChild(new Label { Text = key, ThemeTypeVariation = Variation.Dim, MouseFilter = Control.MouseFilterEnum.Ignore });
        return cap;
    }

    /// <summary>A button drawing the icon, the word and the key cap at its right (UX redesign → Key cap). The row lays itself out inside
    /// the button and sets its minimum size; nothing in it takes the mouse.</summary>
    public static Button CapButton(string icon, string word, string key, string tooltip = "")
    {
        var b = new Button { TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None };
        Fill(b, icon, word, key);
        return b;
    }

    /// <summary>Redraws a CapButton's row: a new word or key in place.</summary>
    public static void Fill(Button b, string icon, string word, string key)
    {
        foreach (var c in b.GetChildren()) { b.RemoveChild(c); c.QueueFree(); }
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var edge in new[] { "margin_left", "margin_right" }) pad.AddThemeConstantOverride(edge, 10);
        foreach (var edge in new[] { "margin_top", "margin_bottom" }) pad.AddThemeConstantOverride(edge, 6);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 8); pad.AddChild(row);
        row.AddChild(Icon(icon, Ui.Text, 18));
        row.AddChild(new Label { Text = word, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
        row.AddChild(KeyCap(key));
        b.AddChild(pad);
        pad.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        pad.MinimumSizeChanged += () => b.CustomMinimumSize = pad.GetCombinedMinimumSize();
        b.CustomMinimumSize = pad.GetCombinedMinimumSize();
    }

    /// <summary>A flat button of the theme's icon alone in the text colour (the accent on hover), its words in the tooltip.</summary>
    public static Button IconButton(string icon, string tooltip, int size = 16)
    {
        var b = new Button { Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, Icon = Ui.Icon(icon),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        if (b.Icon is null) b.Text = tooltip;   // never a zero-width button
        b.AddThemeConstantOverride("icon_max_width", size);
        b.AddThemeColorOverride("icon_normal_color", Ui.Text); b.AddThemeColorOverride("icon_hover_color", Ui.Accent);
        b.AddThemeColorOverride("icon_pressed_color", Ui.Accent); b.AddThemeColorOverride("icon_focus_color", Ui.Text);
        return b;
    }

    /// <summary>Shows the controls only while the pointer is over the row (always on a touch screen); they keep their room either way.</summary>
    public static void OnHover(Control row, params Control[] shown)
    {
        if (DisplayServer.IsTouchscreenAvailable()) return;
        void Set(bool on) { foreach (var c in shown) if (GodotObject.IsInstanceValid(c)) c.Modulate = Plain with { A = on ? 1 : 0 }; }
        Set(false);
        row.MouseEntered += () => Set(true);
        row.MouseExited += () => { if (!row.GetGlobalRect().HasPoint(row.GetGlobalMousePosition())) Set(false); };
        foreach (var c in shown) c.MouseEntered += () => Set(true);
    }

    public static void Clear(Node box, params Node[] keep) { foreach (var c in box.GetChildren()) if (!keep.Contains(c)) { box.RemoveChild(c); c.QueueFree(); } }
}
