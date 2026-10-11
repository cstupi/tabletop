using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class LogPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly ScrollContainer _scroll = new(); readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    // Session outlives this panel: the subscriptions are fields and come off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session s, TableView view, Action<string> report)
    {
        _s = s; _view = view; _report = report;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_scroll); _scroll.AddChild(_rows); _scroll.SizeFlagsVertical = SizeFlags.ExpandFill; _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _onEvents = _ => Render(); _onBoard = Render;
        s.EventsApplied += _onEvents; s.BoardChanged += _onBoard;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard;
    }

    void Send(Command command) => Guarded.Command(_s, command, _report);

    void Render()
    {
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var names = new Names(game, p);
        var live = EventApplier.EffectiveLog(_s.Log).Select(e => e.Seq).ToHashSet();
        foreach (var e in _s.Log)
        {
            var row = new HBoxContainer(); _rows.AddChild(row);
            var isLive = live.Contains(e.Seq);
            var line = $"#{e.Seq}  {LogText.Describe(e, names)}";
            var text = new Button { Text = !isLive && e.Payload is not Undone ? Strike(line) : line, Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            // A Button takes no Label variation: struck and pending rows fade instead.
            if (!isLive && e.Payload is not Undone || _s.PendingUndoTo is { } to && e.Seq > to) text.Modulate = Ui.Faded;
            text.Pressed += () =>
            {
                if (!IsInstanceValid(_view)) return;
                foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null);
                foreach (var id in LogText.Touched(e)) _view.PieceAt(id)?.SetHighlight(Ui.Accent);
            };
            row.AddChild(text);
            if (e.Payload is StepCompleted { Rolls.Count: > 0 } rolled)   // each roll's dice inline (in-game UX spec → Log)
                row.AddChild(new Label { Text = string.Join("   ", rolled.Rolls.Select(r =>
                    $"{r.Roll}: {string.Join(" ", r.Dice.Select(d => d.Success ? $"[{d.Raw}]" : $"{d.Raw}"))}")) });
            if (isLive && e.Payload is not Undone) { var undo = new Button { Text = "Undo to here", Flat = true }; undo.Pressed += () => Guarded.Run(() => _s.UndoToAsync(e.Seq), _report); row.AddChild(undo); }
            if (isLive && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                // A step holds several rolls whenever the ruleset splits one: OPR's roll-to-hit takes one pool per Quality value the
                // attacking models bring, because p.4 tests each attack against "the model's Quality value" and a joined hero brings
                // its own. Reroll and set-die name a RollRecord, so there is one pair per roll, labelled by threshold when it would
                // otherwise be ambiguous. AddModifier names the roll by name, so it stays one button for the step.
                foreach (var roll in step.Rolls)
                {
                    var which = step.Rolls.Count == 1 ? "" : $" {roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}";
                    var reroll = new Button { Text = $"Reroll all{which}", Flat = true }; reroll.Pressed += () => Send(new RerollDice(step.FrameId, roll.Id, [])); row.AddChild(reroll);
                    var set = new Button { Text = $"Set die 1{which}…", Flat = true }; set.Pressed += () => PieceMenu.AskInteger(this, "Raw value for die 1", v => Send(new SetDice(step.FrameId, roll.Id, [new DieValue(0, v)]))); row.AddChild(set);
                }
                var mod = new Button { Text = "+1", Flat = true }; mod.Pressed += () => Send(new AddModifier(step.FrameId, step.Rolls[0].Roll, 1)); row.AddChild(mod);
            }
        }
        CallDeferred(nameof(ScrollToEnd));
    }

    /// <summary>A Button has no strike style: U+0336 after each character.</summary>
    static string Strike(string s) => string.Concat(s.Select(c => $"{c}\u0336"));

    void ScrollToEnd() { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; }
}
