# Task 17: Table input

**Wave:** 3 · **Depends on:** 7, 9, 11a · **Model:** opus

**Files:**
- Modify: `client/src/Table/TableInput.cs` (selection rules, pick clicks, hover, strict gating, a drag threshold, the
  `RefusedReceived` subscription that sits inside a comment today, keys routed to the new partial)
- Create: `client/src/Table/TableInput.Keys.cs` (keys through `Hotkeys.Of`, Esc innermost first, L, Tab/Shift+Tab/Space)
- Create: `client/src/Table/TableInput.Hold.cs` (a held unit: formations, Q/E, placing the held models)
- Modify: `client/src/Table/OrbitCamera.cs` (`Centre(Vector3)` for Tab and Space)

`TableInput.cs` would pass 500 lines with everything in it, so the keys and the hold are partials.

**Interfaces:**
- Consumes: `Formation.Arrange(IReadOnlyList<Base> bases, int rows, Point3 centre, Yaw facing)` (Task 7), read as: the
  rows run perpendicular to `facing`, the front row towards it, one point per base in input order;
  `Hotkeys.Of(HotKey key, bool shift, bool ctrl) : Intent?`, `Intent` and `HotKey` (Task 11a; `HotKey`'s member names
  are `Godot.Key`'s, `KpEnter` included, so a key maps with `Enum.TryParse<HotKey>(key.ToString(), out var k)`);
  `HeaderView.ActionFrame(Projection p)` (Task 9); `GameInfo.Enforcement` (exists).
- Produces (README *Fixed interfaces → Godot*):

```csharp
public event Action<Guid?>? HoveredUnitChanged;
public event Action<Guid>? UnitClicked;                  // also raised by Select(...), so Tab and the models strip move the sheet
public event Action<Guid>? TargetClicked;                // the eligible id clicked: a model id if Eligible holds it, else the unit id
public event Action<Guid, bool>? ModelClicked;           // left or right click on a model whose id is in Eligible
public bool SightHeld { get; }
public IReadOnlySet<Guid> Eligible { get; set; }         // unit ids, model ids or both; a unit id covers its models
public event Action? EligibleChanged;                    // the setter ran: whoever composes highlights relights
public event Action? SightHeldChanged;                   // L went down or up
public event Action<IReadOnlyList<Guid>>? HandMoved;     // a hand move's MoveModels went out for these models
public event Action<Intent>? IntentPressed;              // every intent the table does not consume itself
public bool Holding { get; }                             // a drag is armed
public void Select(Guid unitId, Guid? modelId = null);
// OrbitCamera
public void Centre(Vector3 target);
```

Kept as they are: `Selected`, `Proposing`, `Allowance`, `ModelDragged`, `PieceRightClicked`, `TableRightClicked`,
`SelectionChanged`, `PieceLeftClicked`, `PickNextPiece`, `PickNextPoint`, `CancelDrag`, `ConsumeOwn`, `Bind`.

**Routing.** A click on a piece whose model id or unit id is in `Eligible` answers: it raises `ModelClicked` (model id
in `Eligible` only) and `TargetClicked`, and neither selects nor starts a drag. A right click on such a model raises
`ModelClicked(id, true)` in place of the piece menu. Keys: the table consumes rows and turns while `Holding`, Esc's
drag, M, P, L, Tab, Shift+Tab and Space; Home and End stay `OrbitCamera`'s; everything else (1–9, Enter, Esc past the
drag, G, F1/?, Ctrl+Z) goes out as `IntentPressed`. Spec → *Hotkeys*: Esc cancels "innermost first: a drag, the
target assignments, the selection". The Hud parts take keys in `_Input`, which runs before the table viewport sees
the key (Task 16a → *Keys*), and the decision box leaves Esc and the digits alone while the left button is down. So an
Esc that reaches the table either cancels the drag in hand or, the box having had no use for it, clears the
selection.

**Strict.** Spec → *Moves*: "Under `strict` the drag does not start and the server refuses the command." A model drag
starts under `strict` only when every dragged model is in `Proposing`: a move step, and the deployment placement's
ghosts, which its owner puts in `Proposing` (spec → *Deployment placement*: "Nothing reaches the server before
Enter"). The click still selects. Terrain and marker drags are not hand moves and are not gated.

**Threshold.** A drag arms after the cursor travels 6 px; until then nothing moves and the release sends nothing, so a
click that wobbles never becomes a hand move (owner: "No accidental moves").

No xUnit tests: every type here is a Godot node, and the logic it calls (`Formation`, `Hotkeys`, `MoveRuler`) is tested
in its own task. The task ends with a clean build, `check-client` and the launch checks in Step 6.

- [ ] **Step 1: OrbitCamera — `Centre`**

In `client/src/Table/OrbitCamera.cs`, after the `FocusOn` line add:

```csharp
    /// <summary>Look at a point, keeping the distance and angle: Tab and Space bring a unit to the centre of the screen.</summary>
    public void Centre(Vector3 target) { _target = target; Apply(); }
```

- [ ] **Step 2: TableInput.cs — state, events, Bind**

Replace lines 14–27 (from `TableView _view = null!;` to `public event Action<Guid>? PieceLeftClicked;`) with:

```csharp
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
    /// <summary>What a click picks while a decision wants a unit or a model: unit ids, model ids, or both. Empty when nothing is being picked.</summary>
    public IReadOnlySet<Guid> Eligible { get => _eligible; set { _eligible = value; EligibleChanged?.Invoke(); } }
    public bool SightHeld { get; private set; }
    /// <summary>A drag is armed: past the threshold, models in hand.</summary>
    public bool Holding => _drag is { Armed: true };
    public event Action<Guid, Point3>? ModelDragged;
    public event Action<Guid, Vector2>? PieceRightClicked;
    public event Action<Vector3>? TableRightClicked;
    public event Action? SelectionChanged;
    public event Action<Guid>? PieceLeftClicked;
    public event Action<Guid?>? HoveredUnitChanged;
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
```

Replace the `Bind` body's line 39 (the `view.IsHeld = …` line, whose comment swallowed the `RefusedReceived`
subscription) with:

```csharp
        view.IsHeld = id => _drops.Holds(id) || _drag?.Current.ContainsKey(id) == true || Proposing.Contains(id);   // a drop awaiting its event, the pieces in hand, a proposed placement
        session.RefusedReceived += OnRefused;
```

In `Snapshot()`, replace `_drag is null ? [] :` with `_drag is not { Armed: true } ? [] :`.

- [ ] **Step 3: TableInput.cs — pointer**

Replace `_UnhandledInput` and `OnMotion` (from `public override void _UnhandledInput` through the closing brace of
`OnMotion`) with:

```csharp
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
        if (over?.UnitId == _hovered) return;
        _hovered = over?.UnitId; HoveredUnitChanged?.Invoke(_hovered);
    }
```

Replace `OnLeftDown` (from `void OnLeftDown` through its closing brace) with:

```csharp
    void OnLeftDown(Vector2 screen, bool shift)
    {
        var (point, piece) = Pick(screen);
        if (_pickPoint is { } wantPoint) { _pickPoint = null; wantPoint(OnPlane(screen, 0) ?? point); return; }   // a point pick is the table surface, never a box top
        if (_pickPiece is { } wantPiece && piece is not null) { _pickPiece = null; wantPiece(IdOf(piece)); return; }
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
    /// step, or a deployment placement's ghosts. Terrain and markers are not hand moves; silent and warn send on release.
    /// </summary>
    bool MayDrag(Node3D piece, IReadOnlyList<Guid> models) =>
        piece is not PieceNode || models.All(Proposing.Contains) || _session.Game?.Enforcement != Enforcement.Strict;

    /// <summary>Select a unit, or one of its models, as a click would: Tab and the stat sheet's models strip.</summary>
    public void Select(Guid unitId, Guid? modelId = null)
    {
        Selected = new Selection(unitId, modelId is { } m ? [m] : []); _presenceDirty = true;
        SelectionChanged?.Invoke(); UnitClicked?.Invoke(unitId);
    }

    void ClearSelection() { Selected = null; _presenceDirty = true; SelectionChanged?.Invoke(); }
```

In `OnLeftUp`, replace

```csharp
        var moved = drag.Current.Any(kv => kv.Value != drag.Starts[kv.Key]);
        if (!moved) return;
```

with

```csharp
        var moved = drag.Armed && drag.Current.Any(kv => kv.Value != drag.Starts[kv.Key]);
        if (!moved) return;
```

and replace

```csharp
                await _session.SendCommandAsync(new MoveModels(moves), held);
```

with

```csharp
                await _session.SendCommandAsync(new MoveModels(moves), held);
                HandMoved?.Invoke(drag.ModelIds);                         // the undo button waits for this move's event
```

A ghost (a disembark's, or a deployment placement's from Task 16a, drawn by `TableView.ShowGhosts`) is not `OnTable`, so a drag of one starts from
where its node stands, which is also where the player last dropped it. In `Position`, after the `OnTable` line, add:

```csharp
        if (_view.PieceAt(id) is { } ghost) return WorldFrame.FromGodot(ghost.Position);   // a ghost: where its node stands
```

and in `CurrentYaw`, after the `OnTable` line, add:

```csharp
        if (_view.PieceAt(id) is { } ghost) return WorldFrame.YawFrom(ghost.Rotation.Y);
```

`AskPanel` is deleted in Task 19. In the `CancelDrag` summary replace `The Ask panel calls it` with `The decision box calls
it`, and in `OnLeftUp` replace `// A declared move: the Ask panel's Commit sends` with
`// A declared move or a placement: the decision box's Enter sends`.

The `Container` lines after them become the fallback for an occupant with no ghost node. `MoveNode`, `PickNextPiece`,
`PickNextPoint`, `CancelDrag` and `Measure` stay.

- [ ] **Step 4: TableInput.Keys.cs**

Create `client/src/Table/TableInput.Keys.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>
/// Keys (spec → Hotkeys). Hotkeys.Of names the intent; the table acts on its own (rows and turns while holding, Esc's drag,
/// M, P, L, Tab, Space) and raises IntentPressed for the rest. Home and End are OrbitCamera's.
/// </summary>
public partial class TableInput
{
    void OnKey(InputEventKey k)
    {
        if (k.Echo || Translate(k.Keycode) is not { } key || Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) is not { } intent) return;
        if (intent == Intent.Sight) { SetSight(k.Pressed); return; }
        if (k.Pressed && Act(intent)) GetViewport().SetInputAsHandled();
    }

    /// <summary>HotKey's members carry Godot's names. A key Hotkeys does not name is no key; IsDefined stops a numeric name parsing.</summary>
    static HotKey? Translate(Key key) => Enum.TryParse<HotKey>(key.ToString(), out var k) && Enum.IsDefined(k) ? k : null;

    void SetSight(bool held) { if (SightHeld == held) return; SightHeld = held; SightHeldChanged?.Invoke(); }

    /// <summary>L released while the window was elsewhere never arrives: losing focus lets go of it.</summary>
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut) SetSight(false); }

    /// <summary>True when the key was used. Option1–5, Q and E are the held unit's while one is held, the decision box's otherwise.</summary>
    bool Act(Intent intent)
    {
        switch (intent)
        {
            case >= Intent.Option1 and <= Intent.Option5 when HoldingModels: Arrange(intent - Intent.Option1 + 1); return true;
            case Intent.TurnLeft when HoldingModels: Turn(15); return true;           // positive yaw is counter-clockwise from above
            case Intent.TurnRight when HoldingModels: Turn(-15); return true;
            case Intent.TurnLeft or Intent.TurnRight: return false;
            case Intent.Cancel: Cancel(); return true;
            case Intent.Measure: Tool = Tool == TableTool.Measure ? TableTool.Select : TableTool.Measure; _measureFrom = null; return true;
            case Intent.Ping:
                var (at, target) = Pick(GetViewport().GetMousePosition());
                Observe(_session.SendPingAsync(new Ping(_session.ParticipantId ?? Guid.Empty, at, target is null ? null : IdOf(target))));
                return true;
            case Intent.NextUnit: Cycle(1); return true;
            case Intent.PreviousUnit: Cycle(-1); return true;
            case Intent.FocusCamera: FocusActive(); return true;
            case Intent.CameraTop or Intent.CameraLevel or Intent.SingleModel: return false;
            default: IntentPressed?.Invoke(intent); return true;
        }
    }

    /// <summary>Esc, innermost first: the drag (its models back where they started); the decision box has already taken an Esc it used for assignments; then the selection and the measurement.</summary>
    void Cancel()
    {
        if (_drag is { } drag)
        {
            foreach (var (id, at) in drag.Starts) { MoveNode(id, at); ModelDragged?.Invoke(id, at); }
            CancelDrag();
            return;
        }
        IntentPressed?.Invoke(Intent.Cancel);
        _measurement = null; _measureFrom = null; _ruler.Hide();
        ClearSelection();
    }

    /// <summary>Tab and Shift+Tab: my units on the table with no action recorded this round, in army order, after the selected one; select it and bring it to the centre.</summary>
    void Cycle(int step)
    {
        if (_session.Projection is not { } p || _session.Game is not { } game || _session.MyTeamId is not { } team) return;
        var mine = game.Participants.Where(x => x.TeamId == team).Select(x => x.Id).ToHashSet();
        var units = p.Units.Values
            .Where(u => mine.Contains(u.ParticipantId) && u.Actions.Count == 0 && p.Models.Values.Any(m => m.UnitId == u.Id && m.Location is OnTable))
            .Select(u => u.Id).ToList();
        if (units.Count == 0) return;
        var at = Selected is { } s ? units.IndexOf(s.PieceId) : -1;
        var next = at < 0 ? units[step > 0 ? 0 : units.Count - 1] : units[((at + step) % units.Count + units.Count) % units.Count];
        Select(next); Focus(next);
    }

    /// <summary>Space: the unit whose activation is running (the frame that carries an action), else the selected one.</summary>
    void FocusActive()
    {
        var active = _session.Projection is { } p ? HeaderView.ActionFrame(p)?.Context.ActorUnitId : null;
        if ((active ?? Selected?.PieceId) is { } unit) Focus(unit);
    }

    void Focus(Guid unitId)
    {
        if (_session.Projection is not { } p) return;
        var at = p.Models.Values.Where(m => m.UnitId == unitId).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        if (at.Count == 0) return;
        _view.Camera.Centre(WorldFrame.ToGodot(new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z))));
    }
}
```


- [ ] **Step 5: TableInput.Hold.cs**

Create `client/src/Table/TableInput.Hold.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// A unit in hand (spec → Formations): 1–5 lay it out in that many rows about the cursor, the rows across the screen as the
/// camera sees it; Q/E turn the rows 15°. The drop is the drag's: a move step waits for Enter, anything else is a hand move.
/// </summary>
public partial class TableInput
{
    /// <summary>Holding models of the projection: a terrain piece or a marker in hand takes no formation.</summary>
    bool HoldingModels => _drag is { Armed: true } d && _session.Projection is { } p && d.ModelIds.All(p.Models.ContainsKey);

    void Arrange(int rows) { _drag!.Rows = rows; Place(Arranged(_drag.Cursor)); }

    void Turn(double degrees)
    {
        if (_drag is not { Rows: > 0 } drag) return;                    // the turn is the arrangement's; an unarranged unit keeps its shape
        drag.Turn += degrees; Place(Arranged(drag.Cursor));
    }

    /// <summary>The held models in Rows rows about the point, facing away from the viewer turned by Q/E; the models take that facing.</summary>
    Dictionary<Guid, Point3> Arranged(Point3 centre)
    {
        var drag = _drag!; var p = _session.Projection!;
        drag.Yaw = new Yaw(CameraYaw() + drag.Turn);
        var at = Formation.Arrange(drag.ModelIds.Select(id => p.Models[id].Slot.Base).ToList(), drag.Rows, centre, drag.Yaw);
        return drag.ModelIds.Select((id, i) => (id, at[i])).ToDictionary(x => x.id, x => x.Item2);
    }

    /// <summary>The camera's heading over the table as a wire yaw (WorldFrame: wire Y is Godot −Z; yaw 0 faces +Y, positive counter-clockwise).</summary>
    double CameraYaw()
    {
        var forward = -_view.Camera.GlobalTransform.Basis.Z;
        var x = forward.X; var y = -forward.Z;
        return Math.Atan2(-x, y) * 180 / Math.PI;
    }

    /// <summary>
    /// The held models to these points, each settled on what it stands over, and the ruler on the lead model. Inside a move
    /// step a model past the allowance turns red (spec → Formations: "a model beyond its move distance shows red").
    /// </summary>
    void Place(IReadOnlyDictionary<Guid, Point3> targets)
    {
        var drag = _drag!;
        foreach (var (id, to) in targets)
        {
            var z = Settling.SurfaceHeightAt(new Point3(to.X, to.Y, drag.Current[id].Z + 0.5), _settleAgainst);
            drag.Current[id] = new Point3(to.X, to.Y, z);
            MoveNode(id, drag.Current[id]);
            var piece = _view.PieceAt(id);
            if (drag.Rows > 0 && piece is not null) piece.Rotation = new Vector3(0, WorldFrame.Radians(drag.Yaw), 0);
            ModelDragged?.Invoke(id, drag.Current[id]);
            if (Allowance is { } allowance && Proposing.Contains(id) && piece is not null)
                piece.SetHighlight(MoveRuler.Read(drag.Starts[id].Horizontal, drag.Current[id].Horizontal, allowance).OverAllowance ? Colors.Red : Palette.Selected);
        }
        var lead = drag.ModelIds[0];
        _ruler.Show(drag.Starts[lead], drag.Current[lead],
            MoveRuler.Read(drag.Starts[lead].Horizontal, drag.Current[lead].Horizontal, Proposing.Contains(lead) ? Allowance : null), Colors.White);
    }
}
```

A hand move sends `drag.Yaw` for every model (unchanged), so a formation's facing goes with it; a move step's answer
keeps each model's own yaw, as the decision box builds it.

- [ ] **Step 6: Build, check and launch**

Run: `dotnet build client/Tabletop.csproj 2>&1 | grep -E "error|Warn|Build succeeded"`
Expected: `Build succeeded`, no errors.

Run: `./scripts/check-client`
Expected: no output, exit 0.

Run: `./scripts/client`, open a playing game under `warn`, and look for:
- Hovering a unit and moving off it: the cursor stays an arrow (nothing is eligible yet).
- A click on a unit with a 1–3 px wobble: the unit is selected, no `move_models` in the server log.
- Drag a unit 10" under `warn`: it moves on release. Press 3 while holding: three rows about the cursor; Q twice:
  the rows turn 30° anticlockwise; release: one hand move.
- Esc while dragging: the models go back; Esc again: the selection clears.
- Tab: my next unactivated unit is selected and centred; Space: the active unit is centred.
- A `strict` game: dragging a unit outside a move step does nothing but select it.

- [ ] **Step 7: Commit**

```bash
git add client/src/Table/TableInput.cs client/src/Table/TableInput.Keys.cs client/src/Table/TableInput.Hold.cs client/src/Table/OrbitCamera.cs
git commit -m "Table input: selection, picks, hover, formations, strict gating and the play hotkeys

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
