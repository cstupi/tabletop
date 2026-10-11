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
        foreach (var type in new[] { "PanelContainer", "Panel", "TooltipPanel", "PopupPanel", "PopupMenu", "AcceptDialog", "TabContainer" }) t.SetStylebox("panel", type, panel);
        StyleBox Btn(float light) { var b = Box(s.ButtonNinePatch, s.ButtonMargin, s, C(u.Background).Lightened(light), C(u.PanelBorder), s.BorderWidth); if (b is StyleBoxTexture x) x.ModulateColor = Colors.White.Lightened(light); return b; }
        foreach (var (state, light) in new[] { ("normal", 0f), ("hover", 0.15f), ("pressed", 0.3f), ("disabled", -0.2f) }) t.SetStylebox(state, "Button", Btn(light));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty()); t.SetStylebox("tab_selected", "TabContainer", Btn(0.3f)); t.SetStylebox("tab_unselected", "TabContainer", Btn(0f)); t.SetStylebox("tab_hovered", "TabContainer", Btn(0.15f));
        t.SetStylebox("normal", "LineEdit", Flat(s, C(u.Panel), C(u.PanelBorder), 1)); t.SetStylebox("focus", "LineEdit", Flat(s, C(u.Panel), C(u.Accent), 1)); t.SetStylebox("panel", "ItemList", Flat(s, C(u.Panel), C(u.PanelBorder), 1));
        foreach (var type in new[] { "Label", "CheckBox", "LineEdit", "ItemList", "TooltipLabel", "PopupMenu", "TabContainer" }) t.SetColor("font_color", type, C(u.Text));
        foreach (var name in new[] { "font_color", "font_hover_color", "font_focus_color" }) t.SetColor(name, "Button", C(u.ButtonText));
        t.SetColor("font_pressed_color", "Button", C(u.Accent)); t.SetColor("font_disabled_color", "Button", C(u.DimText)); t.SetColor("font_selected_color", "ItemList", C(u.Accent)); t.SetColor("font_selected_color", "TabContainer", C(u.ButtonText));
        Variant(t, Variation.Badge, "PanelContainer", "panel", Flat(s, C(u.Panel), C(u.Accent), 2));
        Variant(t, Variation.Chip, "PanelContainer", "panel", Flat(s, C(u.Panel) with { A = 0 }, C(u.Accent), 1, 4));
        Variant(t, Variation.DieFace, "PanelContainer", "panel", Flat(s, C(u.Dice.Face), C(u.Dice.Mark), 1, 2));
        foreach (var (v, c) in new[] { (Variation.Dim, u.DimText), (Variation.Accent, u.Accent), (Variation.Success, u.Success), (Variation.Failure, u.Failure), (Variation.Warning, u.Warning) }) LabelVariant(t, v, c: C(c));
        LabelVariant(t, Variation.Display, u.Display); LabelVariant(t, Variation.Numerals, u.Numerals, C(u.Dice.Mark)); LabelVariant(t, Variation.Banner, u.Display with { Size = 48 }, C(u.Accent));
        t.SetColor("font_outline_color", Variation.Banner, C(u.Background)); t.SetConstant("outline_size", Variation.Banner, 6);
        return t;
    }

    static void Variant(Godot.Theme t, string v, string baseType, string name, StyleBox box) { t.SetTypeVariation(v, baseType); t.SetStylebox(name, v, box); }

    static void LabelVariant(Godot.Theme t, string v, ThemeFont? f = null, Color? c = null)
    { t.SetTypeVariation(v, "Label"); if (f is not null) { t.SetFont("font", v, FontOf(f)); t.SetFontSize("font_size", v, f.Size); } if (c is { } col) t.SetColor("font_color", v, col); }

    static StyleBox Box(string? ninePatch, int margin, PanelShape s, Color bg, Color border, int width)
    {
        if (ninePatch is null) return Flat(s, bg, border, width);
        var b = new StyleBoxTexture { Texture = GD.Load<Texture2D>(ninePatch) }; b.SetTextureMarginAll(margin); b.SetContentMarginAll(s.Padding); return b;
    }

    static StyleBoxFlat Flat(PanelShape s, Color bg, Color border, int width, int? padding = null)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = s.Clipped ? 1 : 8 };
        b.SetBorderWidthAll(width); b.SetCornerRadiusAll(s.CornerRadius); b.SetContentMarginAll(padding ?? s.Padding); return b;
    }

    // Variable fonts at the theme's weight; a static font ignores the axis.
    static Font FontOf(ThemeFont f) => new FontVariation { BaseFont = GD.Load<FontFile>(f.Path),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = f.Weight } };
}
