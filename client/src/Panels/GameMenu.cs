using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>
/// The game menu of lobby, setup, play, replay and watching (spec 2026-11-21-setup-screen-layout-design.md → Game menu, GameMenuView): Resume,
/// Options, Leave game, Home on a card over a dimmed window. ☰ opens it, and Esc when nothing else takes Esc (GameMenuView.OnEsc over
/// <see cref="State"/>); Esc or Resume closes it. It hears keys in _Input, ahead of the table's viewport.
/// </summary>
public partial class GameMenu : Control
{
    VBoxContainer _items = null!;
    /// <summary>What else would use an Esc now (the Hud's overlays, the open Ask, the table); the menu fills in its own and the text focus.</summary>
    public Func<EscState> State { get; init; } = () => new EscState();
    public Func<bool> CanLeave { get; init; } = () => false;
    public Action Options { get; init; } = () => { };
    public Action Leave { get; init; } = () => { };
    public Action Home { get; init; } = () => { };
    /// <summary>Raised as the menu opens: the screen drops what the pointer was hovering on the table, which the dim covers now.</summary>
    public event Action? Opened;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = Ui.Background with { A = 0.6f }, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(dim);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); };   // a click beside the card resumes
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var card = new PanelContainer { ThemeTypeVariation = Variation.Card, CustomMinimumSize = new Vector2(280, 0) }; center.AddChild(card);
        _items = new VBoxContainer(); _items.AddThemeConstantOverride("separation", Space.S); card.AddChild(_items);
    }

    public void Open()
    {
        foreach (var c in _items.GetChildren()) { _items.RemoveChild(c); c.QueueFree(); }
        foreach (var item in GameMenuView.Items(CanLeave()))
        {
            var b = new Button { Text = GameMenuView.Text(item), FocusMode = FocusModeEnum.None, ThemeTypeVariation = item == GameMenuItem.Resume ? Variation.Primary : "" };
            var it = item;
            b.Pressed += () => { Close(); Pick(it); };
            _items.AddChild(b);
        }
        Visible = true;
        MoveToFront();
        Opened?.Invoke();
    }

    public void Close() => Visible = false;

    void Pick(GameMenuItem item)
    {
        switch (item)
        {
            case GameMenuItem.Options: Options(); break;
            case GameMenuItem.Leave: Leave(); break;
            case GameMenuItem.Home: Home(); break;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        var text = GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        switch (GameMenuView.OnEsc(State() with { MenuOpen = Visible, TextFocus = text }))
        {
            case EscPress.Open: Open(); GetViewport().SetInputAsHandled(); break;
            case EscPress.Close: Close(); GetViewport().SetInputAsHandled(); break;
        }
    }
}
