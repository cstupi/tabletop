using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

public enum TableTool { Select, Measure }

/// <summary>Mouse and keys → presence, commands and edits. Geometry is the library's; Godot only casts the pick ray.</summary>
public partial class TableInput : Node
{
    TableView _view = null!; Session _session = null!; RulerNode _ruler = null!;
    readonly Godot.Timer _presenceTimer = new() { WaitTime = 0.05, Autostart = true };   // 20 Hz
    bool _presenceDirty; Cursor? _cursor; Measurement? _measurement; Point3? _measureFrom;
    Drag? _drag; readonly PendingDrops _drops = new(); Guid? _hovered;
    IReadOnlySet<Guid> _eligible = new HashSet<Guid>();
    public TableTool Tool { get; set; } = TableTool.Select;
    public Selection? Selected { get; private set; }
    /// <summary>Models a declared move or a deployment placement is positioning: their drag streams proposed ghosts, and the release sends nothing (the decision box commits).</summary>
    public HashSet<Guid> Proposing { get; } = new();
    public double? Allowance { get; set; }
    /// <summary>The least each of the move step's models must move (MoveRegion.MinDistance, an Aircraft's 30"), each held to the table from where it
    /// stands (MoveRuler.Minimum): shorter shows red. Null when the Ask sets no minimum.</summary>
    public IReadOnlyDictionary<Guid, double>? Minimum { get; set; }
    /// <summary>What a click picks while a decision wants a unit or a model: unit ids, model ids, or both. Empty when nothing is being picked.</summary>
    public IReadOnlySet<Guid> Eligible { get => _eligible; set { _eligible = value; EligibleChanged?.Invoke(); } }
    public bool SightHeld { get; private set; }
    /// <summary>Objective markers locked from play on: a drag on a locked one does not start. Local to this client.</summary>
    public MarkerLocks Locks { get; } = new();
    /// <summary>The marker under the cursor, or null.</summary>
    public Guid? HoveredMarker { get; private set; }
    /// <summary>A drag is armed: past the threshold, models in hand.</summary>
    public bool Holding => _drag is { Armed: true };
    public event Action<Guid, Point3>? ModelDragged;
    public event Action<Guid, Vector2>? PieceRightClicked;
    public event Action<Vector3>? TableRightClicked;
    public event Action? SelectionChanged;
    public event Action<Guid>? PieceLeftClicked;
    public event Action<Guid?>? HoveredUnitChanged;
    public event Action<Guid?>? HoveredMarkerChanged;
    public event Action<Guid>? UnitClicked;
    public event Action<Guid>? TargetClicked;
    public event Action<Guid, bool>? ModelClicked;
    public event Action<IReadOnlyList<Guid>>? HandMoved;
    public event Action<Intent>? IntentPressed;
    public event Action? EligibleChanged, SightHeldChanged;

    /// <summary>A drag in hand. Armed once the cursor has travelled <see cref="DragThreshold"/> pixels. Rows above zero is a formation about the cursor, turned by Turn degrees from the camera's heading.</summary>
    sealed class Drag(Guid pieceId, IReadOnlyList<Guid> modelIds, Point3 start, Vector2 screen, Dictionary<Guid, Point3> starts, Yaw yaw)
    {
        public Guid PieceId { get; } = pieceId;
        public IReadOnlyList<Guid> ModelIds { get; } = modelIds;
        public Point3 Start { get; } = start;
        public Vector2 Screen { get; } = screen;
        public Dictionary<Guid, Point3> Starts { get; } = starts;
        public Dictionary<Guid, Point3> Current { get; } = new(starts);
        public Yaw Yaw { get; set; } = yaw;
        public bool Armed { get; set; }
        public int Rows { get; set; }
        public double Turn { get; set; }
        public Point3 Cursor { get; set; } = start;
    }
    const float DragThreshold = 6;

    /// <summary>The volumes a dragged piece settles on: every world volume except the ones the dragged piece itself owns,
    /// so dragging a terrain placement does not make it climb its own hill. Volume ids and piece ids are different ids,
    /// so the exclusion is built from the placement, not by subtracting the drag's piece id.</summary>
    List<WorldVolume> _settleAgainst = [];

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        view.AddChild(_ruler = new RulerNode()); AddChild(_presenceTimer);
        view.IsHeld = id => _drops.Holds(id) || _drag?.Current.ContainsKey(id) == true || Proposing.Contains(id);   // a drop awaiting its event, the pieces in hand, a proposed placement
        session.RefusedReceived += OnRefused;
        _presenceTimer.Timeout += OnPresenceTick;
    }

    void OnPresenceTick() { if (_presenceDirty) { _presenceDirty = false; Observe(_session.SendPresenceAsync(Snapshot())); } }

    public override void _ExitTree() { _presenceTimer.Timeout -= OnPresenceTick; if (_session is not null) _session.RefusedReceived -= OnRefused; }

    /// <summary>A refused drop: the nodes go back to the projection's truth.</summary>
    void OnRefused(Refused r) { if (_drops.Refuse(r.MessageId).Count > 0 && IsInstanceValid(_view)) _view.Redraw(); }

    /// <summary>The Animator's question of each relocation: true when this client dropped the model there itself, so the node is already in place and the hold is over.</summary>
    public bool ConsumeOwn(ModelRelocation moved) => _drops.Consume(moved);

    /// <summary>A socket send that fails is logged, never thrown into the engine and never left unobserved.</summary>
    static async void Observe(Task task)
    {
        try { await task; }
        catch (Exception ex) { GD.PushWarning($"Send failed: {ex.Message}"); }
    }

    Presence Snapshot() => new(_session.ParticipantId ?? Guid.Empty, _cursor, Selected,
        _drag is not { Armed: true } ? [] : _drag.Current.Select(kv => new PresencePlacement(kv.Key, kv.Value, _drag.Yaw, Proposing.Contains(kv.Key) ? PlacementKind.Proposed : PlacementKind.Held)).ToList(), _measurement);

    (Point3 Point, Node3D? Piece) Pick(Vector2 screen)
    {
        var from = _view.Camera.ProjectRayOrigin(screen); var dir = _view.Camera.ProjectRayNormal(screen);
        Node3D? best = null; var bestIsPiece = false; var bestDistance = float.MaxValue; Vector3? hit = null;
        foreach (var piece in _view.Pieces)
        {
            if (piece.IsQueuedForDeletion()) continue;
            var bounds = piece switch { PieceNode p => p.Bounds, TerrainNode t => t.Bounds, MarkerNode m => m.Bounds, _ => new Aabb() };
            if (RayHit(bounds, from, dir) is not { } h) continue;
            // A piece beats terrain and markers: a terrain box encloses its whole area volume, so a model standing inside it is hit after the box's top face.
            var isPiece = piece is PieceNode; var d = from.DistanceTo(h);
            if (best is null || (isPiece && !bestIsPiece) || (isPiece == bestIsPiece && d < bestDistance)) { best = piece; bestIsPiece = isPiece; bestDistance = d; hit = h; }
        }
        var ground = new Plane(Vector3.Up, 0).IntersectsRay(from, dir);
        return (WorldFrame.FromGodot(hit ?? ground ?? Vector3.Zero), best);
    }

    /// <summary>The camera ray against the horizontal plane at wire height z (Godot y): where a drag follows the cursor, whatever stands in the way. Null when the ray never reaches it.</summary>
    Point3? OnPlane(Vector2 screen, double z)
    {
        var hit = new Plane(Vector3.Up, (float)z).IntersectsRay(_view.Camera.ProjectRayOrigin(screen), _view.Camera.ProjectRayNormal(screen));
        return hit is { } h ? WorldFrame.FromGodot(h) : null;
    }

    /// <summary>Ray against an axis-aligned box (slab method): the entry point, or null. Godot's Aabb has no ray test in C#.</summary>
    static Vector3? RayHit(Aabb box, Vector3 from, Vector3 dir)
    {
        float near = 0, far = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = from[axis]; var d = dir[axis]; var lo = box.Position[axis]; var hi = lo + box.Size[axis];
            if (Mathf.Abs(d) < 1e-9f) { if (origin < lo || origin > hi) return null; continue; }
            var t1 = (lo - origin) / d; var t2 = (hi - origin) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1); far = Mathf.Min(far, t2);
            if (near > far) return null;
        }
        return from + dir * near;
    }

    static Guid IdOf(Node3D piece) => piece switch { PieceNode p => p.ModelId, TerrainNode t => t.PlacementId, MarkerNode m => m.MarkerId, _ => Guid.Empty };

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m: OnMotion(m.Position); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b: OnLeftDown(b.Position, b.ShiftPressed); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }: Observe(OnLeftUp()); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } b when !Input.IsMouseButtonPressed(MouseButton.Middle):
                var (point, piece) = Pick(b.Position);
                if (piece is PieceNode marked && Eligible.Contains(marked.ModelId)) { ModelClicked?.Invoke(marked.ModelId, true); break; }   // a casualty pick takes a wound back
                if (piece is not null && !b.AltPressed) PieceRightClicked?.Invoke(IdOf(piece), b.Position); else if (b.AltPressed) TableRightClicked?.Invoke(WorldFrame.ToGodot(point));
                break;
            case InputEventKey k: OnKey(k); break;
        }
    }

    void OnMotion(Vector2 screen)
    {
        var (point, piece) = Pick(screen);
        Hover(_drag is { Armed: true } ? null : piece);
        if (_drag is not null) { piece = null; point = OnPlane(screen, _drag.Start.Z) ?? point; }   // a drag follows the plane it started on: pieces and terrain never take the point
        _cursor = new Cursor(point, piece is null ? null : IdOf(piece)); _presenceDirty = true;
        if (_drag is null) return;
        if (!_drag.Armed && screen.DistanceTo(_drag.Screen) < DragThreshold) return;
        _drag.Armed = true; _drag.Cursor = point;
        var dx = point.X - _drag.Start.X; var dy = point.Y - _drag.Start.Y;
        Place(_drag.Rows > 0 ? Arranged(point) : _drag.Starts.ToDictionary(kv => kv.Key, kv => new Point3(kv.Value.X + dx, kv.Value.Y + dy, kv.Value.Z)));
    }

    /// <summary>The unit under the cursor, raised on change; an eligible piece takes the pointing hand.</summary>
    void Hover(Node3D? piece)
    {
        var over = piece as PieceNode;
        Input.SetDefaultCursorShape(over is not null && (Eligible.Contains(over.ModelId) || Eligible.Contains(over.UnitId)) ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        var marker = (piece as MarkerNode)?.MarkerId;
        if (marker != HoveredMarker) { HoveredMarker = marker; HoveredMarkerChanged?.Invoke(marker); }
        if (over?.UnitId == _hovered) return;
        _hovered = over?.UnitId; HoveredUnitChanged?.Invoke(_hovered);
    }

    void MoveNode(Guid id, Point3 to)
    {
        Node3D? node = _view.PieceAt(id) ?? (Node3D?)_view.TerrainAt(id) ?? _view.MarkerAt(id);
        if (node is not null) node.Position = WorldFrame.ToGodot(to);
    }

    /// <summary>The menu's deferred inputs: a PieceId item wants the next piece clicked, a TablePoint item the next point.</summary>
    Action<Guid>? _pickPiece; Action<Point3>? _pickPoint;
    public void PickNextPiece(Action<Guid> then) => _pickPiece = then;
    public void PickNextPoint(Action<Point3> then) => _pickPoint = then;
    /// <summary>
    /// Deployment placement's own point pick, separate from the menu's one-shot slot so neither can take the other's click: it
    /// stands until its owner clears it. A menu pick armed meanwhile takes the next click first, and the placement the one after.
    /// </summary>
    public Action<Point3>? PlacementPoint { get; set; }

    /// <summary>Abandon the drag in progress and send nothing. The decision box calls it when the click that started the drag turned out to be an answer — clicking an enterable container embarks the unit rather than shoving the transport about.</summary>
    public void CancelDrag() { _drag = null; _ruler.Hide(); _presenceDirty = true; _view.Redraw(); }

    void OnLeftDown(Vector2 screen, bool shift)
    {
        var (point, piece) = Pick(screen);
        if (_pickPoint is { } wantPoint) { _pickPoint = null; wantPoint(OnPlane(screen, 0) ?? point); return; }   // a point pick is the table surface, never a box top
        if (_pickPiece is { } wantPiece && piece is not null) { _pickPiece = null; wantPiece(IdOf(piece)); return; }
        if (PlacementPoint is { } place) { place(OnPlane(screen, 0) ?? point); return; }
        if (Tool == TableTool.Measure) { Measure(point, piece); return; }
        if (piece is null) { ClearSelection(); return; }
        if (piece is PieceNode target && (Eligible.Contains(target.ModelId) || Eligible.Contains(target.UnitId)))
        {
            // A pick answers: it neither selects nor drags. A model-id set (casualties, a Takedown) names the model; a unit-id set the unit.
            if (Eligible.Contains(target.ModelId)) ModelClicked?.Invoke(target.ModelId, false);
            TargetClicked?.Invoke(Eligible.Contains(target.ModelId) ? target.ModelId : target.UnitId);
            return;
        }
        var id = IdOf(piece);
        var projection = _session.Projection;
        IReadOnlyList<Guid> models;
        if (piece is PieceNode pn && projection is not null)
        {
            // The unit's models the view is drawing: the ones OnTable, or — during a disembark or a placement — its ghosts.
            var unitModels = projection.Models.Values.Where(m => m.UnitId == pn.UnitId && _view.PieceAt(m.Id) is not null).Select(m => m.Id).ToList();
            Selected = shift ? new Selection(pn.UnitId, [pn.ModelId]) : new Selection(pn.UnitId, []);
            models = shift ? [pn.ModelId] : unitModels;
            UnitClicked?.Invoke(pn.UnitId);
        }
        else { Selected = new Selection(id, []); models = [id]; }
        SelectionChanged?.Invoke(); _presenceDirty = true;
        if (MayDrag(piece, models))
        {
            var starts = models.ToDictionary(m => m, m => Position(m));
            var own = (_session.Projection?.Board.Table ?? _session.Draft)?.Terrain.FirstOrDefault(t => t.Id == id)?.Volumes.Select(v => v.Id).ToHashSet() ?? [];
            _settleAgainst = _view.Volumes.Where(v => !own.Contains(v.Id)).ToList();
            _drag = new Drag(id, models, point, screen, starts, CurrentYaw(id));
        }
        // Last, with the drag already made, so a listener that treats the click as an answer can CancelDrag() it: the piece's
        // own id, which for a model is the model, not the unit Selection widens it to (MoveContainment.Enterable is model ids).
        PieceLeftClicked?.Invoke(id);
    }

    /// <summary>
    /// Under strict a hand move is refused (spec → Moves), so a model drag starts only when every model is Proposing: a move
    /// step, or a deployment placement's ghosts. A locked objective marker never drags, under every enforcement. Terrain and
    /// other markers are not hand moves.
    /// </summary>
    bool MayDrag(Node3D piece, IReadOnlyList<Guid> models) => piece switch
    {
        PieceNode => models.All(Proposing.Contains) || _session.Game?.Enforcement != Enforcement.Strict,
        MarkerNode m => Locks.MayDrag(_session.Game?.Status, _session.Projection, m.MarkerId),
        _ => true,
    };

    /// <summary>Select a unit, or one of its models, as a click would: Tab and the stat sheet's models strip.</summary>
    public void Select(Guid unitId, Guid? modelId = null)
    {
        Selected = new Selection(unitId, modelId is { } m ? [m] : []); _presenceDirty = true;
        SelectionChanged?.Invoke(); UnitClicked?.Invoke(unitId);
    }

    void ClearSelection() { Selected = null; _presenceDirty = true; SelectionChanged?.Invoke(); }

    Point3 Position(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Position;
        if (_view.PieceAt(id) is { } ghost) return WorldFrame.FromGodot(ghost.Position);   // a ghost: where its node stands
        if (Container(id) is { } inside) return inside.Placement.Position;                   // a disembark ghost starts on the container
        if (t?.Terrain.FirstOrDefault(x => x.Id == id) is { } terrain) return terrain.Position;
        return t?.Markers.Concat(p?.Markers.Values ?? []).FirstOrDefault(m => m.Id == id)?.Position ?? new Point3(0, 0, 0);
    }

    Yaw CurrentYaw(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Yaw;
        if (_view.PieceAt(id) is { } ghost) return WorldFrame.YawFrom(ghost.Rotation.Y);
        if (Container(id) is { } inside) return inside.Placement.Yaw;                        // and facing the way it did
        return t?.Terrain.FirstOrDefault(x => x.Id == id)?.Yaw ?? t?.Markers.FirstOrDefault(m => m.Id == id)?.Yaw ?? new Yaw(0);
    }

    /// <summary>
    /// Where an occupant is: nowhere of its own (game flow → Inside a container, "an occupant has no table position of its
    /// own … and disembarks relative to it"), so its drag starts on the container's placement. Null unless the model is
    /// Inside something that is itself on the table.
    /// </summary>
    OnTable? Container(Guid id) =>
        _session.Projection is { } p && p.Models.GetValueOrDefault(id)?.Location is Inside i
            ? p.Models.GetValueOrDefault(i.ContainerId)?.Location as OnTable : null;

    async Task OnLeftUp()
    {
        if (Tool == TableTool.Measure)                                   // "two clicks or a drag" (client shell -> Input and presence)
        {
            var (to, hit) = Pick(GetViewport().GetMousePosition());
            if (_measureFrom is { } from && from.Horizontal.DistanceTo(to.Horizontal) > 0.25) Measure(to, hit);
            return;
        }
        if (_drag is null) return;
        var drag = _drag; _drag = null; _ruler.Hide(); _presenceDirty = true;
        var moved = drag.Armed && drag.Current.Any(kv => kv.Value != drag.Starts[kv.Key]);
        if (!moved) return;
        var status = _session.Game?.Status;
        var p = _session.Projection; var draft = _session.Draft;
        // A declared move or a placement: the decision box's Enter sends the Destinations, so nothing goes now and — unlike every other drop —
        // the nodes are left where the player put them. Redrawing here would snap a proposed placement back to the projection,
        // and a disembark ghost has no projected placement to snap to at all.
        if (drag.ModelIds.All(Proposing.Contains)) return;
        var held = Guid.CreateVersion7(); var freeMove = false;
        try
        {
            if (p is not null && drag.ModelIds.All(p.Models.ContainsKey))
            {
                // The nodes stay where they were dropped until the server's event (or a refusal) lands; the Animator skips this move.
                var moves = drag.ModelIds.Select(m => new ModelRelocation(m, new OnTable(new Placement(drag.Starts[m], drag.Yaw)), new OnTable(new Placement(drag.Current[m], drag.Yaw)))).ToList();
                freeMove = true; _drops.Add(held, moves);
                await _session.SendCommandAsync(new MoveModels(moves), held);
                HandMoved?.Invoke(drag.ModelIds);                         // the undo button waits for this move's event
            }
            else if (status == GameStatus.Setup && draft is not null && draft.Terrain.Any(t => t.Id == drag.PieceId))
                await _session.SendEditAsync(new MoveTerrain(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
            else if (status == GameStatus.Setup && draft is not null && draft.Markers.Any(m => m.Id == drag.PieceId))
                await _session.SendEditAsync(new MoveSetupMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
            else if (p is not null && (p.Markers.ContainsKey(drag.PieceId) || p.Board.Table.Markers.Any(m => m.Id == drag.PieceId)))   // a play marker, or a setup one (an unlocked objective)
                await _session.SendCommandAsync(new MoveMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        }
        catch (Exception ex) { GD.PushWarning($"Send failed: {ex.Message}"); _drops.Refuse(held); freeMove = false; }
        if (!freeMove && IsInstanceValid(this) && IsInstanceValid(_view)) _view.Redraw();   // an edit or a marker move still snaps back until its event lands; a free move waits for its event or a refusal
    }

    void Measure(Point3 point, Node3D? piece)
    {
        var anchor = piece is PieceNode pn && _session.Projection?.Models.GetValueOrDefault(pn.ModelId) is { Location: OnTable on } m
            ? new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), on.Placement) : new PlacedModel(new ModelBody(Base.Circle(0), 0), new Placement(point, new Yaw(0)));
        if (_measureFrom is null) { _measureFrom = point; _pendingAnchor = anchor; return; }
        var (a, b) = Distance.ClosestPoints(_pendingAnchor!.Value, anchor);
        _measurement = new Measurement(a, b); _measureFrom = null; _presenceDirty = true;
        _ruler.Show(a, b, MoveRuler.Read(a.Horizontal, b.Horizontal, null), Colors.Yellow);
    }
    PlacedModel? _pendingAnchor;
}
