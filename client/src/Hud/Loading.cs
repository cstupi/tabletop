using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>One wait (spec 2026-10-25-loading-design.md): its line of text and a bar, filled to the count when the work is counted and
/// running back and forth when it is not. <see cref="Strip"/> lays them in a row for the Hud's loading slot; otherwise stacked.</summary>
public partial class LoadingPanel : PanelContainer
{
    public bool Strip { get; init; }
    readonly Label _text = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly ProgressBar _bar = new() { ShowPercentage = false, MinValue = 0, MaxValue = 1, Step = 0, MouseFilter = MouseFilterEnum.Ignore };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        BoxContainer box = Strip ? new HBoxContainer() : new VBoxContainer();
        box.MouseFilter = MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", Strip ? 12 : 10);
        if (Strip)
        {
            _text.SizeFlagsHorizontal = SizeFlags.ExpandFill; _text.VerticalAlignment = VerticalAlignment.Center;
            _bar.CustomMinimumSize = new Vector2(96, 6); _bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        }
        else
        {
            _text.HorizontalAlignment = HorizontalAlignment.Center; _text.ThemeTypeVariation = Variation.Display;
            _bar.CustomMinimumSize = new Vector2(320, 6);
        }
        box.AddChild(_text); box.AddChild(_bar); AddChild(box);
    }

    public void Set(LoadingLine line)
    {
        if (_text.Text != line.Text) _text.Text = line.Text;
        _bar.Indeterminate = !line.HasBar;
        if (line.HasBar) _bar.Value = line.Fraction;
    }
}

/// <summary>The whole window in the theme's background with a <see cref="LoadingPanel"/> at its centre: entering a game until its first
/// table stands, and the shader warm-up. It takes the mouse, so nothing under it is clicked while it shows. The panel shows only
/// once the wait has lasted 200 ms (<see cref="LoadingGate"/>); a shorter one is the plain background.</summary>
public partial class LoadingCurtain : Control
{
    readonly ColorRect _back = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly LoadingPanel _panel = new();
    /// <summary>How long a wait lasts before its panel shows.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(200);
    LoadingGate? _gate;
    Action? _onTheme;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        _back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_back);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(centre);
        centre.AddChild(_panel);
        _onTheme = Recolour; Ui.Changed += _onTheme; Recolour();
        // Under a Node (Main, for the shader warm-up) the anchors have no parent rect: the curtain follows the window's.
        if (GetParent() is not Control) { Fit(); GetViewport().SizeChanged += Fit; }
    }

    void Fit() { Position = Vector2.Zero; Size = GetViewportRect().Size; }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (GetParent() is not Control) GetViewport().SizeChanged -= Fit;
    }

    void Recolour() => _back.Color = Ui.Of(Ui.Current.Background);

    /// <summary>Null hides the curtain.</summary>
    public void Set(LoadingLine? line)
    {
        var shown = (_gate ??= new LoadingGate(Delay)).Update(line is not null, TimeSpan.FromMilliseconds(Time.GetTicksMsec()));
        Visible = line is not null;
        _panel.Visible = shown;
        if (line is not null) _panel.Set(line);
    }
}
