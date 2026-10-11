using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;

namespace Tabletop.Client.Hud;

/// <summary>Lower right: a button that opens the log above it. G toggles it. GameScreen.Layout places both through Place.</summary>
public partial class LogDrawer : Control
{
    Session _s = null!;
    readonly PanelContainer _drawer = new() { Visible = false };
    readonly CombatLogPanel _log = new();
    public CombatLogPanel CombatLog => _log;
    readonly Button _toggle = new() { Text = "Log (G)" };

    /// <summary>A failed undo or dice edit from the log, for the screen's toast.</summary>
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
        AddChild(_drawer); AddChild(_toggle);
        _toggle.Pressed += Toggle;
        SetOpen(session.Settings.LogOpen);
    }

    public void Toggle() { SetOpen(!_drawer.Visible); _s.UpdateSettings(x => x with { LogOpen = _drawer.Visible }); }

    /// <summary>The dice tray's click: open the log at that roll.</summary>
    public void OpenAt(Guid rollId) { if (!_drawer.Visible) Toggle(); _log.Reveal(rollId); }

    /// <summary>The button into its slot, and the panel into its slot when it shows.</summary>
    public void Place(HudRect button, HudRect? panel)
    {
        HudPlacement.Bottom(_toggle, button);
        if (panel is { } r && _drawer.Visible) HudPlacement.Fill(_drawer, r);
    }

    void SetOpen(bool open) { _drawer.Visible = open; _toggle.Text = open ? "Close log (G)" : "Log (G)"; OpenChanged?.Invoke(); }

    public override void _Input(InputEvent e)
    {
        if (KeyOverlay.IntentOf(e, GetViewport()) != Intent.ToggleLog) return;
        Toggle(); GetViewport().SetInputAsHandled();
    }
}
