using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>The play log (client UX spec → Combat log): a filter over CombatLog's blocks, rolls and lines. Renders only; every word is CombatLog's.</summary>
public partial class CombatLogPanel : VBoxContainer
{
    static readonly string[] FilterLabels = ["All", "Rolls", "My units", "This unit"];   // LogFilter's order
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly OptionButton _filter = new();
    readonly ScrollContainer _scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly HashSet<string> _open = [];
    Guid? _reveal, _focus; Control? _revealed; bool _toEnd, _shown, _stale;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _onTheme;

    /// <summary>The unit on the card, which the This unit filter keeps; set by the screen when the card changes.</summary>
    public Guid? FocusUnit
    {
        get => _focus;
        set { if (_focus == value) return; _focus = value; if (_s is not null && _s.Settings.LogFilter == LogFilter.Unit) Render(false); }
    }

    public void Bind(Session s, TableView view, Action<string> report)
    {
        _s = s; _view = view; _report = report;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        for (var i = 0; i < FilterLabels.Length; i++) _filter.AddItem(FilterLabels[i], i);
        _filter.Selected = (int)s.Settings.LogFilter;
        _filter.ItemSelected += i => { _s.UpdateSettings(x => x with { LogFilter = (LogFilter)(int)i }); Render(false); };
        AddChild(_filter); AddChild(_scroll); _scroll.AddChild(_rows);
        _onEvents = _ => Render(true); _onBoard = () => Render(true);
        s.EventsApplied += _onEvents; s.BoardChanged += _onBoard;
        _onTheme = () => Render(false); Ui.Changed += _onTheme;   // the rows' dice faces are the theme's
        Render(true);
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
    }

    /// <summary>Whether the drawer shows the panel. Closed, a render only marks the rows stale; opening renders them.</summary>
    public bool Shown
    {
        get => _shown;
        set { _shown = value; if (value && _stale) Render(true); }
    }

    /// <summary>Expand the block and the roll holding this RollRecord and scroll to it.</summary>
    public void Reveal(Guid rollId) { _reveal = rollId; Render(false); }

    void Render(bool toEnd)
    {
        if (!_shown) { _stale = true; return; }
        _stale = false;
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        _revealed = null; _toEnd = toEnd;
        if (_s.Projection is not { } p || _s.Game is not { } game) return;
        var n = new Names(game, p);
        foreach (var e in CombatLog.Filter(CombatLog.Build(_s.Log, n), _s.Settings.LogFilter, u => n.TeamOfUnit(u) is { } t && t == _s.MyTeamId, _focus))
            Add(_rows, e);
        _reveal = null;
        CallDeferred(nameof(Scroll));
    }

    static string Key(LogEntry e) => e is RollEntry r ? r.Record.Id.ToString() : e.Seq.ToString();
    bool Holds(LogEntry e) => _reveal is { } id && (e is RollEntry r && r.Record.Id == id || e is ActivationBlock b && b.Entries.Any(Holds));

    void Add(VBoxContainer into, LogEntry e)
    {
        if (Holds(e)) _open.Add(Key(e));
        var open = _open.Contains(Key(e));
        var text = e switch
        {
            ActivationBlock b => $"{(open ? "▾" : "▸")} {b.Header}{(open || b.Chain.Length == 0 ? "" : $"  ·  {b.Chain}")}",
            RollEntry r => $"{(open ? "▾" : "▸")} {r.Collapsed(CombatLog.Pip)}",
            LineEntry l => $"#{l.Seq}  {l.Text}",
            _ => "",
        };
        var row = new HBoxContainer(); into.AddChild(row);
        var button = new Button { Text = e.Undone ? Strike(text) : text, Flat = true, ThemeTypeVariation = Variation.FlatButton, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        if (e.Undone || _s.PendingUndoTo is { } to && e.Seq > to) button.Modulate = Ui.Faded;
        button.Pressed += () => { Highlight(e); if (e is LineEntry) return; if (!_open.Remove(Key(e))) _open.Add(Key(e)); Render(false); };
        row.AddChild(button);
        if (e is RollEntry shown && shown.Record.Id == _reveal) _revealed = button;
        if (!e.Undone && e.Event.Payload is not Undone) AddButton(row, "Undo to here", () => Guarded.Run(() => _s.UndoToAsync(e.Seq), _report));
        if (!open) return;
        var inner = new VBoxContainer(); var indent = new MarginContainer(); indent.AddThemeConstantOverride("margin_left", 16);
        indent.AddChild(inner); into.AddChild(indent);
        if (e is ActivationBlock block) foreach (var x in block.Entries) Add(inner, x);
        if (e is RollEntry roll) Details(inner, roll);
    }

    void Details(VBoxContainer into, RollEntry r)
    {
        foreach (var line in r.Basis.Concat(r.Modifiers)) into.AddChild(new Label { Text = line });
        foreach (var d in r.Dice)
        {
            // An unjudged die shows neither pass nor fail: a plain face and an unstyled label.
            var line = new HBoxContainer(); into.AddChild(line);
            line.AddChild(DiceFaces.Of(new Die(d.Raw, d.Modified, !d.Judged || d.Success)));
            line.AddChild(new Label { Text = d.Text, ThemeTypeVariation = !d.Judged ? "" : d.Success ? Variation.Success : Variation.Failure });
        }
        foreach (var a in r.Amendments) into.AddChild(new Label { Text = a.Undone ? Strike(a.Text) : a.Text, ThemeTypeVariation = a.Undone ? Variation.Dim : "" });
        var chips = new HFlowContainer(); into.AddChild(chips);   // the rules that acted in the step, with their catalog text
        foreach (var name in r.Applied) chips.AddChild(new Button { Text = name, Flat = true, ThemeTypeVariation = Variation.FlatButton, TooltipText = _s.Rules.TextOf(name) ?? name });
        if (r.Undone) return;
        var actions = new HBoxContainer(); into.AddChild(actions);
        AddButton(actions, "Reroll all", () => Send(new RerollDice(r.FrameId, r.Record.Id, [])));
        AddButton(actions, "Set die 1…", () => PieceMenu.AskInteger(this, "Raw value for die 1", v => Send(new SetDice(r.FrameId, r.Record.Id, [new DieValue(0, v)]))));
        AddButton(actions, "+1", () => Send(new AddModifier(r.FrameId, r.Record.Roll, 1)));
    }

    static void AddButton(Container row, string text, Action then) { var b = new Button { Text = text, Flat = true, ThemeTypeVariation = Variation.FlatButton }; b.Pressed += then; row.AddChild(b); }

    void Highlight(LogEntry e)
    {
        if (!IsInstanceValid(_view)) return;
        foreach (var piece in _view.Pieces.OfType<PieceNode>()) piece.SetHighlight(null);
        foreach (var id in LogText.Touched(e.Event)) _view.PieceAt(id)?.SetHighlight(Ui.Accent);
    }

    void Send(Command command) => Guarded.Command(_s, command, _report);
    /// <summary>A Button has no strike style: U+0336 after each character.</summary>
    static string Strike(string s) => string.Concat(s.Select(c => $"{c}̶"));

    void Scroll()
    {
        if (!IsInstanceValid(_scroll)) return;
        if (_revealed is not null && IsInstanceValid(_revealed)) _scroll.EnsureControlVisible(_revealed);
        else if (_toEnd) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }
}
