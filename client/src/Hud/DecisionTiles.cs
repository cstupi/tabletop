using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>The decision card's parts (UX redesign part 2 → Decision box): buttons with their key cap inside, option tiles, a unit's group,
/// the allowance bar, a team's band and chip. Each draws a Core value with the shared Variations; nothing here decides.</summary>
public static partial class DecisionTiles
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

    /// <summary>An option tile: its icon over its word, its key cap in the top corner. Made once and set by Fill, so the decision box keeps its
    /// tiles from one Ask to the next.</summary>
    public sealed partial class ChoiceTile : Button
    {
        public readonly TextureRect Picture = DecisionTiles.Icon(null, 26);
        public readonly Label Word = new() { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        public readonly Label Key = DecisionTiles.Cap("");
        /// <summary>What the tile stands for now, read by the handlers the decision box wires once.</summary>
        public OptionTile? Option;

        public ChoiceTile() : this(96) { }

        public ChoiceTile(int minWidth)
        {
            FocusMode = FocusModeEnum.None; ThemeTypeVariation = Variation.Tile; SizeFlagsHorizontal = SizeFlags.ExpandFill; CustomMinimumSize = new Vector2(minWidth, 0);
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            col.AddThemeConstantOverride("separation", 4);
            Picture.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; col.AddChild(Picture);
            Word.CustomMinimumSize = new Vector2(minWidth - 24, 0); col.AddChild(Word);
            DecisionTiles.Holding(this, col, 10, 10);
            Key.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            Key.GrowHorizontal = GrowDirection.Begin;
            Key.Position = new Vector2(-4, 4);
            AddChild(Key);
            Resized += () => Key.Position = new Vector2(Size.X - Key.Size.X - 5, 5);
        }
    }

    /// <summary>Sets a tile to an option: its icon, word, key cap and tooltip.</summary>
    public static void Fill(ChoiceTile b, OptionTile tile)
    {
        b.Option = tile;
        Look.Tip(b, tile.Label);
        var picture = tile.Icon is null ? null : Ui.Icon(tile.Icon);
        if (!ReferenceEquals(b.Picture.Texture, picture)) b.Picture.Texture = picture;
        b.Picture.Visible = picture is not null;
        if (b.Picture.SelfModulate != Ui.Text) b.Picture.SelfModulate = Ui.Text;
        Look.Text(b.Word, tile.Word);
        var key = DecisionCard.KeyCap(tile.Key) ?? "";
        Look.Text(b.Key, key, Variation.KeyCap); b.Key.Visible = key.Length > 0;
        if (b.Disabled) b.Disabled = false;
    }

    /// <summary>Tiles in rows of up to four.</summary>
    public static GridContainer Grid(int count)
    {
        var g = new GridContainer { Columns = Math.Clamp(count, 1, 4), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        g.AddThemeConstantOverride("h_separation", 8); g.AddThemeConstantOverride("v_separation", 8);
        return g;
    }

    /// <summary>A unit's group: its team's left edge, its name and pips, its tiles, and the note once under them; a run of options naming no
    /// one unit is its tiles alone. Made once and set by Fill.</summary>
    public sealed partial class OptionPanel : PanelContainer
    {
        public readonly HBoxContainer Head = new();
        public readonly Label Unit = new() { ThemeTypeVariation = Variation.Display, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis },
            Pips = new() { VerticalAlignment = VerticalAlignment.Center },
            Note = new() { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        public readonly GridContainer Tiles = Grid(1);
        public readonly MarginContainer Inset = new();
        public readonly Kept Kept;
        public int? Ordinal;

        public OptionPanel()
        {
            Kept = new(Tiles);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) Inset.AddThemeConstantOverride($"margin_{side}", 10);
            Inset.AddThemeConstantOverride("margin_left", 14);
            AddChild(Inset);
            var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); Inset.AddChild(col);
            Head.AddThemeConstantOverride("separation", 10); col.AddChild(Head);
            Head.AddChild(Unit); Head.AddChild(Pips);
            col.AddChild(Tiles); col.AddChild(Note);
        }
    }

    static readonly StyleBoxEmpty Bare = new();

    /// <summary>Sets a group: framed in its team's column with its name, pips and note when it names a unit or carries a note, else bare tiles.
    /// Returns the tiles' kept children for the caller to fill.</summary>
    public static Kept Fill(OptionPanel panel, OptionGroup group)
    {
        var framed = group.Unit is not null || group.Note is not null;
        int? ordinal = framed ? group.TeamOrdinal : null;
        if (panel.Ordinal != ordinal || !panel.HasThemeStyleboxOverride("panel"))
        {
            panel.Ordinal = ordinal;
            panel.AddThemeStyleboxOverride("panel", ordinal is { } o ? Ui.TeamColumn(o) : Bare);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) panel.Inset.AddThemeConstantOverride($"margin_{side}", framed ? side == "left" ? 14 : 10 : 0);
        }
        panel.Head.Visible = group.Unit is not null;
        if (group.Unit is { } unit) Look.Text(panel.Unit, unit, Variation.Display);
        panel.Pips.Visible = group.Pips is not null;
        if (group.Pips is { } pips) { Look.Text(panel.Pips, pips); Look.Ink(panel.Pips, group.TeamOrdinal > 0 ? Ui.Team(group.TeamOrdinal) : Ui.Text); }
        panel.Note.Visible = group.Note is not null;
        if (group.Note is { } note) Look.Text(panel.Note, note, Variation.Warning);
        var columns = Math.Clamp(group.Tiles.Count, 1, 4);
        if (panel.Tiles.Columns != columns) panel.Tiles.Columns = columns;
        return panel.Kept.Begin();
    }

    /// <summary>Forgets the cached frame, so the next Fill sets it in the current theme.</summary>
    public static void Restyle(OptionPanel panel) => panel.Ordinal = -1;

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
