using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre, above the decision box (GameScreen.Layout places it): the latest roll as the combat log's collapsed entry (client UX spec → Dice tray).
/// Clicking it opens the log there; with Animate on the faces tumble for 0.4 s before settling, changing in place each frame.</summary>
public partial class DiceTray : PanelContainer
{
    const double TumbleSeconds = 0.4;
    Session _s = null!;
    readonly Button _line = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton };
    readonly HBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly Random _faces = new();   // decoration while tumbling; the dice shown settled are the record's
    RollEntry? _shown; double _tumble; readonly List<Control> _tumbling = [];
    Action<RenderCause>? _onFrame; Action? _onTheme;

    bool _covered;
    /// <summary>The log is open and holds the same roll: the tray hides and gives its slot to the decision box.</summary>
    public bool Covered { get => _covered; set { if (_covered == value) return; _covered = value; Render(); } }

    /// <summary>The shown roll's RollRecord id, when the player clicks it.</summary>
    public event Action<Guid>? RollClicked;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_line);
        _line.AddChild(_row);   // a Button lays out no children: the row fills it and sets its minimum size
        _row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _row.MinimumSizeChanged += () => _line.CustomMinimumSize = new(0, _row.GetCombinedMinimumSize().Y);   // the height only: a long line clips, the tooltip has all of it
        _line.ClipContents = true;
        _line.Pressed += () => { if (_shown is { } r) RollClicked?.Invoke(r.Record.Id); };
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        _onTheme = Paint; Ui.Changed += _onTheme;   // the dice faces are the theme's
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.Frame.Render -= _onFrame;
    }

    void Render()
    {
        var latest = _s.Projection is not null ? _s.CombatLog?.Latest : null;
        Visible = latest is not null && !_covered;
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
        if (_tumble <= 0) { Paint(); return; }                      // settled: the record's dice and the result
        foreach (var face in _tumbling) DiceFaces.SetRaw(face, _faces.Next(1, 7));   // tumbling: the faces change in place
    }

    void Paint()
    {
        if (_shown is not { } r) return;
        var settled = _tumble <= 0;
        _line.TooltipText = r.Collapsed(CombatLog.Pip);
        foreach (var c in _row.GetChildren()) { _row.RemoveChild(c); c.QueueFree(); }
        _tumbling.Clear();
        if (r.DieLabels is { Count: > 0 }) { Add(new Label { Text = r.Collapsed(CombatLog.Pip) }); return; }   // a roll-off: the team words go between the faces
        Add(new Label { Text = r.Title });
        foreach (var d in r.Dice)
        {
            var face = settled ? d.Raw : _faces.Next(1, 7);
            // An unjudged die shows neither pass nor fail, so it is drawn as a plain (unfaded) face.
            var c = DiceFaces.Of(!settled ? new Die(face, face, true) : new Die(d.Raw, d.Modified, !(r.Judged && d.Judged) || d.Success));
            Add(c);
            if (!settled) _tumbling.Add(c);
        }
        // An unjudged roll has no pass or fail, so no result label; a roll-off whose dice no team labels may still name the winner.
        if (settled && r.Judged) Add(new Label { Text = r.Result, ThemeTypeVariation = r.Successes > 0 ? Variation.Success : Variation.Dim });
        else if (settled && r.Outcome.Length > 0) Add(new Label { Text = r.Outcome });
    }

    void Add(Control c) { Ignore(c); _row.AddChild(c); }

    /// <summary>The click belongs to the button: nothing in the row takes the mouse.</summary>
    static void Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in c.GetChildren()) if (child is Control k) Ignore(k);
    }
}
