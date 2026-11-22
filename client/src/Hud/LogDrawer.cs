using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>Top right: the theme's `log` icon as a button (held down while open; the tooltip its word and the log key's cap) that opens the log
/// below it, down the right side. The log key toggles it. GameScreen.Layout places both through Place.</summary>
public partial class LogDrawer : Control
{
    Session _s = null!;
    readonly PanelContainer _drawer = new() { Visible = false };
    readonly CombatLogPanel _log = new();
    public CombatLogPanel CombatLog => _log;
    readonly Button _toggle = new() { FocusMode = FocusModeEnum.None };
    string _cap = Hotkeys.Bindings.Cap(Intent.ToggleLog);
    /// <summary>The Log button, whose size the Hud's corner buttons take.</summary>
    public Control Toggle => _toggle;
    /// <summary>The open log panel; hidden while the drawer is closed.</summary>
    public Control Panel => _drawer;

    /// <summary>A failed undo from the log, for the screen's toast.</summary>
    public event Action<string>? Reported;
    /// <summary>The panel opened or closed: the Hud lays out again.</summary>
    public event Action? OpenChanged;
    public bool Open => _drawer.Visible;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var inner = new VBoxContainer(); _drawer.AddChild(inner);
        inner.AddChild(_log); _log.Bind(session, view, message => Reported?.Invoke(message));
        AddChild(_drawer); AddChild(_toggle); Refill();
        _toggle.Pressed += Flip;
        Ui.Changed += Refill; session.SettingsChanged += OnSettings;
        SetOpen(session.Settings.LogOpen);
    }

    void Refill() { _cap = Hotkeys.Bindings.Cap(Intent.ToggleLog); HudBits.IconFace(_toggle, "log"); _toggle.TooltipText = $"Log ({_cap})"; }
    /// <summary>A rebind of the log's key shows on the button.</summary>
    void OnSettings() { if (Hotkeys.Bindings.Cap(Intent.ToggleLog) != _cap) Refill(); }
    public override void _ExitTree() { Ui.Changed -= Refill; if (_s is not null) _s.SettingsChanged -= OnSettings; }

    public void Flip() { SetOpen(!_drawer.Visible); _s.UpdateSettings(x => x with { LogOpen = _drawer.Visible }); }

    /// <summary>The dice tray's click: open the log at that roll.</summary>
    public void OpenAt(Guid rollId) { if (!_drawer.Visible) Flip(); _log.Reveal(rollId); }

    /// <summary>The button into its slot, and the panel into its slot when it shows.</summary>
    public void Place(HudRect button, HudRect? panel)
    {
        HudPlacement.Top(_toggle, button);
        if (panel is { } r && _drawer.Visible) HudPlacement.Fill(_drawer, r);
    }

    void SetOpen(bool open) { _drawer.Visible = open; _log.Shown = open; _toggle.ToggleMode = true; _toggle.SetPressedNoSignal(open); OpenChanged?.Invoke(); }

    public override void _Input(InputEvent e)
    {
        if (KeyOverlay.IntentOf(e, GetViewport()) != Intent.ToggleLog) return;
        Flip(); GetViewport().SetInputAsHandled();
    }
}
