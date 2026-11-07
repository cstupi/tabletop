using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>The decision card's parts (UX redesign part 2 → Decision box): buttons with their key cap inside, option tiles, a unit's group,
/// the allowance bar, a team's band and chip. Each draws a Core value with the shared Variations; nothing here decides.</summary>
public static class DecisionTiles
{
    /// <summary>A key cap: the key boxed in the numeral font. Empty for no key.</summary>
    public static Label Cap(string key) => new() { Text = key, ThemeTypeVariation = Variation.KeyCap, VerticalAlignment = VerticalAlignment.Center, Visible = key != "",
        MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };

    public static TextureRect Icon(string? name, int size = 20, Color? tint = null)
    {
        var r = new TextureRect { Texture = name is null ? null : Ui.Icon(name), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        r.Visible = r.Texture is not null;
        r.SelfModulate = tint ?? Ui.Text;                                       // the white icon in the theme's text colour unless tinted
        return r;
    }

    /// <summary>A button drawing its own content: the content fills it, ignores the mouse, and sets its minimum size.</summary>
    public static Button Holding(Button b, Control content, int padX = 0, int padY = 0)
    {
        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right" }) inset.AddThemeConstantOverride($"margin_{side}", padX);
        foreach (var side in new[] { "top", "bottom" }) inset.AddThemeConstantOverride($"margin_{side}", padY);
        inset.AddChild(content);
        inset.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        b.AddChild(inset);
        void Fit() { if (GodotObject.IsInstanceValid(b) && GodotObject.IsInstanceValid(inset)) b.CustomMinimumSize = new Vector2(b.CustomMinimumSize.X, inset.GetCombinedMinimumSize().Y); }
        inset.MinimumSizeChanged += Fit;
        b.Ready += Fit;
        return b;
    }

    /// <summary>A card's action: its word and its key cap, the primary one lit, the rest flat.</summary>
    public static Button Action(string text, string key, Action pressed, bool primary = false, string? icon = null)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = primary ? Variation.Primary : Variation.FlatButton, TooltipText = "" };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        if (icon is not null) row.AddChild(Icon(icon, 18, primary ? Ui.Background with { A = 1 } : null));
        var word = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        if (primary) word.AddThemeColorOverride("font_color", Ui.Background with { A = 1 });
        row.AddChild(word);
        if (key != "") { var cap = Cap(key); if (primary) cap.AddThemeColorOverride("font_color", Ui.Background with { A = 1 }); row.AddChild(cap); }
        b.Pressed += pressed;
        b.CustomMinimumSize = new Vector2(0, 34);
        Holding(b, row, primary ? 14 : 6, 4);
        b.Ready += () => b.CustomMinimumSize = new Vector2(row.GetCombinedMinimumSize().X + (primary ? 28 : 12), Math.Max(34, b.CustomMinimumSize.Y));
        return b;
    }

    /// <summary>An option tile: its icon over its word, its key cap in the top corner.</summary>
    public static Button Tile(OptionTile tile, Action pressed, int minWidth = 96)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = Variation.Tile, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(minWidth, 0), TooltipText = tile.Label };
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 4);
        if (tile.Icon is not null) { var i = Icon(tile.Icon, 26); i.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; col.AddChild(i); }
        col.AddChild(new Label { Text = tile.Word, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(minWidth - 24, 0) });
        Holding(b, col, 10, 10);
        if (DecisionCard.KeyCap(tile.Key) is { Length: > 0 } key)
        {
            var cap = Cap(key);
            cap.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
            cap.GrowHorizontal = Control.GrowDirection.Begin;
            cap.Position = new Vector2(-4, 4);
            b.AddChild(cap);
            b.Resized += () => { if (GodotObject.IsInstanceValid(cap)) cap.Position = new Vector2(b.Size.X - cap.Size.X - 5, 5); };
        }
        b.Pressed += pressed;
        return b;
    }

    /// <summary>Tiles in rows of up to four.</summary>
    public static GridContainer Grid(int count)
    {
        var g = new GridContainer { Columns = Math.Clamp(count, 1, 4), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        g.AddThemeConstantOverride("h_separation", 8); g.AddThemeConstantOverride("v_separation", 8);
        return g;
    }

    /// <summary>A unit's group: its team's left edge, its name and pips, its tiles, and the note once under them.</summary>
    public static PanelContainer Group(OptionGroup group, Control tiles)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.TeamColumn(group.TeamOrdinal));
        var inset = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) inset.AddThemeConstantOverride($"margin_{side}", 10);
        inset.AddThemeConstantOverride("margin_left", 14);
        panel.AddChild(inset);
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); inset.AddChild(col);
        if (group.Unit is { } unit)
        {
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); col.AddChild(head);
            head.AddChild(new Label { Text = unit, ThemeTypeVariation = Variation.Display, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            if (group.Pips is { } pips) head.AddChild(Pips(pips, group.TeamOrdinal));
        }
        col.AddChild(tiles);
        if (group.Note is { } note) col.AddChild(new Label { Text = note, ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        return panel;
    }

    public static Label Pips(string pips, int ordinal)
    {
        var l = new Label { Text = pips, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeColorOverride("font_color", ordinal > 0 ? Ui.Team(ordinal) : Ui.Text);
        return l;
    }

    /// <summary>The allowance bar: the track filled to the farthest model, a mark at a minimum, the inches beside it; over the allowance in failure colour.</summary>
    public static HBoxContainer Bar(out Action<AllowanceBar> set)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        var bar = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 1, Step = 0, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var mark = new ColorRect { Color = Ui.Text, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddChild(mark);
        var text = new Label { VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(bar); row.AddChild(text);
        StyleBoxFlat? over = null;
        set = b =>
        {
            if (!GodotObject.IsInstanceValid(bar)) return;
            bar.Value = b.Fraction; text.Text = b.Text;
            if (b.Over) { over ??= (StyleBoxFlat)((StyleBoxFlat)bar.GetThemeStylebox("fill")).Duplicate(); over.BgColor = Ui.Failure; bar.AddThemeStyleboxOverride("fill", over); }
            else bar.RemoveThemeStyleboxOverride("fill");
            text.RemoveThemeColorOverride("font_color"); if (b.Over) text.AddThemeColorOverride("font_color", Ui.Failure);
            mark.Visible = b.MinimumFraction is not null;
            if (b.MinimumFraction is { } f) { mark.AnchorLeft = mark.AnchorRight = (float)f; mark.AnchorTop = 0; mark.AnchorBottom = 1; mark.OffsetLeft = -1; mark.OffsetRight = 1; mark.OffsetTop = -3; mark.OffsetBottom = 3; }
        };
        return row;
    }

    /// <summary>A team's band and its words: a bar in the team's colour, the team icon, the name in it, then the rest dim.</summary>
    public static PanelContainer Band(int ordinal, string team, string? rest, string icon = "team")
    {
        var panel = new PanelContainer { ThemeTypeVariation = Variation.Quiet, MouseFilter = Control.MouseFilterEnum.Ignore };
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); panel.AddChild(row);
        var colour = ordinal > 0 ? Ui.Team(ordinal) : Ui.DimText;
        row.AddChild(new ColorRect { Color = colour, CustomMinimumSize = new Vector2(6, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(Icon(icon, 18, colour));
        var name = new Label { Text = team.ToUpperInvariant(), ThemeTypeVariation = Variation.Display, VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeColorOverride("font_color", colour); row.AddChild(name);
        if (rest is not null) row.AddChild(new Label { Text = rest, ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        return panel;
    }
}
