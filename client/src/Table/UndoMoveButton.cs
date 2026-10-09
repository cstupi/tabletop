using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>"Undo move" beside my last hand move until anything else lands in the log; Ctrl+Z presses it. Lives in a CanvasLayer of the table's viewport, so the camera's screen positions are its own.</summary>
public partial class UndoMoveButton : Button
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly HandMoveUndo _undo = new();
    IReadOnlyList<Guid>? _sent;
    Action<IReadOnlyList<Guid>>? _onMoved; Action<Intent>? _onIntent; Action<IReadOnlyList<GameEvent>>? _onEvents;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        Text = "Undo move"; Visible = false; FocusMode = FocusModeEnum.None;
        Pressed += Undo;
        _onMoved = OnHandMoved;
        _onIntent = intent => { if (intent == Intent.UndoMove) Undo(); };
        _onEvents = OnEvents;
        input.HandMoved += _onMoved; input.IntentPressed += _onIntent; session.EventsApplied += _onEvents;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents;
        if (IsInstanceValid(_input)) { _input.HandMoved -= _onMoved; _input.IntentPressed -= _onIntent; }
    }

    // HandMoved is raised after the send's await, so it can land before or after its ModelsMoved event: whichever comes second pairs them.
    (long Seq, IReadOnlyList<Guid> Ids)? _lastOwn;   // my newest ModelsMoved, while it is still the newest event

    static bool Covers(IReadOnlyList<Guid> sent, IReadOnlyList<Guid> moved) => sent.Count > 0 && sent.All(moved.Contains);

    void OnHandMoved(IReadOnlyList<Guid> models)
    {
        if (_lastOwn is { } last && Covers(models, last.Ids)) { _undo.OnOwnMove(last.Seq, models); _lastOwn = null; _sent = null; }
        else _sent = models;
    }

    void OnEvents(IReadOnlyList<GameEvent> events)
    {
        foreach (var e in events)
        {
            _lastOwn = null;
            if (e.Actor == _s.ParticipantId && e.Payload is ModelsMoved moved)
            {
                var ids = moved.Moves.Select(r => r.ModelId).ToList();
                if (_sent is { } sent && Covers(sent, ids)) { _undo.OnOwnMove(e.Seq, sent); _sent = null; continue; }
                _lastOwn = (e.Seq, ids);
            }
            _undo.OnEvent(e.Seq);
        }
    }

    public override void _Process(double delta)
    {
        if (_undo.UndoTo is null || _undo.Models.Count == 0 || _view.PieceAt(_undo.Models[0]) is not { } node || _view.Camera.IsPositionBehind(node.GlobalPosition))
        { Visible = false; return; }
        Position = _view.Camera.UnprojectPosition(node.GlobalPosition) + new Vector2(28, -14);
        Visible = true;
    }

    void Undo()
    {
        if (_undo.UndoTo is not { } to) return;
        Guarded.Run(() => _s.UndoToAsync(to), message => GD.PushWarning($"Undo failed: {message}"));
    }
}
