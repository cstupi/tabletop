using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The server's sight answer drawn: one line per model pair (green clear, amber screened, red blocked), the blockers lit, a summary over the target, and under a target pick the models that cannot shoot the hovered or an assigned target dimmed.</summary>
public partial class SightLines : Node3D
{
    static readonly Color ClearColour = new(0.2f, 0.9f, 0.3f), ScreenedColour = new(1f, 0.75f, 0.1f), BlockedColour = new(0.95f, 0.15f, 0.15f);
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() };
    readonly Node3D _marks = new();
    readonly Label3D _summary = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 44, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true, Visible = false };
    Guid? _hovered;
    // Each query's id is made here before the send, so the newest query per target is known at once and any older answer is dropped.
    (Guid Id, Guid Target)? _query; (SightAnswer Answer, Guid Target)? _shown;      // the hovered unit's newest query, and the answer drawn
    readonly Dictionary<Guid, Guid> _assignedQueries = new();                         // assigned target → its newest query
    readonly Dictionary<Guid, SightAnswer> _assignedAnswers = new();
    readonly SightCache _assignedKeys = new();                                        // assigned target → the placements its newest query was sent for
    readonly SightSlot _hoveredKey = new();                                           // the placements the hovered pair's newest query was sent for
    IReadOnlyList<AskOption> _groups = []; IReadOnlyDictionary<string, Guid> _assigned = new Dictionary<string, Guid>();
    Action<Guid?>? _onHover; Action? _onChanged; Action<RenderCause>? _onFrame; Action<SightAnswer>? _onAnswer;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_lines); AddChild(_marks); AddChild(_summary);
        _onHover = id => { _hovered = id; Ask(); }; _onChanged = Ask; _onFrame = c => { if ((c & RenderCause.Events) == 0) return; Ask(); AskAssigned(); }; _onAnswer = OnAnswer;
        input.HoveredUnitChanged += _onHover; input.SightHeldChanged += _onChanged; input.SelectionChanged += _onChanged;
        session.Frame.Render += _onFrame; session.SightAnswered += _onAnswer;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.Frame.Render -= _onFrame; _s.SightAnswered -= _onAnswer;
        if (IsInstanceValid(_input)) { _input.HoveredUnitChanged -= _onHover; _input.SightHeldChanged -= _onChanged; _input.SelectionChanged -= _onChanged; }
    }

    /// <summary>The decision box's target assignment: each assigned target is asked about, so the models that cannot shoot it dim. Empty clears it.</summary>
    public void ShowAssigned(IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned) { _groups = groups; _assigned = assigned; AskAssigned(); Draw(); }

    /// <summary>My open Ask, when it is a target assignment: the shooting unit is its frame's actor.</summary>
    Guid? Shooter(Projection p, GameInfo game) =>
        AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds) is { Mode: AskMode.Assign } mine && mine.Ask.FrameId is { } f
            ? p.Stack.FirstOrDefault(x => x.Id == f)?.Context.ActorUnitId : null;

    void Ask()
    {
        if (_s.Projection is not { } p || _s.Game is not { } game) { _query = null; _shown = null; _hoveredKey.Forget(); Draw(); return; }
        var observer = Shooter(p, game) ?? (_input.SightHeld ? _input.Selected?.PieceId : null);
        if (observer is not { } from || _hovered is not { } to || from == to || !p.Units.ContainsKey(from)) { _query = null; _shown = null; _hoveredKey.Forget(); Draw(); return; }
        var key = SightKey.Of(p, from, to);
        if (_query is not null && !_hoveredKey.NeedsAsk(key)) return;                 // nothing the answer depends on moved: it stands
        var id = Guid.CreateVersion7();
        _query = (id, to);
        _hoveredKey.Asked(key);
        Send(id, from, to, () => { if (_query?.Id == id) _hoveredKey.Forget(); });
    }

    /// <summary>Ask about each assigned target that is new or whose pair's model placements or terrain changed since its last query; the other answers stand.</summary>
    void AskAssigned()
    {
        var targets = _assigned.Values.Distinct().ToList();
        foreach (var gone in _assignedQueries.Keys.Where(t => !targets.Contains(t)).ToList()) { _assignedQueries.Remove(gone); _assignedAnswers.Remove(gone); }
        _assignedKeys.Keep(targets);
        if (targets.Count == 0 || _s.Projection is not { } p || _s.Game is not { } game || Shooter(p, game) is not { } from) return;
        foreach (var target in targets)
        {
            var key = SightKey.Of(p, from, target);
            if (!_assignedKeys.NeedsAsk(target, key)) continue;
            var id = Guid.CreateVersion7();
            _assignedQueries[target] = id;
            _assignedKeys.Asked(target, key);
            Send(id, from, target, () => _assignedKeys.Forget(target));
        }
    }

    async void Send(Guid id, Guid from, Guid to, Action? failed = null)
    {
        try { await _s.QuerySightAsync(id, from, to); }
        catch (Exception ex) { GD.PushWarning($"Sight query failed: {ex.Message}"); failed?.Invoke(); }
    }

    void OnAnswer(SightAnswer answer)
    {
        if (_query is { } q && answer.QueryId == q.Id) { _shown = (answer, q.Target); Draw(); return; }
        foreach (var (target, id) in _assignedQueries)
            if (id == answer.QueryId) { _assignedAnswers[target] = answer; Draw(); return; }
    }

    void Clear()
    {
        _lines.Mesh = null; _summary.Visible = false;
        foreach (var c in _marks.GetChildren()) c.QueueFree();
    }

    void Draw()
    {
        Clear();
        if (_s.Projection is not { } p || _s.Game is not { } game) return;
        var shooting = Shooter(p, game) is not null;
        var hovered = _query is not null ? _shown : null;
        if (shooting)
            foreach (var id in SightSummary.Dimmed(p, hovered?.Answer, _groups, _assigned, _assignedAnswers))
                if (_view.PieceAt(id) is { } node) Mark(node.Bounds, new Color(0, 0, 0, 0.55f));
        if (hovered is not { } shown) return;
        var (answer, target) = shown;
        Vector3? Eye(Guid model) => p.Models.GetValueOrDefault(model) is { Location: OnTable on } m
            ? WorldFrame.ToGodot(on.Placement.Position) + Vector3.Up * (float)(m.Slot.Height / 2) : null;
        var segments = new List<(Vector3, Vector3, Color)>();
        foreach (var line in answer.Lines)
            if (Eye(line.FromModelId) is { } a && Eye(line.ToModelId) is { } b)
                segments.Add((a, b, !line.Clear ? BlockedColour : line.Screened ? ScreenedColour : ClearColour));
        _lines.Mesh = LineMesh.Of(segments);
        foreach (var id in answer.Lines.Where(l => !l.Clear).SelectMany(l => l.BlockedBy).Distinct())
            if (BoundsOf(p, id) is { } box) Mark(box, BlockedColour with { A = 0.35f });

        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == target).ToList();
        if (nodes.Count == 0) return;
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        _summary.Text = SightSummary.Text(answer, new Names(game, p).Unit(target), shooting);
        _summary.Position = new Vector3(centre.X, nodes.Max(n => n.Bounds.End.Y) + 2.0f, centre.Z);   // above the hover label
        _summary.Visible = true;
    }

    /// <summary>A blocker's box: a model's node, a unit's models together (a unit perimeter blocks as one), or the terrain piece that owns the volume.</summary>
    Aabb? BoundsOf(Projection p, Guid id)
    {
        if (_view.PieceAt(id) is { } piece) return piece.Bounds;
        if (p.Units.ContainsKey(id))
        {
            var boxes = p.Models.Values.Where(m => m.UnitId == id).Select(m => _view.PieceAt(m.Id)?.Bounds).OfType<Aabb>().ToList();
            return boxes.Count == 0 ? null : boxes.Skip(1).Aggregate(boxes[0], (all, box) => all.Merge(box));
        }
        var placement = p.Board.Table.Terrain.FirstOrDefault(t => t.Volumes.Any(v => v.Id == id));
        return placement is not null && _view.TerrainAt(placement.Id) is { } terrain ? terrain.Bounds : null;
    }

    void Mark(Aabb box, Color colour) =>
        _marks.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box.Size + Vector3.One * 0.1f }, Position = box.GetCenter(), MaterialOverride = Palette.Translucent(colour) });
}
