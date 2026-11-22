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
        StyleBox panel = Box(s.PanelNinePatch, s.PanelMargin, s, C(u.Panel), C(u.PanelBorder), s.BorderWidth);
        foreach (var type in new[] { "PanelContainer", "Panel", "AcceptDialog", "TabContainer" }) t.SetStylebox("panel", type, panel);
        // A popup floats over other controls: its panel is opaque, or the text under it reads through a translucent panel colour.
        StyleBox popup = Box(s.PanelNinePatch, s.PanelMargin, s, C(u.Panel) with { A = 1 }, C(u.PanelBorder), s.BorderWidth);
        foreach (var type in new[] { "TooltipPanel", "PopupPanel", "PopupMenu" }) t.SetStylebox("panel", type, popup);
        StyleBox Btn(float light) { var b = Box(s.ButtonNinePatch, s.ButtonMargin, s, C(u.Background).Lightened(light), C(u.PanelBorder), s.BorderWidth); if (b is StyleBoxTexture x) x.ModulateColor = light >= 0 ? Grey.Lightened(light) : Grey.Darkened(-light); return b; }
        foreach (var (state, light) in new[] { ("normal", 0f), ("hover", 0.15f), ("pressed", 0.3f), ("disabled", -0.2f) }) t.SetStylebox(state, "Button", Btn(light));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty()); t.SetStylebox("tab_selected", "TabContainer", Btn(0.3f)); t.SetStylebox("tab_unselected", "TabContainer", Btn(0f)); t.SetStylebox("tab_hovered", "TabContainer", Btn(0.15f));
        t.SetStylebox("normal", "LineEdit", Flat(s, C(u.Panel), C(u.PanelBorder), 1)); t.SetStylebox("focus", "LineEdit", Flat(s, C(u.Panel), C(u.Accent), 1)); t.SetStylebox("panel", "ItemList", Flat(s, C(u.Panel), C(u.PanelBorder), 1));
        foreach (var type in new[] { "Label", "LineEdit", "ItemList", "TooltipLabel", "PopupMenu", "TabContainer" }) t.SetColor("font_color", type, C(u.Text));
        t.SetColor("default_color", "RichTextLabel", C(u.Text)); t.SetColor("selection_color", "RichTextLabel", C(u.Accent) with { A = 0.35f });
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color" }) t.SetColor(name, "Button", C(u.ButtonText));
        // Disabled text on the button face: the face's own text colour, faded, which reads on a dark face (fantasy) and a light one alike.
        t.SetColor("font_pressed_color", "Button", C(u.Accent)); t.SetColor("font_disabled_color", "Button", C(u.ButtonText) with { A = 0.5f });
        foreach (var type in new[] { "LineEdit", "SpinBox" }) t.SetColor("font_placeholder_color", type, C(u.DimText));
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
        LabelVariant(t, Variation.Display, u.Display); LabelVariant(t, Variation.Mono, UiThemes.Mono, C(u.Text)); LabelVariant(t, Variation.Numerals, u.Numerals, C(u.Dice.Mark)); LabelVariant(t, Variation.Banner, u.Display with { Size = 48 }, C(u.Accent));
        t.SetColor("font_outline_color", Variation.Banner, C(u.Background)); t.SetConstant("outline_size", Variation.Banner, 6);
        LabelVariant(t, Variation.BannerDetail, u.Display with { Size = 24 }, C(u.Text));
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
        Variant(t, Variation.Card, "PanelContainer", "panel", Flat(s, C(u.Panel) with { A = 1 }, C(u.PanelBorder), Math.Max(1, s.BorderWidth), Space.S));
        Variant(t, Variation.Quiet, "PanelContainer", "panel", Pad(Flat(s, C(u.Panel) with { A = 0.8f }, C(u.PanelBorder) with { A = 0.5f }, 1, 0), Space.S, Space.S));
        // A tile, and the same lit (its hover look at rest: the matching control under the pointer elsewhere) and round (an icon alone: the
        // action buttons over the selected unit).
        foreach (var (variation, lit, round) in new[] { (Variation.Tile, false, false), (Variation.TileLit, true, false), (Variation.RoundTile, false, true), (Variation.RoundTileLit, true, true) })
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
        foreach (var (state, shade) in new[] { ("normal", 0f), ("hover", 0.15f), ("pressed", 0.3f) })
            t.SetStylebox(state, Variation.Primary, Pad(Flat(s, C(u.Accent).Lightened(shade), C(u.Accent).Lightened(shade), 1, 0), Space.M, Space.S));
        t.SetStylebox("disabled", Variation.Primary, Pad(Flat(s, C(u.Accent) with { A = 0.22f }, C(u.Accent) with { A = 0.4f }, 1, 0), Space.M, Space.S));
        t.SetStylebox("focus", Variation.Primary, new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) t.SetColor(name, Variation.Primary, C(u.Background) with { A = 1 });
        t.SetColor("font_disabled_color", Variation.Primary, C(u.Text) with { A = 0.5f });
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

    // A nine-patch button modulated from grey, so lightening for hover and pressed shows.
    static readonly Color Grey = new(0.85f, 0.85f, 0.85f);

    static void Variant(Godot.Theme t, string v, string baseType, string name, StyleBox box) { t.SetTypeVariation(v, baseType); t.SetStylebox(name, v, box); }

    static void LabelVariant(Godot.Theme t, string v, ThemeFont? f = null, Color? c = null)
    {
        // A RichTextLabel given the variation (the log's selectable rows) reads the same font and colour under its own item names.
        t.SetTypeVariation(v, "Label");
        if (f is not null) { t.SetFont("font", v, FontOf(f)); t.SetFontSize("font_size", v, f.Size); t.SetFont("normal_font", v, FontOf(f)); t.SetFontSize("normal_font_size", v, f.Size); }
        if (c is { } col) { t.SetColor("font_color", v, col); t.SetColor("default_color", v, col); }
    }

    static StyleBox Box(string? ninePatch, int margin, PanelShape s, Color bg, Color border, int width)
    {
        if (ninePatch is null) return Flat(s, bg, border, width);
        var b = new StyleBoxTexture { Texture = GD.Load<Texture2D>(ninePatch) }; b.SetTextureMarginAll(margin); b.SetContentMarginAll(Space.S); return b;
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
