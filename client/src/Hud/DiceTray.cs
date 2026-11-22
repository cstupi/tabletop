using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Right column, above the decision box (GameScreen.Layout places it): the latest roll (UX redesign part 2 → Dice tray): the dice icon and
/// the roll's title, then its faces at 40 px framed pass or fail and the result in the outcome's colour.
/// Clicking it opens the log there; with Animate on the faces tumble for 0.4 s before settling, changing in place each frame.</summary>
public partial class DiceTray : PanelContainer
{
    const double TumbleSeconds = 0.4;
    Session _s = null!;
    readonly Button _line = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton };
    readonly VBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore };   // the title line over the faces, which wrap inside the tray's slot
    readonly Random _faces = new();   // decoration while tumbling; the dice shown settled are the record's
    RollEntry? _shown; double _tumble; readonly List<Control> _tumbling = [];
    Action<RenderCause>? _onFrame; Action? _onTheme;

    bool _covered;
    Action? _onHud;

    /// <summary>The shown roll's RollRecord id, when the player clicks it.</summary>
    public event Action<Guid>? RollClicked;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_line);
        _line.AddChild(_row);   // a Button lays out no children: the row fills it and sets its minimum size
        _row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _row.MinimumSizeChanged += () => _line.CustomMinimumSize = new(0, _row.GetCombinedMinimumSize().Y);   // the height only: a long line wraps to a second row
        _line.Pressed += () => { if (_shown is { } r) RollClicked?.Invoke(r.Record.Id); };
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        // The open log holds the latest roll: the frame gives the tray no slot then (HudFrame.Covered), and it hides.
        _onHud = () => { if (_covered == _s.Hud.Covered(HudPart.Dice)) return; _covered = !_covered; Render(); };
        session.HudChanged += _onHud; _covered = session.Hud.Covered(HudPart.Dice);
        _onTheme = Paint; Ui.Changed += _onTheme;   // the dice faces are the theme's
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) { _s.Frame.Render -= _onFrame; _s.HudChanged -= _onHud; }
    }

    void Render()
    {
        var latest = _s.Projection is not null ? _s.CombatLog?.Latest : null;
        Visible = latest is not null && !_covered;
        var changed = latest is not null && (_shown is null || _shown.Record.Id != latest.Record.Id || !_shown.Dice.SequenceEqual(latest.Dice));
        // The row is rebuilt only when what it draws changed: most renders (a move, a step) leave the latest roll as it was.
        var redraw = changed || latest is not null && latest.Collapsed(CombatLog.Pip) != _shown!.Collapsed(CombatLog.Pip);
        _shown = latest;
        if (latest is null) return;
        if (changed) _tumble = _s.Settings.Animate ? TumbleSeconds : 0;
        if (redraw) Paint();
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
        // The title line: the dice icon and the roll's words; a roll-off's words carry its team labels between the faces.
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", Space.S);
        title.AddChild(HudBits.Icon("dice", Ui.Accent, 18));
        var rollOff = r.DieLabels is { Count: > 0 };
        title.AddChild(new Label { Text = rollOff ? r.Collapsed(CombatLog.Pip) : r.Title, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Add(title);
        if (rollOff) return;
        var faces = new HFlowContainer(); faces.AddThemeConstantOverride("h_separation", Space.XS); Add(faces);
        void Face(Control c) { Ignore(c); faces.AddChild(c); }
        foreach (var d in r.Dice)
        {
            var face = settled ? d.Raw : _faces.Next(1, 7);
            // An unjudged die shows neither pass nor fail, so it is drawn as a plain (unfaded) face; a judged one framed pass or fail.
            var c = DiceFaces.Of(!settled ? new Die(face, face, true) : new Die(d.Raw, d.Modified, !(r.Judged && d.Judged) || d.Success), 48, settled && r.Judged && d.Judged);
            Face(c);
            if (!settled) _tumbling.Add(c);
        }
        // An unjudged roll has no pass or fail, so no result label; a roll-off whose dice no team labels may still name the winner.
        if (settled && r.Judged) Face(new Label { Text = r.Result, ThemeTypeVariation = r.Successes > 0 ? Variation.Success : Variation.Failure, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        else if (settled && r.Outcome.Length > 0) Face(new Label { Text = r.Outcome, SizeFlagsVertical = SizeFlags.ShrinkCenter });
    }

    void Add(Control c) { Ignore(c); _row.AddChild(c); }

    /// <summary>The click belongs to the button: nothing in the row takes the mouse.</summary>
    static void Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in c.GetChildren()) if (child is Control k) Ignore(k);
    }
}
