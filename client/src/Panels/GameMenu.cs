using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>
/// The game menu of lobby and setup (spec 2026-11-21-setup-screen-layout-design.md → Game menu, GameMenuView): Resume, Options, Leave game, Home
/// on a card over a dimmed window. ☰ opens it, and Esc when nothing else takes Esc (no text focus, nothing selected or held on the table);
/// Esc or Resume closes it. It hears keys in _Input, ahead of the table's viewport.
/// </summary>
public partial class GameMenu : Control
{
    VBoxContainer _items = null!;
    /// <summary>Whether the table uses an Esc now: a selection, held pieces, picked models.</summary>
    public Func<bool> TableBusy { get; init; } = () => false;
    public Func<bool> CanLeave { get; init; } = () => false;
    public Action Options { get; init; } = () => { };
    public Action Leave { get; init; } = () => { };
    public Action Home { get; init; } = () => { };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = Ui.Background with { A = 0.6f }, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(dim);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); };   // a click beside the card resumes
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center);
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
        switch (GameMenuView.OnEsc(Visible, text, TableBusy()))
        {
            case EscPress.Open: Open(); GetViewport().SetInputAsHandled(); break;
            case EscPress.Close: Close(); GetViewport().SetInputAsHandled(); break;
        }
    }
}
