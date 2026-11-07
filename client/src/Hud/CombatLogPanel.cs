using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>The play log (combat-log spec → The log): a filter over CombatLog's blocks, rolls and lines. A block shows its lines beneath its header
/// until the header is clicked; a roll shows its raw faces bright or faded and expands on click. Detailed (beside the filter, ClientSettings.LogDetailed)
/// draws each roll and named change as its worked lines in monospace (detailed-log spec). Every row's words are selectable text (SelectableText):
/// a drag selects within the row, Ctrl+C or the right-click menu copies, and a click without a drag is the row's; Copy puts the rows as drawn
/// on the clipboard (LogCopy), and an activation's own Copy that activation with every roll open (LogCopy.Activation). Renders only; every word is CombatLog's.</summary>
public partial class CombatLogPanel : VBoxContainer
{
    static readonly string[] FilterLabels = ["All", "Rolls", "My units", "This unit"];   // LogFilter's order
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly OptionButton _filter = new();
    Button _copy = null!;
    readonly CheckBox _detailed = new() { Text = "Detailed", TooltipText = "Each roll as a worked resolution: every modifier, what was ignored, every die" };
    readonly ScrollContainer _scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
    readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly HashSet<string> _open = [], _folded = [];   // rolls opened, blocks folded
    Guid? _reveal, _focus; Control? _revealed; bool _toEnd, _shown, _stale;
    readonly List<LogEntry> _drawn = []; readonly List<int> _rowStarts = [];   // the top-level entries on screen and the index of each one's first row
    (LogFilter, bool, Guid?, long?, bool, RuleTexts)? _mode;                    // what every row was drawn under
    Action<RenderCause>? _onFrame; Action? _onTheme;

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
        _detailed.ButtonPressed = s.Settings.LogDetailed;
        _detailed.Toggled += on => { _s.UpdateSettings(x => x with { LogDetailed = on }); Render(false); };
        // The head: the log's icon and name, the filter, Detailed, and Copy (the whole log as shown) as its icon.
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 10);
        top.AddChild(HudBits.Icon("log", Ui.Accent, 18)); top.AddChild(new Label { Text = "LOG", ThemeTypeVariation = Variation.Display });
        _copy = HudBits.IconButton("copy", "Copy the log as shown", 18); _copy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _filter.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.AddChild(_filter); top.AddChild(_detailed); top.AddChild(_copy);
        _copy.Pressed += () => DisplayServer.ClipboardSet(LogCopy.Text(_drawn, _s.Settings.LogDetailed, IsOpen));
        AddChild(top); AddChild(_scroll); _scroll.AddChild(_rows);
        // The newest entry stays in view: the bar's range changes after the rows are laid out, a frame after a render, so the jump
        // to the end happens then. Scrolling away from the end lets go; scrolling back to it follows again.
        var bar = _scroll.GetVScrollBar();
        bar.Changed += () => { if (_toEnd && _revealed is null) _scroll.ScrollVertical = (int)bar.MaxValue; };
        bar.ValueChanged += v => _toEnd = v >= bar.MaxValue - bar.Page - 1;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) Render(true); };
        s.Frame.Render += _onFrame;
        _onTheme = () => Render(false); Ui.Changed += _onTheme;   // the rows' dice faces are the theme's
        Render(true);
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.Frame.Render -= _onFrame;
    }

    /// <summary>Whether the drawer shows the panel. Closed, a render only marks the rows stale; opening renders them and shows the newest entry.</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            if (!value) return;
            if (_stale) Render(true);
            else { _toEnd = true; CallDeferred(nameof(Scroll)); }
        }
    }

    /// <summary>Expand the block and the roll holding this RollRecord and scroll to it.</summary>
    public void Reveal(Guid rollId) { _reveal = rollId; Render(false); }

    /// <summary>A render for new events (toEnd) keeps the rows of the leading top-level entries that draw as before (CombatLogRows.Kept) and
    /// rebuilds from the first that differs: an append rebuilds the last block and adds the new ones, an undo rebuilds from the first entry it
    /// strikes. A toggle, a reveal, the filter, the focus, the theme, a pending undo, watching or the rule texts rebuild every row.</summary>
    void Render(bool toEnd)
    {
        if (!_shown) { _stale = true; return; }
        _stale = false;
        _revealed = null; if (toEnd) _toEnd = true;
        IReadOnlyList<LogEntry> next = [];
        if (_s.Projection is not null && _s.CombatLog is { } log)
        {
            var n = log.Names;
            next = CombatLog.Filter(log.Entries, _s.Settings.LogFilter, u => n.TeamOfUnit(u) is { } t && t == _s.MyTeamId, _focus);
        }
        var mode = (_s.Settings.LogFilter, _s.Settings.LogDetailed, _focus, _s.PendingUndoTo, _s.IsWatching, _s.Rules);
        var kept = toEnd && _reveal is null && _mode == mode ? CombatLogRows.Kept(_drawn, next) : 0;
        _mode = mode;
        var from = kept < _rowStarts.Count ? _rowStarts[kept] : _rows.GetChildCount();
        foreach (var c in _rows.GetChildren().Skip(from).ToList()) { _rows.RemoveChild(c); c.QueueFree(); }
        _drawn.RemoveRange(kept, _drawn.Count - kept); _rowStarts.RemoveRange(kept, _rowStarts.Count - kept);
        for (var i = kept; i < next.Count; i++) { _rowStarts.Add(_rows.GetChildCount()); _drawn.Add(next[i]); Add(_rows, next[i]); }
        _reveal = null;
        CallDeferred(nameof(Scroll));
    }

    bool IsOpen(LogEntry e) => e is ActivationBlock ? !_folded.Contains(Key(e)) : _open.Contains(Key(e));

    static string Key(LogEntry e) => e is RollEntry r ? r.Record.Id.ToString() : e.Seq.ToString();
    bool Holds(LogEntry e) => _reveal is { } id && (e is RollEntry r && r.Record.Id == id || e is ActivationBlock b && b.Entries.Any(Holds));

    void Add(VBoxContainer into, LogEntry e)
    {
        if (Holds(e)) { _open.Add(Key(e)); _folded.Remove(Key(e)); }
        var open = IsOpen(e);
        var faded = e.Undone || _s.PendingUndoTo is { } to && e.Seq > to;
        var set = e is ActivationBlock ? _folded : _open;   // a block starts open, a roll closed
        void Toggle() { Highlight(e); if (e is LineEntry) return; if (!set.Remove(Key(e))) set.Add(Key(e)); Render(false); }
        Control shown;
        var detailed = _s.Settings.LogDetailed;
        if (detailed && e is RollEntry worked) into.AddChild(shown = MonoRow(worked.Worked, e.Undone, faded, Toggle));
        else if (detailed && e is LineEntry { Worked: { } lines }) into.AddChild(shown = MonoRow(lines, e.Undone, faded, Toggle));
        else if (e is RollEntry roll && roll.DieLabels is not { Count: > 0 }) into.AddChild(shown = RollRow(roll, open, faded, Toggle));
        else
        {
            var text = e switch
            {
                ActivationBlock b => $"{(open ? "▾" : "▸")} {b.Header}{(open || b.Chain.Length == 0 ? "" : $"  ·  {b.Chain}")}",
                RollEntry r => $"{(open ? "▾" : "▸")} {r.Collapsed(CombatLog.Pip)}",   // a roll-off: the team words go between the faces
                LineEntry l => l.Text,
                _ => "",
            };
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass }; row.AddThemeConstantOverride("separation", 6); into.AddChild(row);
            // A block leads with its actor's team band (UX redesign part 2 → Log and undo).
            if (e is ActivationBlock && _s.CombatLog is { } state && _s.Game is { } game && CombatLogRows.Band(e, state.Names, game) is var band and > 0)
            {
                var bar = HudBits.Band(band, 4); if (faded) bar.Modulate = Ui.Faded; row.AddChild(bar);
            }
            var words = SelectableText(text, e.Undone, "", Toggle);
            if (faded) words.Modulate = Ui.Faded;
            if (e is ActivationBlock { Spell: { } spell } cast) words.TooltipText = SpellText.Tooltip(_s.Rules, spell, cast.SpellCost) ?? "";
            row.AddChild(words);
            // Undo to here and Copy as icons at the row's right, shown while the pointer is over the row.
            var actions = new List<Control>();
            if (e is not RollEntry && CombatLog.UndoHere(e, _s.IsWatching)) actions.Add(AddButton(row, "undo", "Undo to here", () => Guarded.Run(() => _s.UndoToAsync(e.Seq), _report)));
            if (e is ActivationBlock whole) actions.Add(AddButton(row, "copy", "Copy this activation", () => DisplayServer.ClipboardSet(LogCopy.Activation(whole, _s.Settings.LogDetailed))));
            if (actions.Count > 0) { HudBits.OnHover(row, [.. actions]); HudBits.OnHover(words, [.. actions]); }
            shown = words;
        }
        if (e is RollEntry revealed && revealed.Record.Id == _reveal) _revealed = shown;
        if (!open) return;
        var inner = new VBoxContainer(); var indent = new MarginContainer(); indent.AddThemeConstantOverride("margin_left", 16);
        indent.AddChild(inner); into.AddChild(indent);
        if (e is ActivationBlock block) foreach (var x in block.Entries) Add(inner, x);
        if (e is RollEntry opened) Details(inner, opened, detailed);
    }

    /// <summary>Worked lines in the monospace face, one text, wrapping only past the panel's width. A click without a drag is the row's.</summary>
    static Control MonoRow(IReadOnlyList<string> lines, bool undone, bool faded, Action clicked)
    {
        var text = SelectableText(string.Join("\n", lines), undone, Variation.Mono, clicked);
        if (faded) text.Modulate = Ui.Faded;
        return text;
    }

    /// <summary>A row's words as text the mouse selects: a drag selects, Ctrl+C or the right-click menu copies. A left click that selected
    /// nothing is the row's (clicked) and gives the focus back, so the table's keys work again; a selection keeps it until Ctrl+C. Wraps
    /// to the panel's width with a 160 px floor; the wheel passes through to the scroll. Struck with [s], so a copy holds plain words.</summary>
    static RichTextLabel SelectableText(string text, bool undone, string variation, Action? clicked)
    {
        var words = text.Replace("[", "[lb]");
        var label = new RichTextLabel
        {
            BbcodeEnabled = true, Text = undone ? $"[s]{words}[/s]" : words, FitContent = true, ScrollActive = false, SelectionEnabled = true,
            ContextMenuEnabled = true, FocusMode = FocusModeEnum.All, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation,
            CustomMinimumSize = new(160, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (clicked is null) return label;
        label.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } || label.GetSelectedText().Length > 0) return;
            label.ReleaseFocus(); clicked();
        };
        return label;
    }

    /// <summary>The roll as its title, its raw faces (bright on a success, faded on a failure, an unjudged die plain), then its outcome and
    /// live amendments, wrapping to the panel's width; each text keeps a width floor. A press on the faces, or a click without a drag on
    /// the words, is the row's; the wheel passes through to the scroll.</summary>
    static Control RollRow(RollEntry r, bool open, bool faded, Action clicked)
    {
        var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { row.AcceptEvent(); clicked(); } };
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var d in r.Dice) flow.AddChild(DiceFaces.Of(new Die(d.Raw, d.Modified, !d.Judged || d.Success), judged: d.Judged));
        Ignore(flow); row.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(SelectableText($"{(open ? "▾" : "▸")} {r.Title}", r.Undone, "", clicked)); row.AddChild(flow);
        var tail = string.Join(" · ", new[] { r.Outcome }.Concat(r.Amendments.Where(a => !a.Undone).Select(a => a.Line(CombatLog.Pip))).Where(t => t.Length > 0));
        if (tail.Length > 0) flow.AddChild(SelectableText(tail, r.Undone, faded ? Variation.Dim : "", clicked));
        if (faded) row.Modulate = Ui.Faded;
        return row;
    }

    /// <summary>A press on the faces belongs to the row: nothing in them takes the mouse.</summary>
    static void Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in c.GetChildren()) if (child is Control k) Ignore(k);
    }

    /// <summary>A roll's expansion: its details and each die (not in the detailed log, whose lines already hold them), the amendments, the rule
    /// chips, Undo to here.</summary>
    void Details(VBoxContainer into, RollEntry r, bool detailed)
    {
        if (!detailed) foreach (var line in r.Details) into.AddChild(SelectableText(line, false, "", null));
        foreach (var d in detailed ? [] : r.Dice)
        {
            // An unjudged die shows neither pass nor fail: a plain face and an unstyled label.
            var line = new HBoxContainer(); into.AddChild(line);
            line.AddChild(DiceFaces.Of(new Die(d.Raw, d.Modified, !d.Judged || d.Success), judged: d.Judged));
            line.AddChild(SelectableText(d.Text, false, !d.Judged ? "" : d.Success ? Variation.Success : Variation.Failure, null));
        }
        foreach (var a in detailed ? [] : r.Amendments) into.AddChild(SelectableText(a.Text, a.Undone, a.Undone ? Variation.Dim : "", null));
        var chips = new HFlowContainer(); if (r.Applied.Count > 0) into.AddChild(chips); else chips.QueueFree();   // the rules that acted in the step, with their catalog text
        foreach (var name in r.Applied) chips.AddChild(new Button { Text = name, Flat = true, ThemeTypeVariation = Variation.FlatButton, TooltipText = _s.Rules.TextOf(name) ?? name });
        if (!CombatLog.UndoHere(r, _s.IsWatching)) return;
        var actions = new HBoxContainer(); into.AddChild(actions);
        AddButton(actions, "undo", "Undo to here", () => Guarded.Run(() => _s.UndoToAsync(r.Seq), _report));
    }

    /// <summary>An action on a row as its icon, the words in the tooltip.</summary>
    /// <summary>A row's hover action: the icon at 22 px in a hit area of at least 28 px.</summary>
    static Button AddButton(Container row, string icon, string tooltip, Action then)
    {
        var b = HudBits.IconButton(icon, tooltip, 22); b.CustomMinimumSize = new Vector2(28, 28); b.IconAlignment = HorizontalAlignment.Center; b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        b.Pressed += then; row.AddChild(b); return b;
    }

    void Highlight(LogEntry e)
    {
        if (!IsInstanceValid(_view)) return;
        foreach (var piece in _view.Pieces.OfType<PieceNode>()) piece.SetHighlight(null);
        foreach (var id in LogText.Touched(e.Event)) _view.PieceAt(id)?.SetHighlight(Ui.Accent);
    }

    void Scroll()
    {
        if (!IsInstanceValid(_scroll)) return;
        if (_revealed is not null && IsInstanceValid(_revealed)) _scroll.EnsureControlVisible(_revealed);
        else if (_toEnd) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;   // the bar's Changed finishes it once the rows are laid out
    }
}
