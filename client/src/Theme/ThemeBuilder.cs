using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Theming;

/// <summary>Builds the Godot theme for a UiTheme: panels, buttons, text colours, fonts and the Variation types.</summary>
public static class ThemeBuilder
{
    public static Godot.Theme Build(UiTheme u)
    {
        var t = new Godot.Theme { DefaultFont = FontOf(u.Body), DefaultFontSize = u.Body.Size };
        var s = u.Shape; Color C(Rgba c) => Ui.Of(c);
        var m = u.Materials;
        // Panels and buttons are the theme's materials (spec 2026-11-21-theme-materials-design.md): nine-patches whose drawn frame (the
        // material's inset) sits outside the padding. A text button pads M across and S down, so every text button is as tall.
        StyleBox panel = Nine(m.Panel, Space.S, Space.S);
        foreach (var type in new[] { "PanelContainer", "Panel", "TabContainer" }) t.SetStylebox("panel", type, panel);
        // Every dialog (AcceptDialog, ConfirmationDialog): even L padding, L between the content and the button row (buttons_separation),
        // each button at least DialogButtonWidth wide; DialogLayout right-aligns the row and spaces its buttons L apart, Cancel left of the
        // primary (project.godot gui/common/swap_cancel_ok = Cancel First on every platform).
        StyleBox dialogPanel = Nine(m.Panel, Space.L, Space.L);
        t.SetStylebox("panel", "AcceptDialog", dialogPanel); Variant(t, Variation.DialogCard, "PanelContainer", "panel", dialogPanel);
        t.SetConstant("buttons_separation", "AcceptDialog", Space.L); t.SetConstant("buttons_min_width", "AcceptDialog", DialogButtonWidth);
        // A popup floats over other controls: its panel is opaque, or the text under it reads through a translucent panel colour.
        StyleBox popup = Nine(m.Panel, Space.S, Space.S);
        foreach (var type in new[] { "TooltipPanel", "PopupPanel", "PopupMenu" }) t.SetStylebox("panel", type, popup);
        foreach (var (state, face) in new[] { ("normal", m.Button), ("hover", m.ButtonHover), ("pressed", m.ButtonPressed), ("hover_pressed", m.ButtonPressed), ("disabled", m.ButtonDisabled) })
            t.SetStylebox(state, "Button", Nine(face, Space.M, Space.S, stretchDown: m.ButtonStretch));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetStylebox("tab_selected", "TabContainer", Nine(m.ButtonPressed, Space.M, Space.S, m.ButtonStretch)); t.SetStylebox("tab_unselected", "TabContainer", Nine(m.Button, Space.M, Space.S, m.ButtonStretch));
        t.SetStylebox("tab_hovered", "TabContainer", Nine(m.ButtonHover, Space.M, Space.S, m.ButtonStretch));
        // Inputs and dropdowns: the field cut into the material (fantasy's parchment groove, sci-fi's scanline screen), in its own ink.
        var field = Nine(m.Input, Space.S, Space.S); var focus = Flat(s, C(u.Accent) with { A = 0 }, C(u.Accent), 2); focus.DrawCenter = false;
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            t.SetStylebox("normal", type, field); t.SetStylebox("focus", type, focus); t.SetStylebox("read_only", type, field);
            t.SetColor("font_color", type, C(m.FieldText)); t.SetColor("font_placeholder_color", type, C(m.FieldDim));
            t.SetColor("caret_color", type, m.FieldText == u.Text ? C(u.Accent) : C(m.FieldText)); t.SetColor("selection_color", type, C(u.Accent) with { A = 0.35f });
            t.SetColor("font_uneditable_color", type, C(m.FieldDim));
        }
        foreach (var (state, light) in new[] { ("normal", 1f), ("hover", 1.08f), ("pressed", 0.9f), ("hover_pressed", 0.9f), ("disabled", 0.7f) })
        { var b = Nine(m.Input, Space.M, Space.S); b.ModulateColor = new Color(light, light, light); t.SetStylebox(state, "OptionButton", b); }
        t.SetStylebox("focus", "OptionButton", new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) t.SetColor(name, "OptionButton", C(m.FieldText));
        t.SetColor("font_disabled_color", "OptionButton", C(m.FieldDim)); t.SetConstant("modulate_arrow", "OptionButton", 1); t.SetConstant("arrow_margin", "OptionButton", Space.S);
        t.SetStylebox("panel", "ItemList", Flat(s, C(u.Panel), C(u.PanelBorder), 1));
        foreach (var type in new[] { "Label", "ItemList", "TooltipLabel", "PopupMenu", "TabContainer" }) t.SetColor("font_color", type, C(u.Text));
        t.SetColor("default_color", "RichTextLabel", C(u.Text)); t.SetColor("selection_color", "RichTextLabel", C(u.Accent) with { A = 0.35f });
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color" }) t.SetColor(name, "Button", C(u.ButtonText));
        // Disabled text on the button face: the face's own text colour, faded, which reads on a dark face (fantasy) and a light one alike.
        t.SetColor("font_pressed_color", "Button", C(u.Accent)); t.SetColor("font_disabled_color", "Button", C(u.ButtonText) with { A = 0.5f });
        t.SetColor("font_placeholder_color", "SpinBox", C(m.FieldDim));
        // A check box drawn in the theme's colours: the default icons are dark grey, which a dark panel hides.
        foreach (var (name, on, colour) in new[] { ("checked", true, C(u.Accent)), ("unchecked", false, C(u.Text)), ("checked_disabled", true, C(u.DimText)), ("unchecked_disabled", false, C(u.DimText)) })
        {
            t.SetIcon(name, "CheckBox", CheckIcon(on, colour, u.Body.Size));
            t.SetIcon(name.Replace("checked", "radio_checked").Replace("unradio_checked", "radio_unchecked"), "CheckBox", CheckIcon(on, colour, u.Body.Size));
        }
        // Dialog title bars: the panel's colours, not Godot's grey.
        var frame = Flat(s, C(u.Panel) with { A = 1 }, C(u.PanelBorder), Math.Max(1, s.BorderWidth)); frame.ExpandMarginTop = 32; frame.SetContentMarginAll(0);
        t.SetStylebox("embedded_border", "Window", frame); t.SetStylebox("embedded_unfocused_border", "Window", frame);
        t.SetColor("title_color", "Window", C(u.Text)); t.SetFont("title_font", "Window", FontOf(u.Display)); t.SetFontSize("title_font_size", "Window", u.Body.Size);
        t.SetConstant("title_height", "Window", 32);
        // CheckBox and a flat button draw on the panel, not on a button face: Text, and no button stylebox.
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed", "focus" }) t.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) t.SetColor(name, "CheckBox", C(u.Text));
        t.SetColor("font_disabled_color", "CheckBox", C(u.DimText));
        t.SetTypeVariation(Variation.FlatButton, "Button");
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed", "focus" })
        { var empty = new StyleBoxEmpty(); empty.SetContentMarginAll(0); empty.ContentMarginLeft = empty.ContentMarginRight = Space.XS; t.SetStylebox(state, Variation.FlatButton, empty); }
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color" }) t.SetColor(name, Variation.FlatButton, C(u.Text));
        foreach (var name in new[] { "font_pressed_color", "font_hover_pressed_color" }) t.SetColor(name, Variation.FlatButton, C(u.Accent));
        t.SetColor("font_disabled_color", Variation.FlatButton, C(u.DimText));
        t.SetColor("font_selected_color", "ItemList", C(u.Accent)); t.SetColor("font_selected_color", "TabContainer", C(u.ButtonText));
        // A hovered list row or menu item keeps the theme's text on its background: Godot's default hover is near-white text, unreadable on a light panel.
        t.SetColor("font_hovered_color", "ItemList", C(u.Text)); t.SetColor("font_hovered_selected_color", "ItemList", C(u.Accent)); t.SetColor("font_hover_color", "PopupMenu", C(u.Text));
        t.SetStylebox("hovered", "ItemList", Flat(s, C(u.Background), C(u.PanelBorder), 1, 0)); t.SetStylebox("hover", "PopupMenu", Flat(s, C(u.Background), C(u.Accent), 1, 0));
        // The loading bar (LoadingPanel): a thin track in the panel's border colour, filled in the accent.
        var track = Flat(s, C(u.Background), C(u.PanelBorder), 1, 0); track.SetCornerRadiusAll(Math.Min(s.CornerRadius, 3));
        var fill = Flat(s, C(u.Accent), C(u.Accent), 0, 0); fill.SetCornerRadiusAll(Math.Min(s.CornerRadius, 3));
        t.SetStylebox("background", "ProgressBar", track); t.SetStylebox("fill", "ProgressBar", fill);
        Variant(t, Variation.Badge, "PanelContainer", "panel", Flat(s, C(u.Panel), C(u.Accent), 2));
        Variant(t, Variation.Chip, "PanelContainer", "panel", Flat(s, C(u.Panel) with { A = 0 }, C(u.Accent), 1, Space.XS));
        Variant(t, Variation.DieFace, "PanelContainer", "panel", Flat(s, C(u.Dice.Face), C(u.Dice.Mark), 1, Space.XS));
        Variant(t, Variation.DieMiss, "PanelContainer", "panel", Flat(s, C(u.Dice.Face), C(u.Failure), 2, Space.XS));
        foreach (var (v, c) in new[] { (Variation.Dim, u.DimText), (Variation.Accent, u.Accent), (Variation.Success, u.Success), (Variation.Failure, u.Failure), (Variation.Warning, u.Warning) }) LabelVariant(t, v, c: C(c));
        LabelVariant(t, Variation.Display, u.Display); LabelVariant(t, Variation.Mono, UiThemes.Mono with { Size = UiThemes.Scaled(UiThemes.Mono.Size, u.TextSize) }, C(u.Text)); LabelVariant(t, Variation.Numerals, u.Numerals, C(u.Dice.Mark)); LabelVariant(t, Variation.Banner, u.Display with { Size = UiThemes.Scaled(48, u.TextSize) }, C(u.Accent));
        t.SetColor("font_outline_color", Variation.Banner, C(u.Background)); t.SetConstant("outline_size", Variation.Banner, 6);
        LabelVariant(t, Variation.BannerDetail, u.Display with { Size = UiThemes.Scaled(24, u.TextSize) }, C(u.Text));
        t.SetColor("font_outline_color", Variation.BannerDetail, C(u.Background)); t.SetConstant("outline_size", Variation.BannerDetail, 4);
        Vocabulary(t, u);
        return t;
    }

    /// <summary>The shared vocabulary: key cap, card, quiet band, tile, and a card's one lit button.</summary>
    static void Vocabulary(Godot.Theme t, UiTheme u)
    {
        var s = u.Shape; Color C(Rgba c) => Ui.Of(c);
        StyleBoxFlat Pad(StyleBoxFlat b, int x, int y) { b.ContentMarginLeft = b.ContentMarginRight = x; b.ContentMarginTop = b.ContentMarginBottom = y; return b; }
        var cap = Pad(Flat(s, C(u.Background) with { A = 0.35f }, C(u.DimText), 1, 0), Space.XS, 0); cap.SetCornerRadiusAll(3);
        LabelVariant(t, Variation.KeyCap, u.Numerals with { Size = Math.Max(11, u.Body.Size - 4) }, C(u.DimText)); t.SetStylebox("normal", Variation.KeyCap, cap);
        var m = u.Materials;
        Variant(t, Variation.Card, "PanelContainer", "panel", Nine(m.Panel, Space.S, Space.S));
        Variant(t, Variation.Quiet, "PanelContainer", "panel", Pad(Flat(s, C(u.Panel) with { A = 0.8f }, C(u.PanelBorder) with { A = 0.5f }, 1, 0), Space.S, Space.S));
        // A tile, and the same lit (its hover look at rest: the matching control under the pointer elsewhere) and round (an icon alone: the
        // action buttons over the selected unit).
        // A tile is a button of the material, S all round inside its frame.
        foreach (var (variation, lit) in new[] { (Variation.Tile, false), (Variation.TileLit, true) })
        {
            t.SetTypeVariation(variation, "Button");
            foreach (var (state, face) in new[] { ("normal", lit ? m.ButtonHover : m.Button), ("hover", m.ButtonHover), ("pressed", m.ButtonPressed), ("hover_pressed", m.ButtonPressed), ("disabled", m.ButtonDisabled) })
                t.SetStylebox(state, variation, Nine(face, Space.S, Space.S, stretchDown: m.ButtonStretch));
            t.SetStylebox("focus", variation, new StyleBoxEmpty());
            foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) t.SetColor(name, variation, C(u.ButtonText));
            t.SetColor("font_disabled_color", variation, C(u.ButtonText) with { A = 0.5f });
        }
        // An icon alone, frameless: S all round (spec 2026-11-21-theme-materials-design.md → Button padding).
        t.SetTypeVariation(Variation.IconOnly, "Button");
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed", "focus" }) { var empty = new StyleBoxEmpty(); empty.SetContentMarginAll(Space.S); t.SetStylebox(state, Variation.IconOnly, empty); }
        foreach (var (variation, lit, round) in new[] { (Variation.RoundTile, false, true), (Variation.RoundTileLit, true, true) })
        {
            t.SetTypeVariation(variation, "Button");
            var hover = (C(u.Background).Lerp(C(u.Accent), 0.18f), C(u.Accent));
            foreach (var (state, bg, edge) in new[] { ("normal", lit ? hover.Item1 : C(u.Background), lit ? hover.Item2 : C(u.PanelBorder)), ("hover", hover.Item1, hover.Item2),
                         ("pressed", C(u.Background).Lerp(C(u.Accent), 0.32f), C(u.Accent)), ("disabled", C(u.Background) with { A = 0.5f }, C(u.PanelBorder) with { A = 0.5f }) })
            {
                var box = Pad(Flat(s, bg with { A = Math.Max(bg.A, round ? 0.85f : 0.5f) }, edge, round && edge == C(u.Accent) ? 2 : 1, 0), Space.S, Space.S);   // over the table a lit round button takes a heavier edge
                if (round) { box.SetCornerRadiusAll(64); box.CornerDetail = 12; }   // a circle in every theme: a bevelled shape's corner detail would cut it to a diamond
                t.SetStylebox(state, variation, box);
            }
            t.SetStylebox("focus", variation, new StyleBoxEmpty());
            foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) t.SetColor(name, variation, C(u.Text));
            t.SetColor("font_disabled_color", variation, C(u.DimText));
        }
        t.SetTypeVariation(Variation.Primary, "Button");
        // The one lit action: the primary face (fantasy's red lacquer, sci-fi's lit key), brighter under the pointer, darker pressed.
        foreach (var (state, light) in new[] { ("normal", 1f), ("hover", 1.15f), ("pressed", 0.85f), ("hover_pressed", 0.85f) })
        { var b = Nine(m.Primary, Space.M, Space.S, stretchDown: m.ButtonStretch); b.ModulateColor = new Color(light, light, light); t.SetStylebox(state, Variation.Primary, b); }
        t.SetStylebox("disabled", Variation.Primary, Nine(m.ButtonDisabled, Space.M, Space.S, stretchDown: m.ButtonStretch));
        t.SetStylebox("focus", Variation.Primary, new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) t.SetColor(name, Variation.Primary, C(u.ButtonText));
        t.SetColor("font_disabled_color", Variation.Primary, C(u.ButtonText) with { A = 0.5f });
    }

    /// <summary>A material's nine-patch: corners fixed, edges and centre tiled (stretched down when stretchDown: a button's glass or
    /// lacquer shading runs top to bottom), the content margin its frame's inset plus the padding.</summary>
    public static StyleBoxTexture Nine(ThemeImage m, int padX, int padY, bool stretchDown = false)
    {
        var b = new StyleBoxTexture { Texture = Ui.Image(m.Path), AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit,
            AxisStretchVertical = stretchDown ? StyleBoxTexture.AxisStretchMode.Stretch : StyleBoxTexture.AxisStretchMode.Tile };
        b.SetTextureMarginAll(m.Margin);
        b.ContentMarginLeft = b.ContentMarginRight = m.Inset + padX; b.ContentMarginTop = b.ContentMarginBottom = m.Inset + padY;
        return b;
    }

    /// <summary>A square box outline in the colour, filled inside when on: the check box's icon at the body text's size.</summary>
    static ImageTexture CheckIcon(bool on, Color colour, int fontSize)
    {
        var n = Math.Max(14, fontSize - 2); var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var x = 0; x < n; x++)
            for (var y = 0; y < n; y++)
            {
                var edge = x < 2 || y < 2 || x >= n - 2 || y >= n - 2;
                var inner = x >= 4 && y >= 4 && x < n - 4 && y < n - 4;
                if (edge || on && inner) image.SetPixel(x, y, colour);
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A dialog button's least width (a size, not spacing): Cancel and a one-word primary both read as buttons.</summary>
    public const int DialogButtonWidth = 120;

    static void Variant(Godot.Theme t, string v, string baseType, string name, StyleBox box) { t.SetTypeVariation(v, baseType); t.SetStylebox(name, v, box); }

    static void LabelVariant(Godot.Theme t, string v, ThemeFont? f = null, Color? c = null)
    {
        // A RichTextLabel given the variation (the log's selectable rows) reads the same font and colour under its own item names.
        t.SetTypeVariation(v, "Label");
        if (f is not null) { t.SetFont("font", v, FontOf(f)); t.SetFontSize("font_size", v, f.Size); t.SetFont("normal_font", v, FontOf(f)); t.SetFontSize("normal_font_size", v, f.Size); }
        if (c is { } col) { t.SetColor("font_color", v, col); t.SetColor("default_color", v, col); }
    }

    static StyleBoxFlat Flat(PanelShape s, Color bg, Color border, int width, int? padding = null)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = s.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(width); b.SetCornerRadiusAll(s.CornerRadius); b.SetContentMarginAll(padding ?? Space.S); return b;
    }

    // Variable fonts at the theme's weight; a static font ignores the axis.
    static Font FontOf(ThemeFont f) => new FontVariation { BaseFont = GD.Load<FontFile>(f.Path),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = f.Weight } };
}
