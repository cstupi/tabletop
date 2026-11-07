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
    TableView _view = null!; Session _session = null!; RulerNode _ruler = null!; LandingMarks _marks = null!;
    readonly Godot.Timer _presenceTimer = new() { WaitTime = 0.05, Autostart = true };   // 20 Hz
    bool _presenceDirty; Cursor? _cursor; readonly TapeMeasure _tape = new(); Vector2 _measureDown;
    Drag? _drag; readonly PendingDrops _drops = new(); Guid? _pointed, _named;
    IReadOnlySet<Guid> _eligible = new HashSet<Guid>();
    public TableTool Tool { get; set; } = TableTool.Select;
    bool _measureHeld;
    /// <summary>Clicks measure: the M tool, or Ctrl held (a click there anchors the ruler again).</summary>
    bool Measuring => Tool == TableTool.Measure || _measureHeld;
    public Selection? Selected { get; private set; }
    /// <summary>Models a declared move or a deployment placement is positioning: their drag streams proposed ghosts, and the release sends nothing (the decision box commits).</summary>
    public HashSet<Guid> Proposing { get; } = new();
    /// <summary>The models in breach and why (ViolationMarks draws them): a hand move's are measured here as it drags, a move's or a deployment's
    /// ghosts by the decision box, which also puts a refused commit's reason on them.</summary>
    public Violations Violations { get; } = new();
    /// <summary>The models in hand now, first in a label's order.</summary>
    public IReadOnlyList<Guid> HeldModels => _drag is { Armed: true } d ? d.ModelIds : [];
    public double? Allowance { get; set; }
    /// <summary>The open move Ask's region: its walls and whether it passes models. Null outside one.</summary>
    public MoveRegion? Region { get; set; }
    /// <summary>The open move Ask's per-model regions: each Proposing model's drag is clamped to its reach. Null outside one, and for a disembark.</summary>
    public MoveRegions? Reach { get; set; }
    /// <summary>Where the decision box's move ghosts stand (its proposal), so a drag reads the unit's allowance from every model of the move
    /// (MoveRegions.Allowance). The least each must move is Reach's (MoveRegions.Minimum, in the direction it goes). Null outside a move.</summary>
    public IReadOnlyDictionary<Guid, Point3>? Proposed { get; set; }
    /// <summary>What a click picks while a decision wants a unit or a model: unit ids, model ids, or both. Empty when nothing is being picked.</summary>
    public IReadOnlySet<Guid> Eligible { get => _eligible; set { _eligible = value; EligibleChanged?.Invoke(); } }
    public bool SightHeld { get; private set; }
    /// <summary>Objective markers locked from play on: a drag on a locked one does not start. Local to this client.</summary>
    public MarkerLocks Locks { get; } = new();
    /// <summary>The marker under the cursor, or null.</summary>
    public Guid? HoveredMarker { get; private set; }
    /// <summary>The terrain piece under the cursor, or null.</summary>
    public Guid? HoveredTerrain { get; private set; }
    /// <summary>A drag is armed: past the threshold, models in hand.</summary>
    public bool Holding => _drag is { Armed: true };
    /// <summary>The held pieces where a drag frame (or Esc) put them, all at once: the decision box gates its ghosts once per frame, not once per model.</summary>
    public event Action<IReadOnlyList<(Guid Id, Point3 At)>>? ModelsDragged;
    public event Action<Guid, Vector2>? PieceRightClicked;
    public event Action<Vector3>? TableRightClicked;
    public event Action? SelectionChanged;
    public event Action<Guid>? PieceLeftClicked;
    public event Action<Guid?>? HoveredUnitChanged;
    public event Action<Guid?>? HoveredMarkerChanged;
    public event Action<Guid?>? HoveredTerrainChanged;
    public event Action<Guid>? UnitClicked;
    public event Action<Guid>? TargetClicked;
    public event Action<Guid, bool>? ModelClicked;
    public event Action<IReadOnlyList<Guid>>? HandMoved;
    public event Action<Intent>? IntentPressed;
    public event Action? EligibleChanged, SightHeldChanged;
    /// <summary>A drag has armed: the models now in hand.</summary>
    public event Action<IReadOnlyList<Guid>>? PickedUp;

    /// <summary>
    /// A drag in hand. Armed once the cursor has travelled <see cref="DragThreshold"/> pixels. Grabs are where each piece stood when
    /// it was picked up (a proposed ghost where it is drawn): the cursor offsets and Esc go from them. Origins are where each stands
    /// in the projection: the ruler and the allowance go from them. Each piece keeps its own yaw; Rows above zero is a formation
    /// about the cursor, turned by Turn degrees from the camera's heading, and every model takes its facing.
    /// </summary>
    sealed class Drag(Guid pieceId, IReadOnlyList<Guid> modelIds, Point3 start, Vector2 screen, Dictionary<Guid, Placement> grabs, Dictionary<Guid, Point3> origins)
    {
        public Guid PieceId { get; } = pieceId;
        public IReadOnlyList<Guid> ModelIds { get; } = modelIds;
        public Point3 Start { get; } = start;
        public Vector2 Screen { get; } = screen;
        public Dictionary<Guid, Placement> Grabs { get; } = grabs;
        public Dictionary<Guid, Point3> Origins { get; } = origins;
        public Dictionary<Guid, Point3> Current { get; } = grabs.ToDictionary(kv => kv.Key, kv => kv.Value.Position);
        public Dictionary<Guid, Yaw> Yaws { get; } = grabs.ToDictionary(kv => kv.Key, kv => kv.Value.Yaw);
        public bool Armed { get; set; }
        public int Rows { get; set; }
        public double Turn { get; set; }
        public Point3 Cursor { get; set; } = start;
        /// <summary>Held pieces lift over spec → Held: null is lifted, every pick-up's start; else the height PageDown and PageUp chose, which each held
        /// piece rests nearest as the drag moves.</summary>
        public double? Level { get; set; }
    }
    const float DragThreshold = 6;

    /// <summary>The volumes a dragged piece settles on: every world volume except the ones the dragged piece itself owns,
    /// so dragging a terrain placement does not make it climb its own hill. Volume ids and piece ids are different ids,
    /// so the exclusion is built from the placement, not by subtracting the drag's piece id.</summary>
    List<WorldVolume> _settleAgainst = [];
    /// <summary>The models a held unit collides with: every model on the table but the held ones, taken when the drag starts.</summary>
    IReadOnlyList<PlacedModel> _stationary = [];

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        Violations.Subject = id => session.Projection?.Models.GetValueOrDefault(id)?.Slot.Name;   // the decision box names the model, as the server does
        view.Camera.Holding = () => HoldingTurnable;
        view.AddChild(_ruler = new RulerNode()); view.AddChild(_marks = new LandingMarks()); AddChild(_presenceTimer);
        _tape.Mode = session.Settings.MeasureMode;
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
        _drag is not { Armed: true } ? [] : _drag.Current.Select(kv => new PresencePlacement(kv.Key, kv.Value, _drag.Yaws[kv.Key], Proposing.Contains(kv.Key) ? PlacementKind.Proposed : PlacementKind.Held)).ToList(), _tape.Line);

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

    /// <summary>A release the table's viewport never heard (after a drag it can reach the window and not the table's container)
    /// still drops what is held: the button's state, not the event, ends the drag, so no drag outlives the press. Q/E held turn
    /// what is in hand smoothly (CameraFocus.HeldTurn).</summary>
    public override void _Process(double delta)
    {
        if ((_drag is not null || _box is not null) && !Input.IsMouseButtonPressed(MouseButton.Left)) Observe(OnLeftUp());
        var held = new HashSet<Intent>();
        if (Input.IsKeyPressed(Key.Q)) held.Add(Intent.TurnLeft);
        if (Input.IsKeyPressed(Key.E)) held.Add(Intent.TurnRight);
        if (CameraFocus.HeldTurn(held, HoldingTurnable, TextFocused(), Input.IsKeyPressed(Key.Shift), delta) is var turn and not 0) Turn(turn);
    }

    /// <summary>A move that ends while its models are in hand (a reconnect, an undo, the Ask closing): the drag goes with it, its
    /// models back where they were picked up, and nothing is sent.</summary>
    public void DropProposed()
    {
        if (_drag is not { } drag || !drag.ModelIds.Any(Proposing.Contains)) return;
        foreach (var (id, at) in drag.Grabs) { MoveNode(id, at.Position); Face(id, at.Yaw); }
        CancelDrag();
    }

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
        ShiftTravelled(screen);
        var (point, piece) = Pick(screen);
        Hover(_drag is { Armed: true } ? null : piece);
        if (_drag is not null) { piece = null; point = OnPlane(screen, _drag.Start.Z) ?? point; }   // a drag follows the plane it started on: pieces and terrain never take the point
        _cursor = new Cursor(point, piece is null ? null : IdOf(piece)); _presenceDirty = true;
        if (_tape.Following) { _tape.Follow(AnchorAt(point, piece)); ShowTape(); return; }   // the line follows the cursor until the second click or Ctrl's release
        DrawBox(screen);
        if (_drag is null) return;
        if (!_drag.Armed && screen.DistanceTo(_drag.Screen) < DragThreshold) return;
        if (!_drag.Armed) { _drag.Armed = true; PickedUp?.Invoke(_drag.ModelIds); }
        _drag.Cursor = point;
        var dx = point.X - _drag.Start.X; var dy = point.Y - _drag.Start.Y;
        Place(_drag.Rows > 0 ? Arranged(point) : _drag.Grabs.ToDictionary(kv => kv.Key, kv => kv.Value.Position with { X = kv.Value.Position.X + dx, Y = kv.Value.Position.Y + dy }));
    }

    /// <summary>The pointer left the table for a Hud part: nothing is hovered.</summary>
    public void ClearHover() { if (_drag is null) Hover(null); }

    /// <summary>The unit under the cursor, raised on change; an eligible piece takes the pointing hand.</summary>
    void Hover(Node3D? piece)
    {
        var over = piece as PieceNode;
        Input.SetDefaultCursorShape(over is not null && (Eligible.Contains(over.ModelId) || Eligible.Contains(over.UnitId)) ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        var marker = (piece as MarkerNode)?.MarkerId;
        if (marker != HoveredMarker) { HoveredMarker = marker; HoveredMarkerChanged?.Invoke(marker); }
        var terrain = (piece as TerrainNode)?.PlacementId;
        if (terrain != HoveredTerrain) { HoveredTerrain = terrain; HoveredTerrainChanged?.Invoke(terrain); }
        var was = HoveredUnit; _pointed = over?.UnitId;
        if (HoveredUnit != was) HoveredUnitChanged?.Invoke(HoveredUnit);
    }

    /// <summary>The hovered unit: the one a decision-box button under the pointer names, else the one under the cursor on the table.</summary>
    public Guid? HoveredUnit => _named ?? _pointed;

    /// <summary>The unit a decision-box button under the pointer names (DecisionView.UnitNamed), or null when the pointer leaves it: its
    /// hover label shows as if the unit itself were hovered. ClearHover and the table's own pointer leave it alone.</summary>
    public void HoverUnit(Guid? unit)
    {
        var was = HoveredUnit; _named = unit;
        if (HoveredUnit != was) HoveredUnitChanged?.Invoke(HoveredUnit);
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
    public void CancelDrag() { var held = _drag?.ModelIds ?? []; _drag = null; ForgetHandMarks(held); HideRulers(); _marks.Clear(); _presenceDirty = true; _view.Redraw(); }

    /// <summary>A hand move's marks end with its drag: the server judges the drop (and warns under warn).</summary>
    void ForgetHandMarks(IReadOnlyList<Guid> held) => Violations.Measure(held.Where(id => !Proposing.Contains(id)), new Dictionary<Guid, string>());

    void OnLeftDown(Vector2 screen, bool shift)
    {
        var (point, piece) = Pick(screen);
        if (_pickPoint is { } wantPoint) { _pickPoint = null; wantPoint(OnPlane(screen, 0) ?? point); return; }   // a point pick is the table surface, never a box top
        if (_pickPiece is { } wantPiece && piece is not null) { _pickPiece = null; wantPiece(IdOf(piece)); return; }
        if (PlacementPoint is { } place) { place(OnPlane(screen, 0) ?? point); return; }
        if (Measuring) { _measureDown = screen; Measure(point, piece); return; }
        var answers = piece is PieceNode eligible && (Eligible.Contains(eligible.ModelId) || Eligible.Contains(eligible.UnitId));
        if (shift && !answers) { StartShift(screen, point, piece); return; }   // Shift: a click toggles a model, a drag holds my model alone or boxes (TableInput.Picks)
        if (piece is null) { ClearSelection(); return; }
        if (answers && piece is PieceNode target)
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
            // A press on a picked model holds the picks; on any other model the whole unit, and the picks are cleared.
            _picks = _picks.Pressed(pn.ModelId);
            Selected = _picks.Selection(pn.UnitId);
            models = _picks.Held(pn.ModelId, unitModels);
            UnitClicked?.Invoke(pn.UnitId);
        }
        else { _picks = ModelPicks.None; Selected = new Selection(id, []); models = [id]; }
        SelectionChanged?.Invoke(); _presenceDirty = true;
        BeginDrag(piece, id, models, point, screen);
        // Last, with the drag already made, so a listener that treats the click as an answer can CancelDrag() it: the piece's
        // own id, which for a model is the model, not the unit Selection widens it to (MoveContainment.Enterable is model ids).
        PieceLeftClicked?.Invoke(id);
    }

    /// <summary>The drag of these models (or this terrain piece or marker) from the press at point and screen, when it may drag; armed once the cursor travels.</summary>
    void BeginDrag(Node3D piece, Guid id, IReadOnlyList<Guid> models, Point3 point, Vector2 screen)
    {
        var projection = _session.Projection;
        if (MayDrag(piece, models))
        {
            var grabs = DragBodies.Grabs(models, Proposing,
                m => _view.PieceAt(m) is { } ghost ? new Placement(WorldFrame.FromGodot(ghost.Position), WorldFrame.YawFrom(ghost.Rotation.Y)) : null,
                m => new Placement(Position(m), CurrentYaw(m)));
            var own = (_session.Projection?.Board.Table ?? _session.Draft)?.Terrain.FirstOrDefault(t => t.Id == id)?.Volumes.Select(v => v.Id).ToHashSet() ?? [];
            _settleAgainst = _view.Volumes.Where(v => !own.Contains(v.Id)).ToList();
            // The Ask's other models are ghosts: they collide where they are drawn, a disembark ghost included.
            _stationary = projection is null ? [] : DragBodies.Stationary(projection, models, Proposing.Where(m => !models.Contains(m) && _view.PieceAt(m) is not null)
                .ToDictionary(m => m, m => new Placement(WorldFrame.FromGodot(_view.PieceAt(m)!.Position), WorldFrame.YawFrom(_view.PieceAt(m)!.Rotation.Y))));
            _drag = new Drag(id, models, point, screen, grabs, models.ToDictionary(m => m, Position));
        }
    }

    /// <summary>
    /// Under strict a hand move is refused (spec → Moves), so a model drag starts only when every model is Proposing: a move
    /// step, or a deployment placement's ghosts. A locked objective marker never drags, under every enforcement. Terrain drags
    /// only in setup (TerrainLock). Neither is a hand move. A watcher drags nothing: the server refuses every change from one.
    /// </summary>
    bool MayDrag(Node3D piece, IReadOnlyList<Guid> models) => !_session.IsWatching && piece switch
    {
        PieceNode => models.All(Proposing.Contains) || _session.Game?.Enforcement != Enforcement.Strict,
        MarkerNode m => Locks.MayDrag(_session.Game?.Status, _session.Projection, m.MarkerId),
        TerrainNode => TerrainLock.MayDrag(_session.Game?.Status),
        _ => true,
    };

    /// <summary>Select a unit, or one of its models, as a click would: Tab and the unit card's wound rows. A model so selected is the picks.</summary>
    public void Select(Guid unitId, Guid? modelId = null)
    {
        _picks = modelId is { } m ? new ModelPicks(unitId, [m]) : ModelPicks.None;
        Selected = _picks.Selection(unitId); _presenceDirty = true;
        SelectionChanged?.Invoke(); UnitClicked?.Invoke(unitId);
    }

    void ClearSelection() { _picks = ModelPicks.None; Selected = null; _presenceDirty = true; SelectionChanged?.Invoke(); }

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
        if (_box is not null) { EndBox(GetViewport().GetMousePosition()); return; }
        if (Measuring)                                                   // the M tool: "two clicks or a drag" (client shell -> Input and presence)
        {
            var screen = GetViewport().GetMousePosition(); var (to, hit) = Pick(screen);
            if (!_measureHeld && _tape.Following && screen.DistanceTo(_measureDown) >= DragThreshold) Measure(to, hit);   // a press that travelled is a drag
            return;
        }
        if (_drag is null) return;
        var drag = _drag; _drag = null; HideRulers(); _marks.Clear(); _presenceDirty = true; ForgetHandMarks(drag.ModelIds);
        if (drag.Armed) foreach (var (id, at) in drag.Current) MoveNode(id, at);       // a lifted piece is drawn hovering: it drops where it lands
        var moved = drag.Armed && drag.Current.Any(kv => kv.Value != drag.Grabs[kv.Key].Position || drag.Yaws[kv.Key] != drag.Grabs[kv.Key].Yaw);
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
            switch (HandMoves.Drop(status, p, draft, new HeldDrop(drag.PieceId, drag.ModelIds, drag.Grabs, drag.Current, drag.Yaws)))
            {
                case { Command: MoveModels mm }:
                    // The nodes stay where they were dropped until the server's event (or a refusal) lands; the Animator skips this move.
                    freeMove = true; _drops.Add(held, mm.Moves);
                    await _session.SendCommandAsync(mm, held);
                    HandMoved?.Invoke(drag.ModelIds);                         // the undo button waits for this move's event
                    break;
                case { Edit: { } edit }: await _session.SendEditAsync(edit); break;
                case { Command: { } command }: await _session.SendCommandAsync(command); break;
            }
        }
        catch (Exception ex) { GD.PushWarning($"Send failed: {ex.Message}"); _drops.Refuse(held); freeMove = false; }
        if (!freeMove && IsInstanceValid(this) && IsInstanceValid(_view)) _view.Redraw();   // an edit or a marker move still snaps back until its event lands; a free move waits for its event or a refusal
    }

    /// <summary>A click while measuring. Ctrl held: the ruler starts again here. The M tool: the first click anchors, the second fixes the line.</summary>
    void Measure(Point3 point, Node3D? piece)
    {
        var at = AnchorAt(point, piece);
        if (!_measureHeld && _tape.Following) _tape.Finish(at); else _tape.Start(at);
        ShowTape();
    }

    /// <summary>Ctrl down: the ruler is anchored at the point under the cursor now, and follows it until Ctrl comes up.</summary>
    void StartTapeAtCursor() { var (point, piece) = Pick(GetViewport().GetMousePosition()); _tape.Start(AnchorAt(point, piece)); ShowTape(); }

    /// <summary>A model measures from its base (p.5), a terrain piece from its top under the point, anything else from the point.</summary>
    PlacedModel AnchorAt(Point3 point, Node3D? piece) =>
        piece is PieceNode pn && _session.Projection?.Models.GetValueOrDefault(pn.ModelId) is { Location: OnTable on } m
            ? new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), on.Placement)
            : new PlacedModel(new ModelBody(Base.Circle(0), 0), new Placement(piece is TerrainNode ? TapeMeasure.OnSurface(point, _view.Volumes) : point, new Yaw(0)));

    /// <summary>The ruler as the tape has it, here and in presence.</summary>
    void ShowTape()
    {
        if (_tape.Line is { } m && _tape.Reading is { } reading) _ruler.Show(m.From, m.To, reading, Colors.Yellow); else _ruler.Hide();
        _presenceDirty = true;
    }

    /// <summary>H: the other mode, remembered in the settings; a line already drawn reads again.</summary>
    void ToggleMeasureMode()
    {
        _tape.Mode = _tape.Mode.Toggled();
        _session.UpdateSettings(s => s with { MeasureMode = _tape.Mode });
        ShowTape();
    }
}
