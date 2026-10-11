using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre, above the decision box (GameScreen.Layout places it): the latest roll as the combat log's collapsed entry (client UX spec → Dice tray).
/// Clicking it opens the log there; with Animate on the faces tumble for 0.4 s before settling.</summary>
public partial class DiceTray : PanelContainer
{
    const double TumbleSeconds = 0.4;
    Session _s = null!;
    readonly Button _line = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton };
    readonly HBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly Random _faces = new();   // decoration while tumbling; the dice shown settled are the record's
    RollEntry? _shown; double _tumble;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _onTheme;

    /// <summary>The shown roll's RollRecord id, when the player clicks it.</summary>
    public event Action<Guid>? RollClicked;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_line);
        _line.AddChild(_row);   // a Button lays out no children: the row fills it and sets its minimum size
        _row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _row.MinimumSizeChanged += () => _line.CustomMinimumSize = _row.GetCombinedMinimumSize();
        _line.Pressed += () => { if (_shown is { } r) RollClicked?.Invoke(r.Record.Id); };
        _onEvents = _ => Render(); _onBoard = Render;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
        _onTheme = Paint; Ui.Changed += _onTheme;   // the dice faces are the theme's
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
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
        foreach (var c in _row.GetChildren()) { _row.RemoveChild(c); c.QueueFree(); }
        Add(new Label { Text = r.Title });
        foreach (var d in r.Dice)
        {
            var face = settled ? d.Raw : _faces.Next(1, 7);
            // An unjudged die shows neither pass nor fail, so it is drawn as a plain (unfaded) face.
            Add(DiceFaces.Of(!settled ? new Die(face, face, true) : new Die(d.Raw, d.Modified, !(r.Judged && d.Judged) || d.Success)));
        }
        // An unjudged roll has no pass or fail, so no result label.
        if (settled && r.Judged) Add(new Label { Text = r.Result, ThemeTypeVariation = r.Successes > 0 ? Variation.Success : Variation.Dim });
    }

    void Add(Control c) { Ignore(c); _row.AddChild(c); }

    /// <summary>The click belongs to the button: nothing in the row takes the mouse.</summary>
    static void Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in c.GetChildren()) if (child is Control k) Ignore(k);
    }
}
