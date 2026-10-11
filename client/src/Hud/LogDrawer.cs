using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;

namespace Tabletop.Client.Hud;

/// <summary>Lower right: a button that opens the log upward from the corner. G toggles it.</summary>
public partial class LogDrawer : VBoxContainer
{
    Session _s = null!;
    readonly PanelContainer _drawer = new() { Visible = false, CustomMinimumSize = new Vector2(640, 420) };
    readonly CombatLogPanel _log = new();
    public CombatLogPanel CombatLog => _log;
    readonly Button _toggle = new() { Text = "Log (G)", SizeFlagsHorizontal = SizeFlags.ShrinkEnd };

    /// <summary>A failed undo or dice edit from the log, for the screen's toast.</summary>
    public event Action<string>? Reported;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.Minsize, 8);
        GrowHorizontal = GrowDirection.Begin; GrowVertical = GrowDirection.Begin;
        Alignment = AlignmentMode.End;
        var inner = new VBoxContainer(); _drawer.AddChild(inner);
        inner.AddChild(_log); _log.Bind(session, view, message => Reported?.Invoke(message));
        AddChild(_drawer); AddChild(_toggle);
        _toggle.Pressed += Toggle;
        SetOpen(session.Settings.LogOpen);
    }

    public void Toggle() { SetOpen(!_drawer.Visible); _s.UpdateSettings(x => x with { LogOpen = _drawer.Visible }); }

    /// <summary>The dice tray's click: open the log at that roll.</summary>
    public void OpenAt(Guid rollId) { if (!_drawer.Visible) Toggle(); _log.Reveal(rollId); }

    void SetOpen(bool open) { _drawer.Visible = open; _toggle.Text = open ? "Close log (G)" : "Log (G)"; }

    public override void _Input(InputEvent e)
    {
        if (KeyOverlay.IntentOf(e, GetViewport()) != Intent.ToggleLog) return;
        Toggle(); GetViewport().SetInputAsHandled();
    }
}
