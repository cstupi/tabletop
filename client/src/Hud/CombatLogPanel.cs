using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>The play log (combat-log spec → The log): a filter over CombatLog's entries drawn as LogSheet's text, summary or Detailed (beside
/// the filter, ClientSettings.LogDetailed; monospace). The whole log is one selectable text: a drag selects across any number of entries and
/// Ctrl+C or the right-click menu copies them as plain lines; the head's Copy puts the whole log as shown on the clipboard. A click that
/// selects nothing picks the entry under it (lit; its pieces lit on the table) and gives the focus back, and the head's Undo to here undoes
/// to the picked entry. Renders only; every word is LogSheet's.</summary>
public partial class CombatLogPanel : VBoxContainer
{
    static readonly string[] FilterLabels = ["All", "Rolls", "My units", "This unit"];   // LogFilter's order
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly OptionButton _filter = new();
    Button _copy = null!, _undo = null!;
    readonly CheckBox _detailed = new() { Text = "Detailed", TooltipText = "Each roll as tables of its dice, its modifiers and its totals" };
    readonly RichTextLabel _text = new()
    {
        BbcodeEnabled = true, SelectionEnabled = true, ContextMenuEnabled = true, ScrollActive = true, FocusMode = FocusModeEnum.All,
        AutowrapMode = TextServer.AutowrapMode.WordSmart, MetaUnderlined = false, SizeFlagsVertical = SizeFlags.ExpandFill,
        SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(160, 0),
    };
    IReadOnlyList<LogParagraph> _paragraphs = [];
    string _drawn = "", _picked = "";   // the bbcode on screen; the picked entry's key
    Guid? _reveal, _focus; bool _shown, _stale, _follow = true, _setting;
    Action<RenderCause>? _onFrame; Action? _onTheme;

    /// <summary>The unit on the card, which the This unit filter keeps; set by the screen when the card changes.</summary>
    public Guid? FocusUnit
    {
        get => _focus;
        set { if (_focus == value) return; _focus = value; if (_s is not null && _s.Settings.LogFilter == LogFilter.Unit) Render(); }
    }

    public void Bind(Session s, TableView view, Action<string> report)
    {
        _s = s; _view = view; _report = report;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        for (var i = 0; i < FilterLabels.Length; i++) _filter.AddItem(FilterLabels[i], i);
        _filter.Selected = (int)s.Settings.LogFilter;
        _filter.ItemSelected += i => { _s.UpdateSettings(x => x with { LogFilter = (LogFilter)(int)i }); Render(); };
        _detailed.ButtonPressed = s.Settings.LogDetailed;
        _detailed.Toggled += on => { _s.UpdateSettings(x => x with { LogDetailed = on }); Render(); };
        // The head: the log's icon and name, the filter, Detailed, Undo to here (the picked entry) and Copy (the whole log as shown).
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", Space.S);
        top.AddChild(HudBits.Icon("log", Ui.Accent, 18)); top.AddChild(new Label { Text = "LOG", ThemeTypeVariation = Variation.Display });
        _undo = HudBits.IconButton("undo", "Undo to here: pick an entry first", 18); _undo.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _copy = HudBits.IconButton("copy", "Copy the log as shown", 18); _copy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _filter.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.AddChild(_filter); top.AddChild(_detailed); top.AddChild(_undo); top.AddChild(_copy);
        _copy.Pressed += () => DisplayServer.ClipboardSet(LogSheet.Text(_paragraphs));
        _undo.Pressed += () => { if (Picked() is { } e && CombatLog.UndoHere(e, _s.IsWatching)) Guarded.Run(() => _s.UndoToAsync(e.Seq), _report); };
        AddChild(top); AddChild(_text);
        _text.MetaClicked += meta => { if (_text.GetSelectedText().Length == 0 && int.TryParse(meta.AsString(), out var i)) Pick(i); };
        _text.MetaHoverStarted += meta => _text.TooltipText = int.TryParse(meta.AsString(), out var i) && i < _paragraphs.Count
            && _paragraphs[i].Entry is ActivationBlock { Spell: { } spell } cast ? SpellText.Tooltip(_s.Rules, spell, cast.SpellCost) ?? "" : "";
        _text.MetaHoverEnded += _ => _text.TooltipText = "";
        // A left click that selected nothing gives the focus back, so the table's keys work again; a selection keeps it until Ctrl+C.
        _text.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && _text.GetSelectedText().Length == 0) _text.ReleaseFocus(); };
        // The newest entry stays in view: scrolling away from the end lets go; scrolling back to it follows again.
        var bar = _text.GetVScrollBar();
        bar.Changed += () => { if (_follow) bar.Value = bar.MaxValue; };
        bar.ValueChanged += v => { if (!_setting) _follow = v >= bar.MaxValue - bar.Page - 1; };   // not the reset a new text makes
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) Render(); };
        s.Frame.Render += _onFrame;
        _onTheme = () => { _drawn = ""; Render(); }; Ui.Changed += _onTheme;   // the colours are the theme's
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.Frame.Render -= _onFrame;
    }

    /// <summary>Whether the drawer shows the panel. Closed, a render only marks the text stale; opening renders it and shows the newest entry.</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            if (!value) return;
            _follow = true;
            if (_stale) Render(); else CallDeferred(nameof(ToEnd));
        }
    }

    /// <summary>Pick the entry holding this RollRecord and scroll to it.</summary>
    public void Reveal(Guid rollId) { _reveal = rollId; Render(); }

    static string Key(LogEntry e) => e is RollEntry r ? $"r{r.Record.Id}" : $"{e.GetType().Name}{e.Seq}";

    LogEntry? Picked() => _paragraphs.FirstOrDefault(p => Key(p.Entry) == _picked)?.Entry;

    void Pick(int i)
    {
        if (i >= _paragraphs.Count) return;
        var e = _paragraphs[i].Entry;
        _picked = _picked == Key(e) ? "" : Key(e);
        Highlight(_picked.Length > 0 ? e : null);
        Render();
    }

    /// <summary>LogSheet over the filtered entries, drawn through LogBbcode; set only when it changed. The scroll stays at the end when it
    /// was there, else where it was; a reveal scrolls to the picked roll's paragraph.</summary>
    void Render()
    {
        if (!_shown) { _stale = true; return; }
        _stale = false;
        IReadOnlyList<LogEntry> next = [];
        var state = _s.Projection is not null ? _s.CombatLog : null;
        if (state is not null)
        {
            var n = state.Names;
            next = CombatLog.Filter(state.Entries, _s.Settings.LogFilter, u => n.TeamOfUnit(u) is { } t && t == _s.MyTeamId, _focus);
        }
        var detailed = _s.Settings.LogDetailed;
        _paragraphs = LogSheet.Of(next, detailed);
        int? revealAt = null;
        if (_reveal is { } id && _paragraphs.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.Entries.Any(e => e is RollEntry r && r.Record.Id == id)) is { p: not null } hit)
        {
            _picked = Key(hit.p.Entry); revealAt = hit.i;
        }
        _reveal = null;
        if (Picked() is null) _picked = "";
        _undo.Disabled = Picked() is not { } picked || !CombatLog.UndoHere(picked, _s.IsWatching);
        _undo.TooltipText = _undo.Disabled ? "Undo to here: pick an entry first" : "Undo to here";
        _undo.Visible = !_s.IsWatching;
        _text.ThemeTypeVariation = detailed ? Variation.Mono : "";
        var bbcode = Bbcode(state, out var lineOf);
        if (bbcode == _drawn && revealAt is null) return;
        var bar = _text.GetVScrollBar(); var was = bar.Value;
        _setting = true;
        _drawn = bbcode; _text.Text = bbcode;
        _text.GetContentHeight();   // lays the text out now, so the bar's range is the new text's
        if (revealAt is { } at) { _follow = false; _text.ScrollToParagraph(lineOf[at]); }
        else bar.Value = _follow ? bar.MaxValue : was;
        _setting = false;
    }

    /// <summary>LogBbcode over the paragraphs: an undone one struck and faded, one past a pending undo faded, a block's header in its team's colour, the picked one tinted.</summary>
    string Bbcode(CombatLogState? state, out IReadOnlyList<int> lineOf)
    {
        var faded = $"#{(Ui.Text with { A = 0.45f }).ToHtml()}"; var lit = $"#{(Ui.Accent with { A = 0.22f }).ToHtml()}";
        return LogBbcode.Of(_paragraphs, p =>
        {
            var dim = p.Undone || _s.PendingUndoTo is { } to && p.Entry.Seq > to;
            var band = p.Entry is ActivationBlock && state is not null && _s.Game is { } game ? CombatLogRows.Band(p.Entry, state.Names, game) : 0;
            return new ParagraphLook(p.Undone, dim ? faded : band > 0 ? $"#{Ui.Team(band).ToHtml(false)}" : null, Key(p.Entry) == _picked ? lit : null);
        }, out lineOf);
    }

    void ToEnd() { if (IsInstanceValid(_text)) _text.GetVScrollBar().Value = _text.GetVScrollBar().MaxValue; }

    void Highlight(LogEntry? e)
    {
        if (!IsInstanceValid(_view)) return;
        foreach (var piece in _view.Pieces.OfType<PieceNode>()) piece.SetHighlight(null);
        if (e is not null) foreach (var id in LogText.Touched(e.Event)) _view.PieceAt(id)?.SetHighlight(Ui.Accent);
    }
}
