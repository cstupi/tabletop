using Godot;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>Options → Video → Show frame rate: frames a second in the window's top-left corner, over every screen, twice a second.
/// Clicks pass through.</summary>
public partial class FpsCounter : CanvasLayer
{
    readonly Label _label = new() { ThemeTypeVariation = Variation.Numerals, MouseFilter = Control.MouseFilterEnum.Ignore };
    double _next;

    public FpsCounter() { Layer = 120; Visible = false; }

    public override void _Ready()
    {
        _label.Position = new Vector2(Space.S, Space.S);
        _label.Theme = Ui.Theme; Ui.Changed += Restyle;
        AddChild(_label);
    }

    void Restyle() => _label.Theme = Ui.Theme;
    public override void _ExitTree() => Ui.Changed -= Restyle;

    public override void _Process(double delta)
    {
        if (!Visible || (_next -= delta) > 0) return;
        _next = 0.5;
        _label.Text = $"{Engine.GetFramesPerSecond():0} fps";
    }
}
