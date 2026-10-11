using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre, above the decision box: the latest roll as the combat log's collapsed entry (client UX spec → Dice tray).
/// Clicking it opens the log there; with Animate on the faces tumble for 0.4 s before settling.</summary>
public partial class DiceTray : PanelContainer
{
    static readonly Color Pass = new(0.35f, 0.8f, 0.4f), Fail = new(0.5f, 0.5f, 0.5f), Tumbling = Colors.White;
    const double TumbleSeconds = 0.4;
    Session _s = null!; Control? _below;
    readonly Button _line = new() { Flat = true };
    readonly Random _faces = new();   // decoration while tumbling; the dice shown settled are the record's
    RollEntry? _shown; double _tumble;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _place;

    /// <summary>The shown roll's RollRecord id, when the player clicks it.</summary>
    public event Action<Guid>? RollClicked;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, 8);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Begin;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_line);
        _line.Pressed += () => { if (_shown is { } r) RollClicked?.Invoke(r.Record.Id); };
        _onEvents = _ => Render(); _onBoard = Render;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
        Render();
    }

    /// <summary>Sit above the decision box, whatever its height: re-placed when it resizes or shows or hides.</summary>
    public void Follow(Control below)
    {
        _below = below; _place = Place;
        below.Resized += _place; below.VisibilityChanged += _place;
        Place();
    }

    public override void _ExitTree()
    {
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
        if (_below is not null && IsInstanceValid(_below)) { _below.Resized -= _place; _below.VisibilityChanged -= _place; }
    }

    void Place()
    {
        var lift = _below is not null && IsInstanceValid(_below) && _below.Visible ? _below.Size.Y + 16 : 8;
        OffsetBottom = -lift;
        OffsetTop = OffsetBottom - GetCombinedMinimumSize().Y;
    }

    void Render()
    {
        var latest = _s.Projection is { } p && _s.Game is { } g ? CombatLog.Latest(_s.Log, new Names(g, p)) : null;
        Visible = latest is not null;
        var changed = latest is not null && (_shown is null || _shown.Record.Id != latest.Record.Id || !_shown.Dice.SequenceEqual(latest.Dice));
        _shown = latest;
        if (latest is null) return;
        if (changed) _tumble = _s.Settings.Animate ? TumbleSeconds : 0;
        Paint();
        Place();
    }

    public override void _Process(double delta)
    {
        if (_tumble <= 0) return;
        _tumble -= delta;
        Paint();
    }

    void Paint()
    {
        if (_shown is not { } r) return;
        var settled = _tumble <= 0;
        _line.Text = r.Collapsed(face => CombatLog.Pip(settled ? face : _faces.Next(1, 7)));
        _line.Modulate = !settled ? Tumbling : r.Successes > 0 ? Pass : Fail;
    }
}
