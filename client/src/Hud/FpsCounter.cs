using Godot;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>Options → Video → Show frame rate: frames a second in the window's top-right corner, over every screen, twice a second.
/// Clicks pass through. In the theme's text colour over an outline of its background, so it reads over any screen in either theme.</summary>
public partial class FpsCounter : CanvasLayer
{
    readonly Label _label = new() { ThemeTypeVariation = Variation.Numerals, MouseFilter = Control.MouseFilterEnum.Ignore };
    double _next;

    const int OutlinePx = 4;

    public FpsCounter() { Layer = 120; Visible = false; }

    public override void _Ready()
    {
        Restyle(); Ui.Changed += Restyle;
        _label.AddThemeConstantOverride("outline_size", OutlinePx);
        AddChild(_label);
        _label.GrowHorizontal = Control.GrowDirection.Begin;
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize, Space.S);
    }

    void Restyle()
    {
        _label.Theme = Ui.Theme;
        _label.AddThemeColorOverride("font_color", Ui.Text);
        _label.AddThemeColorOverride("font_outline_color", Ui.Background);
    }
    public override void _ExitTree() => Ui.Changed -= Restyle;

    public override void _Process(double delta)
    {
        if (!Visible || (_next -= delta) > 0) return;
        _next = 0.5;
        _label.Text = $"{Engine.GetFramesPerSecond():0} fps";
    }
}
