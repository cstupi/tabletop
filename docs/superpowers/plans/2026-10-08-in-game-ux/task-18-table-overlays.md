# Task 18: Table overlays

**Wave:** 4 · **Depends on:** 8, 11a, 11b, 12, 17 · **Model:** sonnet

**Files:**
- Create: `client/src/Table/HoverLabels.cs` (the hovered unit's name, distance line and wounds; the Shift tag; the
  shared `LineMesh` helper)
- Create: `client/src/Table/SightLines.cs` (the sight check, drawn from `SightAnswer`)
- Create: `client/src/Table/TargetLines.cs` (assigned weapon groups to their targets)
- Create: `client/src/Table/UndoMoveButton.cs` (the **Undo move** button over `HandMoveUndo`; Ctrl+Z)

**Interfaces:**
- Consumes:
  - `HoverView.Of(Projection p, GameInfo game, Names n, Guid hoveredUnit, Guid? selectedUnit, IReadOnlyDictionary<Guid, int> maxWounds) : HoverInfo?`
    and `HoverInfo(Guid UnitId, string Name, int TeamOrdinal, double? Distance, Point3? From, Point3? To, IReadOnlyList<(Guid ModelId, int Left, int Max)> Wounds)` (Task 8).
  - `HandMoveUndo` with `UndoTo`, `Models`, `OnOwnMove(long, IReadOnlyList<Guid>)`, `OnEvent(long)` (Task 11b); `Intent.UndoMove` (Task 11a).
  - `Session.MaxWounds`, `Session.FactsChanged`, `Session.QuerySightAsync(Guid, Guid) : Task<Guid>`, `Session.SightAnswered` (Task 12).
  - `SightAnswer`, `SightLine` (Task 1).
  - `TableInput.HoveredUnitChanged`, `SelectionChanged`, `Selected`, `Eligible`, `EligibleChanged`, `SightHeld`,
    `SightHeldChanged`, `HandMoved`, `IntentPressed` (Task 17).
  - Existing: `AskViewModel.Mine`, `AskMode.Assign`, `Names`, `Palette`, `WorldFrame`, `TableView.PieceAt`, `TableView.TerrainAt`,
    `PieceNode.Bounds`, `TerrainNode.Bounds`, `Guarded.Run`, `Session.UndoToAsync`.
- Produces:

```csharp
public partial class HoverLabels : Node3D { public void Bind(Session session, TableView view, TableInput input); }
public partial class SightLines : Node3D { public void Bind(Session session, TableView view, TableInput input); }
public partial class TargetLines : Node3D { public void Bind(Session session, TableView view, TableInput input);
    public void Show(IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned); public void Clear(); }
public partial class UndoMoveButton : Button { public void Bind(Session session, TableView view, TableInput input); }
public static class LineMesh { public static ImmediateMesh Of(IEnumerable<(Vector3 From, Vector3 To, Color Colour)> segments);
    public static StandardMaterial3D Material(); }
```

The three `Node3D` overlays are children of the `TableView` (its origin is the world's, so their local positions are
global ones). `UndoMoveButton` positions itself in the table viewport's screen space, so it goes in a `CanvasLayer`
under the `TableView` (Task 19). `TargetLines` has no input of its own: the decision box's `TargetAssignment` is
`DecisionBox.Targets` (Task 16b), and Task 19 forwards its `Groups` and `Assigned` to `Show` on `TargetsChanged`.
The "Shift: one model" tag is `HoverLabels`': a screen-space `Label` in its own `CanvasLayer`, shown while Shift is
held.

No xUnit tests: these are Godot nodes over tested Core types. The task ends with a clean build and `check-client`.

- [ ] **Step 1: HoverLabels and LineMesh**

Spec → *Hover a unit*: "Its name floats above it in its team's colour. With one of my units selected, the distance
follows the name (`Red Warriors · 8.4"`) … and a thin line joins those two bases. Each model with more than one wound
shows its wounds left above it (`2/3`)." Spec → *Select*: "While Shift is held a 'Shift: one model' tag shows." Spec →
*Target picks*: "the hover label reads 'Click to target'".

Create `client/src/Table/HoverLabels.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The hovered unit's name in its team colour, the distance from my selected unit with the line between the two closest bases, and each multi-wound model's wounds left (HoverView). Shift held shows the one-model tag at the cursor.</summary>
public partial class HoverLabels : Node3D
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly Label3D _name = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 56, PixelSize = 0.01f, OutlineSize = 12, NoDepthTest = true, Visible = false };
    readonly MeshInstance3D _line = new() { MaterialOverride = LineMesh.Material() };
    readonly Node3D _wounds = new();
    readonly CanvasLayer _layer = new(); readonly Label _shift = new() { Text = "Shift: one model", Visible = false };
    Guid? _hovered;
    Action<Guid?>? _onHover; Action? _onChanged; Action<IReadOnlyList<GameEvent>>? _onEvents;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_name); AddChild(_line); AddChild(_wounds); AddChild(_layer); _layer.AddChild(_shift);
        _onHover = id => { _hovered = id; Draw(); }; _onChanged = Draw; _onEvents = _ => Draw();
        input.HoveredUnitChanged += _onHover; input.SelectionChanged += _onChanged; input.EligibleChanged += _onChanged;
        session.EventsApplied += _onEvents; session.FactsChanged += _onChanged;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.FactsChanged -= _onChanged;
        if (IsInstanceValid(_input)) { _input.HoveredUnitChanged -= _onHover; _input.SelectionChanged -= _onChanged; _input.EligibleChanged -= _onChanged; }
    }

    public override void _Process(double delta)
    {
        _shift.Visible = Input.IsKeyPressed(Key.Shift);
        if (_shift.Visible) _shift.Position = GetViewport().GetMousePosition() + new Vector2(18, 18);
    }

    void Draw()
    {
        _name.Visible = false; _line.Mesh = null;
        foreach (var c in _wounds.GetChildren()) c.QueueFree();
        if (_hovered is not { } unit || _s.Projection is not { } p || _s.Game is not { } game) return;
        if (HoverView.Of(p, game, new Names(game, p), unit, MySelectedUnit(p, game), _s.MaxWounds) is not { } info) return;
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == unit && !n.IsQueuedForDeletion()).ToList();
        if (nodes.Count == 0) return;
        var top = nodes.Max(n => n.Bounds.End.Y);
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        var eligible = _input.Eligible.Contains(unit) || nodes.Any(n => _input.Eligible.Contains(n.ModelId));
        _name.Text = eligible ? $"{info.Name} · Click to target" : info.Distance is { } d ? $"{info.Name} · {d:0.0}\"" : info.Name;
        _name.Modulate = Palette.Team(info.TeamOrdinal);
        _name.Position = new Vector3(centre.X, top + 1.2f, centre.Z);
        _name.Visible = true;
        if (info.From is { } from && info.To is { } to)
            _line.Mesh = LineMesh.Of([(WorldFrame.ToGodot(from) + Vector3.Up * 0.15f, WorldFrame.ToGodot(to) + Vector3.Up * 0.15f, Colors.White)]);
        foreach (var (model, left, max) in info.Wounds)
        {
            if (max <= 1 || _view.PieceAt(model) is not { } node) continue;
            _wounds.AddChild(new Label3D { Text = $"{left}/{max}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.01f, OutlineSize = 8, NoDepthTest = true,
                Position = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + 0.4f, node.GlobalPosition.Z) });
        }
    }

    /// <summary>The selected unit when it is one of mine: "With one of my units selected, the distance follows the name".</summary>
    Guid? MySelectedUnit(Projection p, GameInfo game)
    {
        if (_input.Selected?.PieceId is not { } id || p.Units.GetValueOrDefault(id) is not { } u) return null;
        var team = game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId;
        return team is not null && team == _s.MyTeamId ? id : null;
    }
}

/// <summary>Unshaded segments, each in its own colour, drawn over everything: the table's measuring lines.</summary>
public static class LineMesh
{
    public static ImmediateMesh Of(IEnumerable<(Vector3 From, Vector3 To, Color Colour)> segments)
    {
        var mesh = new ImmediateMesh(); var open = false;
        foreach (var (a, b, c) in segments)
        {
            if (!open) { mesh.SurfaceBegin(Mesh.PrimitiveType.Lines); open = true; }
            mesh.SurfaceSetColor(c); mesh.SurfaceAddVertex(a); mesh.SurfaceSetColor(c); mesh.SurfaceAddVertex(b);
        }
        if (open) mesh.SurfaceEnd();
        return mesh;
    }

    public static StandardMaterial3D Material() =>
        new() { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
}
```

`HoverView` decides which models carry wounds; the `max <= 1` filter is the spec's "more than one wound" and costs
nothing if `HoverView` already applies it.

- [ ] **Step 2: SightLines**

Spec → *Sight check*: "With a unit selected, hold **L** and hover a unit; while `declare_targets` is open, hovering a
candidate shows it without L. … a line from every model of the selected unit to every model of the hovered one:
green clear, amber clear but screened (cover), red blocked, with the blocking terrain piece or model lit; a summary,
`4 of 5 models can see Red Warriors · in cover`, and with `declare_targets` open, how many models can shoot." Spec →
*Shooting targets*: "Models that cannot shoot the hovered or assigned target are dimmed, from the server's sight
answer."

`declare_targets` is recognised by its legal type (`AskMode.Assign`), not by name; the observer is then the Ask's
frame actor. `BlockedBy` holds terrain volume ids (`TerrainVolume.Id`) and model ids (`Sight` adds each model's body
under its own id). The summary's "in cover" reads the lines: every clear line is screened. That is a reading of the
answer, not a rule; the shooting rule decides cover on the server.

Create `client/src/Table/SightLines.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The server's sight answer drawn: one line per model pair (green clear, amber screened, red blocked), the blockers lit, a summary over the target, and under a target pick the models that cannot shoot dimmed.</summary>
public partial class SightLines : Node3D
{
    static readonly Color ClearColour = new(0.2f, 0.9f, 0.3f), ScreenedColour = new(1f, 0.75f, 0.1f), BlockedColour = new(0.95f, 0.15f, 0.15f);
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() };
    readonly Node3D _marks = new();
    readonly Label3D _summary = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 44, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true, Visible = false };
    Guid? _hovered, _query; bool _asking;
    readonly Dictionary<Guid, SightAnswer> _early = new();   // answers that beat their QuerySightAsync continuation
    Action<Guid?>? _onHover; Action? _onChanged; Action<IReadOnlyList<GameEvent>>? _onEvents; Action<SightAnswer>? _onAnswer;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_lines); AddChild(_marks); AddChild(_summary);
        _onHover = id => { _hovered = id; Ask(); }; _onChanged = Ask; _onEvents = _ => Ask(); _onAnswer = OnAnswer;
        input.HoveredUnitChanged += _onHover; input.SightHeldChanged += _onChanged; input.SelectionChanged += _onChanged;
        session.EventsApplied += _onEvents; session.SightAnswered += _onAnswer;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.SightAnswered -= _onAnswer;
        if (IsInstanceValid(_input)) { _input.HoveredUnitChanged -= _onHover; _input.SightHeldChanged -= _onChanged; _input.SelectionChanged -= _onChanged; }
    }

    /// <summary>My open Ask, when it is a target assignment: the shooting unit is its frame's actor.</summary>
    Guid? Shooter(Projection p, GameInfo game) =>
        AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds) is { Mode: AskMode.Assign } mine && mine.Ask.FrameId is { } f
            ? p.Stack.FirstOrDefault(x => x.Id == f)?.Context.ActorUnitId : null;

    async void Ask()
    {
        if (_s.Projection is not { } p || _s.Game is not { } game) { Clear(); return; }
        var observer = Shooter(p, game) ?? (_input.SightHeld ? _input.Selected?.PieceId : null);
        if (observer is not { } from || _hovered is not { } to || from == to || !p.Units.ContainsKey(from)) { _query = null; Clear(); return; }
        _asking = true;
        try
        {
            var id = await _s.QuerySightAsync(from, to);
            _query = id;
            if (_early.Remove(id, out var answer)) Draw(answer);
        }
        catch (Exception ex) { GD.PushWarning($"Sight query failed: {ex.Message}"); }
        finally { _asking = false; _early.Clear(); }
    }

    void OnAnswer(SightAnswer answer)
    {
        if (answer.QueryId == _query) Draw(answer);
        else if (_asking) _early[answer.QueryId] = answer;
    }

    void Clear()
    {
        _lines.Mesh = null; _summary.Visible = false;
        foreach (var c in _marks.GetChildren()) c.QueueFree();
    }

    void Draw(SightAnswer answer)
    {
        Clear();
        if (_s.Projection is not { } p || _s.Game is not { } game || _hovered is not { } target) return;
        Vector3? Eye(Guid model) => p.Models.GetValueOrDefault(model) is { Location: OnTable on } m
            ? WorldFrame.ToGodot(on.Placement.Position) + Vector3.Up * (float)(m.Slot.Height / 2) : null;
        var segments = new List<(Vector3, Vector3, Color)>();
        foreach (var line in answer.Lines)
            if (Eye(line.FromModelId) is { } a && Eye(line.ToModelId) is { } b)
                segments.Add((a, b, !line.Clear ? BlockedColour : line.Screened ? ScreenedColour : ClearColour));
        _lines.Mesh = LineMesh.Of(segments);
        foreach (var id in answer.Lines.Where(l => !l.Clear).SelectMany(l => l.BlockedBy).Distinct())
            if (BoundsOf(p, id) is { } box) Mark(box, BlockedColour with { A = 0.35f });

        var observers = answer.Lines.Select(l => l.FromModelId).Distinct().ToList();
        var seeing = answer.Lines.Where(l => l.Clear).Select(l => l.FromModelId).Distinct().Count();
        var inCover = seeing > 0 && answer.Lines.Where(l => l.Clear).All(l => l.Screened);
        var text = $"{seeing} of {observers.Count} models can see {new Names(game, p).Unit(target)}" + (inCover ? " · in cover" : "");
        if (Shooter(p, game) is not null)
        {
            text += $" · {answer.CanShoot.Count} can shoot";
            foreach (var id in observers.Where(o => !answer.CanShoot.Contains(o)))
                if (_view.PieceAt(id) is { } node) Mark(node.Bounds, new Color(0, 0, 0, 0.55f));
        }
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == target).ToList();
        if (nodes.Count == 0) return;
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        _summary.Text = text;
        _summary.Position = new Vector3(centre.X, nodes.Max(n => n.Bounds.End.Y) + 2.0f, centre.Z);   // above the hover label
        _summary.Visible = true;
    }

    /// <summary>A blocker's box: a model's node, or the terrain piece that owns the volume.</summary>
    Aabb? BoundsOf(Projection p, Guid id)
    {
        if (_view.PieceAt(id) is { } piece) return piece.Bounds;
        var placement = p.Board.Table.Terrain.FirstOrDefault(t => t.Volumes.Any(v => v.Id == id));
        return placement is not null && _view.TerrainAt(placement.Id) is { } terrain ? terrain.Bounds : null;
    }

    void Mark(Aabb box, Color colour) =>
        _marks.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box.Size + Vector3.One * 0.1f }, Position = box.GetCenter(), MaterialOverride = Palette.Translucent(colour) });
}
```

- [ ] **Step 3: TargetLines**

Spec → *Shooting targets*: "Clicking an enemy aims every picked group at it; a line in that target's colour runs from
the group's shooters to it." p.8 caps the targets at two (`Assignment.MaxDistinctTargets`), so two colours serve. A
group's `AskOption.Pieces` are weapon ids (`GfShooting`: `new AskOption(group.Name, group.Name, group.WeaponIds)`);
its shooters are the models whose slot carries one of them.

Create `client/src/Table/TargetLines.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>Each assigned weapon group's shooters joined to its target, in that target's colour: first target orange, second cyan.</summary>
public partial class TargetLines : Node3D
{
    static readonly Color[] TargetColours = [new(1f, 0.55f, 0.1f), new(0.2f, 0.85f, 0.95f)];
    Session _s = null!; TableView _view = null!;
    readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() };
    IReadOnlyList<AskOption> _groups = []; IReadOnlyDictionary<string, Guid> _assigned = new Dictionary<string, Guid>();
    Action<IReadOnlyList<GameEvent>>? _onEvents;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view;
        AddChild(_lines);
        _onEvents = _ => Draw();                     // a model removed or moved mid-pick
        session.EventsApplied += _onEvents;
    }

    public override void _ExitTree() { if (_s is not null) _s.EventsApplied -= _onEvents; }

    /// <summary>The decision box's assignment: its weapon groups and group id → target unit. Empty draws nothing.</summary>
    public void Show(IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned) { _groups = groups; _assigned = assigned; Draw(); }

    public void Clear() => Show([], new Dictionary<string, Guid>());

    void Draw()
    {
        _lines.Mesh = null;
        if (_s.Projection is not { } p || _assigned.Count == 0) return;
        var targets = _assigned.Values.Distinct().ToList();
        Vector3? At(Guid model) => p.Models.GetValueOrDefault(model) is { Location: OnTable on } m
            ? WorldFrame.ToGodot(on.Placement.Position) + Vector3.Up * (float)(m.Slot.Height / 2) : null;
        Vector3? Centre(Guid unit)
        {
            var at = p.Models.Values.Where(m => m.UnitId == unit).Select(m => At(m.Id)).OfType<Vector3>().ToList();
            return at.Count == 0 ? null : at.Aggregate(Vector3.Zero, (a, v) => a + v) / at.Count;
        }
        var segments = new List<(Vector3, Vector3, Color)>();
        foreach (var group in _groups)
        {
            if (!_assigned.TryGetValue(group.Id, out var target) || Centre(target) is not { } to) continue;
            var colour = TargetColours[targets.IndexOf(target) % TargetColours.Length];
            var shooters = p.Models.Values.Where(m => m.Slot.Weapons.Any(w => group.Pieces.Contains(w.Id)));
            foreach (var shooter in shooters) if (At(shooter.Id) is { } from) segments.Add((from, to, colour));
        }
        _lines.Mesh = LineMesh.Of(segments);
    }
}
```

- [ ] **Step 4: UndoMoveButton**

Spec → *Moves*: "Under `warn` and `silent` the move is sent on release, and an **Undo move** button appears beside the
moved unit. It sends `Undo` to the event before that `ModelsMoved` and stays until any later event lands in the log,
since an undo to that point would take the later events with it. **Ctrl+Z** does the same."

`TableInput.HandMoved` names the models when the `MoveModels` goes out. The first `ModelsMoved` event I am the actor
of that moves all of them is that move: `HandMoveUndo.OnOwnMove(seq, models)`. Every other event goes to
`OnEvent(seq)`, which expires the undo once a later seq lands (Task 11b, Review Focus 2). A refused move never produces
the event, so the button never shows for it.

Create `client/src/Table/UndoMoveButton.cs`:

```csharp
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
        _onMoved = models => _sent = models;
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

    void OnEvents(IReadOnlyList<GameEvent> events)
    {
        foreach (var e in events)
        {
            if (_sent is { } sent && e.Actor == _s.ParticipantId && e.Payload is ModelsMoved moved && sent.All(id => moved.Moves.Any(r => r.ModelId == id)))
            { _undo.OnOwnMove(e.Seq, sent); _sent = null; }
            else _undo.OnEvent(e.Seq);
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
```

- [ ] **Step 5: Build and check**

Run: `dotnet build client/Tabletop.csproj 2>&1 | grep -E "error|Build succeeded"`
Expected: `Build succeeded`.

Run: `./scripts/check-client`
Expected: no output, exit 0.

The overlays are not in the scene until Task 19 adds them; their on-screen checks are in Task 19's launch list.

- [ ] **Step 6: Commit**

```bash
git add client/src/Table/HoverLabels.cs client/src/Table/SightLines.cs client/src/Table/TargetLines.cs client/src/Table/UndoMoveButton.cs
git commit -m "Table overlays: hover labels, sight lines, target lines and the undo-move button

Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
