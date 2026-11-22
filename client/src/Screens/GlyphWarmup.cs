using Godot;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>
/// Behind the loading curtain: every printable character drawn once in each font, size and outline the play screen's text uses, so the font's
/// glyph cache holds them before play. A glyph is rendered the first time a string needs it at a size and outline, and a hover label's
/// "Click to target", a unit card's rules or a ruler's digits otherwise pay for their new glyphs in the frame that first shows them.
/// The table's labels (hover names, rulers, a presence's name, casualty marks) are Label3Ds drawn for a frame; the Hud's are a label and a
/// button per theme variation, shaped while hidden.
/// </summary>
public static class GlyphWarmup
{
    /// <summary>ASCII, the punctuation and symbols the Hud's and the table's words use, and the dice faces (CombatLog.Pip). A symbol the theme's font
    /// lacks (⚠, ★, the faces) is looked up in the system's fonts the first time it is drawn, which costs a frame tens of milliseconds.</summary>
    public static readonly string Text = new string([.. Enumerable.Range(32, 95).Select(c => (char)c)]) + "·…—–✕″’×°→←↑↓›⇧⇄✓▸▾▶○●◉★⚠⟳⟲⏎⌄⌨☰Δ⚀⚁⚂⚃⚄⚅";

    static readonly string[] LabelVariations = ["", Variation.Banner, Variation.BannerDetail, Variation.Dim, Variation.Accent, Variation.Success, Variation.Failure,
        Variation.Warning, Variation.Display, Variation.Numerals, Variation.Mono];
    static readonly string[] ButtonVariations = ["", Variation.FlatButton, Variation.Tile, Variation.Primary, Variation.KeyCap];

    /// <summary>The nodes made: the table's under <paramref name="table"/>, the Hud's under <paramref name="hud"/>; free them a frame or more later.</summary>
    public static IReadOnlyList<Node> Warm(Node3D table, Control hud)
    {
        var made = new List<Node>();
        foreach (var l in new[]
        {
            new Label3D { FontSize = 40, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 10, NoDepthTest = true },   // HoverLabels' name
            new Label3D { FontSize = 32, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 8, NoDepthTest = true },    // its lock line
            new Label3D { FontSize = 36, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 10, NoDepthTest = true },   // a presence's name
            new Label3D { FontSize = 48, PixelSize = 0.01f },                                                                        // a ruler
            new Label3D { FontSize = 64, PixelSize = 0.01f },                                                                        // a casualty mark
        })
        {
            l.Text = Text; l.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled; l.Position = new Vector3(0, -3, 0);
            table.AddChild(l); made.Add(l);
        }
        foreach (var v in LabelVariations) made.Add(Shape(hud, new Label { Text = Text, ThemeTypeVariation = v, Visible = false }));
        // A wrapped label breaks its lines by word (ICU's break iterator, loaded on its first use): a unit card's note paid 13 ms for it.
        foreach (var v in LabelVariations)
            made.Add(Shape(hud, new Label { Text = Text + " " + Text, ThemeTypeVariation = v, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false, Size = new Vector2(240, 0) }));
        foreach (var v in ButtonVariations) made.Add(Shape(hud, new Button { Text = Text, ThemeTypeVariation = v, Visible = false }));
        return made;
    }

    static Control Shape(Control hud, Control c) { hud.AddChild(c); c.GetCombinedMinimumSize(); return c; }
}
