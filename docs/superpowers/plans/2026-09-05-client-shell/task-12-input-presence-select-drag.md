# Task 12: Input and presence — select, drag, drop, the ruler, measure, ping, others' cursors and ghosts

**Files:**
- Create: `client/src/Table/TableInput.cs`, `client/src/Table/RulerNode.cs`, `client/src/Table/PresenceOverlay.cs`

**Interfaces:**
- Consumes: `TableView` (Task 11), `Session` (`SendPresenceAsync`, `SendPingAsync`, `SendCommandAsync`, `SendEditAsync`, `Others`, `PresenceChanged`, `Pinged`, `Game`, `Projection`, `Draft`, `ParticipantId`), `MoveRuler`, `RulerReadout` (Core); `Presence`, `Cursor`, `Selection`, `PresencePlacement`, `PlacementKind`, `Measurement`, `Ping`, `MoveModels`, `ModelRelocation`, `OnTable`, `Inside`, `MoveTerrain`, `MoveSetupMarker`, `MoveMarker`, `GameStatus` (Protocol); `Settling.SurfaceHeightAt(Point3, …)`, `Distance.ClosestPoints`, `PlacedModel`, `ModelBody`, `Placement` (Geometry).
- Produces: `enum TableTool { Select, Measure }`; `partial class TableInput : Node` — `void Bind(TableView, Session)`, `TableTool Tool`, `Selection? Selected`, `event Action<Guid, Vector2>? PieceRightClicked` (a piece id and the screen position, for the menu), `event Action<Vector3>? TableRightClicked`, `event Action? SelectionChanged`, `event Action<Guid>? PieceLeftClicked` (the clicked piece's own id — a model, not the unit `Selected` widens it to — raised after the drag is made so a listener may `CancelDrag()`), `event Action<Guid, Point3>? ModelDragged`, `HashSet<Guid> Proposing`, `double? Allowance`, `void PickNextPiece(Action<Guid>)` and `void PickNextPoint(Action<Point3>)` (the menu's `PieceId` and `TablePoint` items: the next left click answers them instead of selecting or dragging, then clears itself — one field each, checked at the top of `OnLeftDown`); `void CancelDrag()` (drop the drag in progress and hide the ruler, without sending anything — the Ask panel calls it when a click on an enterable container answers the move instead of moving it); left click selects the unit under the cursor (Shift narrows to the model; during setup a terrain placement or marker); left drag moves the selection: every model of the selection follows by the same displacement, each settles with `Settling.SurfaceHeightAt(new Point3(x, y, currentZ), volumes)`, positions stream as `held` placements at 20 Hz (a `Timer`), the release sends one `MoveModels` with start and stop (or, in setup, one `MoveTerrain`/`MoveSetupMarker`; a token in play, one `MoveMarker`); `M` toggles the measure tool (two clicks on pieces, or one press-and-drag between them, → `Distance.ClosestPoints` → `Measurement`, drawn and relayed); `P` pings at the cursor; the cursor's ray point and hovered piece go in every presence sample. `partial class RulerNode : Node3D` — `void Show(Point3 from, Point3 to, RulerReadout readout, Color)`, `void Hide()`; `partial class PresenceOverlay : Node3D` — draws others' cursors (a small cone at the point, hovered piece highlighted), measurements (a line with the length), `held` placements (the piece node moved there, in the team's colour) and `proposed` ghosts (a translucent copy), rulers for others' held placements from the projection's start position, and pings (a ring that fades over two seconds); `void Bind(TableView, Session)`.
- Picking is ray-versus-AABB over `TableView.Pieces` bounds and the slab plane (`Plane(Vector3.Up, 0).IntersectsRay`); no physics. `Pieces` includes a disembark’s exit ghosts (Task 11), which is the whole of what makes an occupant draggable.
- **Nothing here draws a ruler for a piece with no placement.** The ruler is drawn from `Drag.Starts`, which `Position` fills — and for a model `Inside` a container that start is the *container’s* placement, never `(0,0,0)`, because an occupant *"has no table position of its own … and disembarks relative to it"* (game flow → Inside a container). A piece that is neither on the table nor inside a container that is itself on the table has no node, so no drag can begin on it. The same holds for the other participants: `PresenceOverlay.StartOf` returns null for an occupant, so no ruler is drawn against a placement that does not exist, and their disembark ghosts arrive as `PlacementKind.Proposed`, which draws a standalone translucent cylinder needing no node of its own.

Spec: client shell → Input and presence (every bullet), The table view (Godot physics is not in the scene; geometry runs in `Tabletop.Geometry`), Server wins; presence → The presence value, The cursor is where the ray lands, Drag and ghost are one value, A measurement is two points, A ping is silent; setup phase → The draft is the row (live drag is presence; the drop is the edit); table domain model → The one wire decision (start and stop); scheduler → Moving models by hand is not an answer; geometry plan → Interfaces needed → Client shell plan (`Distance.ClosestPoints` for measurements; `Settling.SurfaceHeightAt(new Point3(x, y, currentZ), volumes)` for a dragged model).

- [ ] **Step 1: Write `RulerNode.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A line from where a piece started to where it is, with the distance in inches and, in a declared move, what is left.</summary>
public partial class RulerNode : Node3D
{
    readonly MeshInstance3D _line = new(); readonly Label3D _label = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f };
    public override void _Ready() { AddChild(_line); AddChild(_label); Hide(); }

    public void Show(Point3 from, Point3 to, RulerReadout readout, Color colour)
    {
        var a = WorldFrame.ToGodot(from) + Vector3.Up * 0.3f; var b = WorldFrame.ToGodot(to) + Vector3.Up * 0.3f;
        var im = new ImmediateMesh(); im.SurfaceBegin(Mesh.PrimitiveType.Lines); im.SurfaceAddVertex(a); im.SurfaceAddVertex(b); im.SurfaceEnd();
        _line.Mesh = im; _line.MaterialOverride = new StandardMaterial3D { AlbedoColor = readout.OverAllowance ? Colors.Red : colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _label.Position = (a + b) / 2 + Vector3.Up * 0.8f;
        _label.Text = readout.Remaining is { } r ? $"{readout.Distance:0.0}\"  ({r:0.0}\" left)" : $"{readout.Distance:0.0}\"";
        _label.Modulate = readout.OverAllowance ? Colors.Red : Colors.White;
        Visible = true;
    }

    public new void Hide() => Visible = false;
}
```

- [ ] **Step 2: Write `TableInput.cs`**

```csharp
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
    readonly Timer _presenceTimer = new() { WaitTime = 0.05, Autostart = true };   // 20 Hz
    bool _presenceDirty; Cursor? _cursor; Measurement? _measurement; Point3? _measureFrom;
    Drag? _drag;
    public TableTool Tool { get; set; } = TableTool.Select;
    public Selection? Selected { get; private set; }
    /// <summary>Models a declared move is positioning: their drag streams proposed ghosts, and the release sends nothing (the Ask panel commits).</summary>
    public HashSet<Guid> Proposing { get; } = new();
    public double? Allowance { get; set; }
    public event Action<Guid, Point3>? ModelDragged;
    public event Action<Guid, Vector2>? PieceRightClicked;
    public event Action<Vector3>? TableRightClicked;
    public event Action? SelectionChanged;
    public event Action<Guid>? PieceLeftClicked;

    sealed record Drag(Guid PieceId, IReadOnlyList<Guid> ModelIds, Point3 Start, Dictionary<Guid, Point3> Starts, Dictionary<Guid, Point3> Current, Yaw Yaw);
    /// <summary>The volumes a dragged piece settles on: every world volume except the ones the dragged piece itself owns,
    /// so dragging a terrain placement does not make it climb its own hill. Volume ids and piece ids are different ids,
    /// so the exclusion is built from the placement, not by subtracting the drag's piece id.</summary>
    List<WorldVolume> _settleAgainst = [];

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        view.AddChild(_ruler = new RulerNode()); AddChild(_presenceTimer);
        _presenceTimer.Timeout += () => { if (_presenceDirty) { _presenceDirty = false; _ = _session.SendPresenceAsync(Snapshot()); } };
    }

    Presence Snapshot() => new(_session.ParticipantId ?? Guid.Empty, _cursor, Selected,
        _drag is null ? [] : _drag.Current.Select(kv => new PresencePlacement(kv.Key, kv.Value, _drag.Yaw, Proposing.Contains(kv.Key) ? PlacementKind.Proposed : PlacementKind.Held)).ToList(), _measurement);

    (Point3 Point, Node3D? Piece) Pick(Vector2 screen)
    {
        var from = _view.Camera.ProjectRayOrigin(screen); var dir = _view.Camera.ProjectRayNormal(screen);
        Node3D? best = null; var bestDistance = float.MaxValue; Vector3? hit = null;
        foreach (var piece in _view.Pieces)
        {
            var bounds = piece switch { PieceNode p => p.Bounds, TerrainNode t => t.Bounds, MarkerNode m => m.Bounds, _ => new Aabb() };
            if (bounds.IntersectsRay(from, dir) is { } h && from.DistanceTo(h) < bestDistance) { best = piece; bestDistance = from.DistanceTo(h); hit = h; }
        }
        var ground = new Plane(Vector3.Up, 0).IntersectsRay(from, dir);
        return (WorldFrame.FromGodot(hit ?? ground ?? Vector3.Zero), best);
    }

    static Guid IdOf(Node3D piece) => piece switch { PieceNode p => p.ModelId, TerrainNode t => t.PlacementId, MarkerNode m => m.MarkerId, _ => Guid.Empty };

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m: OnMotion(m.Position); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b: OnLeftDown(b.Position, b.ShiftPressed); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }: _ = OnLeftUp(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } b when !Input.IsMouseButtonPressed(MouseButton.Middle):
                var (point, piece) = Pick(b.Position);
                if (piece is not null && !b.AltPressed) PieceRightClicked?.Invoke(IdOf(piece), b.Position); else if (b.AltPressed) TableRightClicked?.Invoke(WorldFrame.ToGodot(point));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.M }: Tool = Tool == TableTool.Measure ? TableTool.Select : TableTool.Measure; _measureFrom = null; break;
            case InputEventKey { Pressed: true, Keycode: Key.P }: var (at, target) = Pick(GetViewport().GetMousePosition()); _ = _session.SendPingAsync(new Ping(_session.ParticipantId ?? Guid.Empty, at, target is null ? null : IdOf(target))); break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }: Selected = null; _measurement = null; _presenceDirty = true; SelectionChanged?.Invoke(); break;
        }
    }

    void OnMotion(Vector2 screen)
    {
        var (point, piece) = Pick(screen);
        _cursor = new Cursor(point, piece is null ? null : IdOf(piece)); _presenceDirty = true;
        if (_drag is null) return;
        var delta = new Point2(point.X - _drag.Start.X, point.Y - _drag.Start.Y);
        foreach (var (id, start) in _drag.Starts)
        {
            var x = start.X + delta.X; var y = start.Y + delta.Y;
            var z = Settling.SurfaceHeightAt(new Point3(x, y, _drag.Current[id].Z + 0.5), _settleAgainst);
            _drag.Current[id] = new Point3(x, y, z);
            MoveNode(id, _drag.Current[id]);
            ModelDragged?.Invoke(id, _drag.Current[id]);
        }
        var readout = MoveRuler.Read(_drag.Start.Horizontal, point.Horizontal, Proposing.Contains(_drag.ModelIds[0]) ? Allowance : null);
        var lead = _drag.ModelIds[0];
        _ruler.Show(_drag.Starts[lead], _drag.Current[lead], readout, Colors.White);
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

    /// <summary>Abandon the drag in progress and send nothing. The Ask panel calls it when the click that started the drag turned out to be an answer — clicking an enterable container embarks the unit rather than shoving the transport about.</summary>
    public void CancelDrag() { _drag = null; _ruler.Hide(); _presenceDirty = true; _view.Redraw(); }

    void OnLeftDown(Vector2 screen, bool shift)
    {
        var (point, piece) = Pick(screen);
        if (_pickPoint is { } wantPoint) { _pickPoint = null; wantPoint(point); return; }
        if (_pickPiece is { } wantPiece && piece is not null) { _pickPiece = null; wantPiece(IdOf(piece)); return; }
        if (Tool == TableTool.Measure) { Measure(point, piece); return; }
        if (piece is null) { Selected = null; SelectionChanged?.Invoke(); _presenceDirty = true; return; }
        var id = IdOf(piece);
        var projection = _session.Projection;
        IReadOnlyList<Guid> models;
        if (piece is PieceNode pn && projection is not null)
        {
            // The unit's models the view is drawing: the ones OnTable, or — during a disembark — its exit ghosts. An occupant
            // has no node otherwise, so PieceAt is the test rather than the Location: it answers for both cases (Task 11).
            var unitModels = projection.Models.Values.Where(m => m.UnitId == pn.UnitId && _view.PieceAt(m.Id) is not null).Select(m => m.Id).ToList();
            Selected = shift ? new Selection(pn.UnitId, [pn.ModelId]) : new Selection(pn.UnitId, []);
            models = shift ? [pn.ModelId] : unitModels;
        }
        else { Selected = new Selection(id, []); models = [id]; }
        SelectionChanged?.Invoke();
        var starts = models.ToDictionary(m => m, m => Position(m));
        var own = (_session.Projection?.Board.Table ?? _session.Draft)?.Terrain.FirstOrDefault(t => t.Id == id)?.Volumes.Select(v => v.Id).ToHashSet() ?? [];
        _settleAgainst = _view.Volumes.Where(v => !own.Contains(v.Id)).ToList();
        _drag = new Drag(id, models, point, starts, new Dictionary<Guid, Point3>(starts), CurrentYaw(id));
        _presenceDirty = true;
        // Last, with the drag already made, so a listener that treats the click as an answer can CancelDrag() it: the piece's
        // own id, which for a model is the model — not the unit Selection widens it to, and what the Ask panel needs to match
        // against MoveContainment.Enterable (Task 14).
        PieceLeftClicked?.Invoke(id);
    }

    Point3 Position(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Position;
        if (Container(id) is { } inside) return inside.Placement.Position;                   // a disembark ghost starts on the container
        if (t?.Terrain.FirstOrDefault(x => x.Id == id) is { } terrain) return terrain.Position;
        return t?.Markers.Concat(p?.Markers.Values ?? []).FirstOrDefault(m => m.Id == id)?.Position ?? new Point3(0, 0, 0);
    }

    Yaw CurrentYaw(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Yaw;
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
        var moved = drag.Current.Any(kv => kv.Value != drag.Starts[kv.Key]);
        if (!moved) return;
        var status = _session.Game?.Status;
        var p = _session.Projection; var draft = _session.Draft;
        // A declared move: the Ask panel's Commit sends the Destinations, so nothing goes now and — unlike every other drop —
        // the nodes are left where the player put them. Redrawing here would snap a proposed placement back to the projection,
        // and a disembark ghost has no projected placement to snap to at all.
        if (drag.ModelIds.All(Proposing.Contains)) return;
        if (p is not null && drag.ModelIds.All(p.Models.ContainsKey))
            await _session.SendCommandAsync(new MoveModels(drag.ModelIds.Select(m => new ModelRelocation(m, new OnTable(new Placement(drag.Starts[m], drag.Yaw)), new OnTable(new Placement(drag.Current[m], drag.Yaw)))).ToList()));
        else if (status == GameStatus.Setup && draft is not null && draft.Terrain.Any(t => t.Id == drag.PieceId))
            await _session.SendEditAsync(new MoveTerrain(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        else if (status == GameStatus.Setup && draft is not null && draft.Markers.Any(m => m.Id == drag.PieceId))
            await _session.SendEditAsync(new MoveSetupMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        else if (p is not null && p.Markers.ContainsKey(drag.PieceId))
            await _session.SendCommandAsync(new MoveMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        _view.Redraw();   // server wins: the node snaps back to the projection until the event lands
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
```

A base of diameter 0 stands in for a bare table point, so measuring piece-to-point is the same edge-to-edge call.

- [ ] **Step 3: Write `PresenceOverlay.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>What others are doing, drawn in their team's colour and never animated: cursors, measurements, held pieces, ghosts, pings, and their rulers.</summary>
public partial class PresenceOverlay : Node3D
{
    TableView _view = null!; Session _session = null!;
    readonly Dictionary<Guid, Node3D> _cursors = new(), _measures = new(), _ghosts = new(); readonly Dictionary<Guid, RulerNode> _rulers = new();
    readonly Dictionary<Guid, Presence> _drawn = new();   // what this overlay last drew per participant, so Remove can release it

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        session.PresenceChanged += Draw; session.Pinged += Ping;
    }

    Color ColourOf(Guid participantId)
    {
        var teamId = _session.Game?.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
        var ordinal = _session.Game?.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0;
        return ordinal == 0 ? Colors.LightGray : Palette.Team(ordinal);
    }

    void Draw(Guid participantId)
    {
        Remove(participantId);
        var value = _session.Others.Of(participantId);
        if (value is null) { _view.Redraw(); return; }                     // a held piece returns to where the projection has it
        _drawn[participantId] = value;
        var colour = ColourOf(participantId);
        if (value.Cursor is { } c)
        {
            var cone = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.25f, Height = 0.8f }, Position = WorldFrame.ToGodot(c.Point) + Vector3.Up * 0.5f, MaterialOverride = Palette.Translucent(colour) };
            AddChild(cone); _cursors[participantId] = cone;
            if (c.Target is { } target) _view.PieceAt(target)?.SetHighlight(colour);
        }
        if (value.Measurement is { } m)
        {
            var ruler = new RulerNode(); AddChild(ruler); ruler.Show(m.From, m.To, MoveRuler.Read(m.From.Horizontal, m.To.Horizontal, null), colour); _measures[participantId] = ruler;
        }
        foreach (var placement in value.Placements)
        {
            if (placement.Kind == PlacementKind.Held)
            {
                Node3D? node = _view.PieceAt(placement.PieceId) ?? (Node3D?)_view.TerrainAt(placement.PieceId) ?? _view.MarkerAt(placement.PieceId);
                if (node is null) continue;
                node.Position = WorldFrame.ToGodot(placement.Position); node.Rotation = new Vector3(0, WorldFrame.Radians(placement.Yaw), 0);
                if (node is PieceNode piece) piece.SetHighlight(colour);
                var start = StartOf(placement.PieceId);
                if (start is { } s) { var ruler = new RulerNode(); AddChild(ruler); ruler.Show(s, placement.Position, MoveRuler.Read(s.Horizontal, placement.Position.Horizontal, null), colour); _rulers[placement.PieceId] = ruler; }
            }
            else
            {
                var ghost = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 1.5f }, Position = WorldFrame.ToGodot(placement.Position) + Vector3.Up * 0.75f, MaterialOverride = Palette.Translucent(Palette.Ghost) };
                AddChild(ghost); _ghosts[placement.PieceId] = ghost;
            }
        }
    }

    /// <summary>The start the viewer already holds: the projection's placement, or the draft's (presence → drawn "against the start position already in their projection").</summary>
    Point3? StartOf(Guid pieceId)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(pieceId)?.Location is OnTable on) return on.Placement.Position;
        return t?.Terrain.FirstOrDefault(x => x.Id == pieceId)?.Position ?? t?.Markers.FirstOrDefault(m => m.Id == pieceId)?.Position;
    }

    void Remove(Guid participantId)
    {
        foreach (var d in new[] { _cursors, _measures }) if (d.Remove(participantId, out var n)) n.QueueFree();
        _drawn.Remove(participantId, out var previous);
        foreach (var placement in previous?.Placements ?? [])
        { if (_rulers.Remove(placement.PieceId, out var r)) r.QueueFree(); if (_ghosts.Remove(placement.PieceId, out var g)) g.QueueFree(); _view.PieceAt(placement.PieceId)?.SetHighlight(null); }
        if (previous?.Cursor?.Target is { } target) _view.PieceAt(target)?.SetHighlight(null);
    }

    void Ping(Ping ping)
    {
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.2f }, Position = WorldFrame.ToGodot(ping.Point) + Vector3.Up * 0.1f, MaterialOverride = Palette.Translucent(ColourOf(ping.Sender)) };
        AddChild(ring);
        var tween = CreateTween(); tween.TweenProperty(ring, "scale", Vector3.One * 3, 2.0); tween.Parallel().TweenProperty(ring, "transparency", 1.0f, 2.0); tween.TweenCallback(Callable.From(ring.QueueFree));
    }
}
```

`Remove` reads `_drawn`, not `Session.Others`: the session has already replaced the value by the time `PresenceChanged` fires, and the overlay must release what it drew last.

- [ ] **Step 4: Run it with two clients** (two desktop accounts on the development machine, or one account plus a second local user): each sees the other's cursor cone in the team colour, a dragged piece moving live with a ruler, a measurement line, and a ping ring; the drop snaps to where the `models_moved` event says; a Home-button disconnect on one client makes its cursor vanish on the other. `./scripts/check-client` prints nothing. Commit: `Add selection, drag with held presence, the ruler, measuring, pings and others' presence`.
