# Table UX Implementation Plan, Tasks 5–6

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Continues [`05-table-ux.md`](05-table-ux.md): its Goal, Architecture, Tech Stack, Spec, Requires landed, *From plan 04*,
Global Constraints, Review Focus and File map apply here. Tasks 5–7 need Tasks 1 and 3 (`TableUx`, `RosterView.Words/Pips`,
`Roster.Icon/Chip`).

## Task 5: Unit markers and floating results

**Files:** create `client/src/Core/UnitMarkers.cs`, `client/src/Table/UnitMarkerNodes.cs`, `client/src/Table/FloatingText.cs`,
`tests/Tabletop.Client.Tests/UnitMarkersTests.cs`; modify `client/src/Table/Animator.cs`.

**Interfaces:** consumes `TableUx`, `RosterView.Words/Pips`, `Roster.Icon/Chip`, `CameraFocus.Centroid/TableLevel`,
`OrbitCamera.Pitch`, `Ui.Icon` and `Ui.Changed` (plan 04 Task 5). Produces `UnitMarkers.Of`, `FloatingResults`
(`Seed`, `OnBoard`, `OnBatch`, `Seconds`), `Animator.Played`, `UnitMarkerNodes.Bind(Session, TableView)`,
`FloatingText.Bind(Session, TableView)`, `FloatingText.Show(GameEvent)`.

- [ ] **Step 1: Failing tests.** Create `tests/Tabletop.Client.Tests/UnitMarkersTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class UnitMarkersTests
{
    static readonly Guid Frame = Guid.CreateVersion7();
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1);
    static EventPayload Shoot => new SequenceStarted(Frame, "s", new SequenceContext(TableUx.Squad.Id, [], [], "Hold"));
    static GameEvent Step(long seq, params StateChange[] changes) => new(seq, TableUx.P1, new StepCompleted(Frame, "s", null, [], changes), null);

    [Fact]
    public void Icons_sit_over_the_centroid_above_the_tallest_model()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"), new ConditionSet(new Subject(SubjectKind.Model, A), "shaken", true), new WoundsSet(B, 1));
        var m = UnitMarkers.Of(p, new Dictionary<Guid, int> { [B] = 3 }).Single();                 // Orcs carry nothing; Reserve is off the table
        Assert.Equal((TableUx.Squad.Id, new Point3(12, 10, 1.5)), (m.UnitId, m.Anchor));
        Assert.Equal(new MarkerIcon[] { new("activated", "✓"), new("shaken", "Shaken"), new(null, "●●○") }, m.Icons);
    }

    [Fact]
    public void A_step_floats_models_removed_wounds_taken_and_conditions_set_per_unit()
    {
        var a = TableUx.Applier(TableUx.Deploy, Shoot); var f = new FloatingResults(); f.Seed(a.Projection);
        GameEvent[] first = [Step(3, new WoundsSet(A, 1), new WoundsSet(B, 1), new ModelsMoved([new(B, new OnTable(new Placement(new Point3(12, 10, 0), new Yaw(0))), new OnShelf())]),
            new ConditionSet(new Subject(SubjectKind.Unit, TableUx.Squad.Id), "shaken", true))];
        a.ApplyAll(first);
        Assert.Equal(new[] { "-1 model", "-1 wound", "Shaken" }, f.OnBatch(first, a.Projection)[3].Select(r => r.Text));   // B's wound is its removal
        GameEvent[] second = [Step(4, new WoundsSet(A, 2))]; a.ApplyAll(second);
        Assert.Equal("-1 wound", f.OnBatch(second, a.Projection)[4].Single().Text);                                         // the total went 1 → 2
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void The_catch_up_batch_after_a_board_floats_nothing()
    {
        var f = new FloatingResults(); f.OnBoard();
        var a = TableUx.Applier(); var history = TableUx.Events(TableUx.Deploy, Shoot, new StepCompleted(Frame, "s", null, [], [new WoundsSet(A, 1)])); a.ApplyAll(history);
        Assert.Empty(f.OnBatch(history, a.Projection));
        GameEvent[] next = [Step(4, new WoundsSet(A, 2))]; a.ApplyAll(next);
        Assert.Equal("-1 wound", f.OnBatch(next, a.Projection)[4].Single().Text);
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter UnitMarkersTests` → build fails: no `UnitMarkers`.

- [ ] **Step 2: Implement.** Create `client/src/Core/UnitMarkers.cs`:

```csharp
using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Key looks up the theme's icon (null: wound pips, always text); Text shows when the theme has none.</summary>
public sealed record MarkerIcon(string? Key, string Text);
public sealed record UnitMarker(Guid UnitId, Point3 Anchor, IReadOnlyList<MarkerIcon> Icons);
public sealed record FloatingResult(Guid UnitId, string Text, Point3? At);

/// <summary>Spec → Markers: over each unit on the table, at its models' centroid above the tallest model: activated, each condition, each wounded multi-wound model's pips.</summary>
public static class UnitMarkers
{
    public static IReadOnlyList<UnitMarker> Of(Projection p, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var list = new List<UnitMarker>();
        foreach (var u in p.Units.Values)
        {
            var on = p.Models.Values.Where(m => m.UnitId == u.Id && m.Location is OnTable).ToList();
            var icons = new List<MarkerIcon>();
            if (u.Actions.Count > 0) icons.Add(new("activated", "✓"));
            icons.AddRange(u.Conditions.Concat(on.SelectMany(m => m.Conditions)).Distinct().Order(StringComparer.Ordinal).Select(c => new MarkerIcon(c, RosterView.Words(c))));
            foreach (var m in on) if (maxWounds.GetValueOrDefault(m.Id, 1) is var max && max > 1 && m.Wounds > 0) icons.Add(new(null, RosterView.Pips(Math.Max(0, max - m.Wounds), max)));
            if (on.Count == 0 || icons.Count == 0) continue;
            var at = on.Select(m => ((OnTable)m.Location).Placement.Position).ToList();
            list.Add(new(u.Id, new Point3(at.Average(q => q.X), at.Average(q => q.Y), on.Max(m => ((OnTable)m.Location).Placement.Position.Z + m.Slot.Height)), icons));
        }
        return list;
    }
}

/// <summary>Spec → Floating results: a StepCompleted that removes models, wounds one or sets a condition floats a line per unit. Wounds on the wire are totals, so this
/// keeps each model's last total. The first batch after a board and a batch with an undo only re-read the totals: a join or a resync floats nothing.</summary>
public sealed class FloatingResults
{
    public const double Seconds = 1.2;
    readonly Dictionary<Guid, int> _wounds = new(); bool _catchingUp;
    public void Seed(Projection p) { _wounds.Clear(); foreach (var m in p.Models.Values) _wounds[m.Id] = m.Wounds; }
    public void OnBoard() { _wounds.Clear(); _catchingUp = true; }

    /// <summary>Results by event seq, shown when the Animator plays that event.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<FloatingResult>> OnBatch(IReadOnlyList<GameEvent> batch, Projection p)
    {
        var silent = _catchingUp || batch.Any(e => e.Payload is Undone); _catchingUp = false;
        var shown = new Dictionary<long, IReadOnlyList<FloatingResult>>();
        foreach (var e in batch) if (Of(e.Payload, p) is { Count: > 0 } r && !silent) shown[e.Seq] = r;
        if (silent) Seed(p);
        return shown;
    }

    IReadOnlyList<FloatingResult> Of(EventPayload payload, Projection p)
    {
        IReadOnlyList<StateChange> changes = payload switch { StepCompleted s => s.Changes, RoundStarted r => r.Changes, StateChange c => [c], _ => [] };
        Guid UnitOf(Guid model) => p.Models[model].UnitId;
        var gone = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => r.From is not OnShelf && r.To is OnShelf).ToList();
        var removed = gone.Select(r => r.ModelId).ToHashSet();
        var taken = changes.OfType<WoundsSet>().Where(w => !removed.Contains(w.ModelId)).Select(w => (Unit: UnitOf(w.ModelId), Taken: w.Wounds - _wounds.GetValueOrDefault(w.ModelId))).Where(w => w.Taken > 0).ToList();
        foreach (var w in changes.OfType<WoundsSet>()) _wounds[w.ModelId] = w.Wounds;
        if (payload is not StepCompleted) return [];
        // A unit wiped out has no centroid left: it floats where its first removed model stood.
        Point3? At(Guid unit) => CameraFocus.Centroid(p, unit) ?? gone.Where(r => UnitOf(r.ModelId) == unit).Select(r => r.From).OfType<OnTable>().Select(o => (Point3?)o.Placement.Position).FirstOrDefault();
        FloatingResult Line(Guid unit, string text) => new(unit, text, At(unit));
        return removed.GroupBy(UnitOf).Select(g => Line(g.Key, $"-{g.Count()} model{(g.Count() == 1 ? "" : "s")}"))
            .Concat(taken.GroupBy(w => w.Unit).Select(g => { var k = g.Sum(w => w.Taken); return Line(g.Key, $"-{k} wound{(k == 1 ? "" : "s")}"); }))
            .Concat(changes.OfType<ConditionSet>().Where(c => c.Present).Select(c => Line(c.Subject.Kind == SubjectKind.Unit ? c.Subject.Id : UnitOf(c.Subject.Id), RosterView.Words(c.Condition))))
            .ToList();
    }
}
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter UnitMarkersTests` → 3 passed.

- [ ] **Step 4: Animator.** Add `public event Action<GameEvent>? Played;` and make the first statement inside
  `foreach (var e in _queue.Take(...))` in `Pump` `Played?.Invoke(e);`, so with Animate off every result of the batch rises together.

- [ ] **Step 5: Nodes.** Both are full-rect `Control`s in the table's `CanvasLayer` (Task 8), drawing in screen space at
  projected anchors; neither takes the mouse. Create `client/src/Table/UnitMarkerNodes.cs`:

```csharp
using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>A row of icons over each unit (UnitMarkers), hidden at table level (End) and behind the camera.</summary>
public partial class UnitMarkerNodes : Control
{
    Session _s = null!; TableView _view = null!; readonly List<(Vector3 At, HBoxContainer Row)> _rows = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view; SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.BoardChanged += _onChanged; session.FactsChanged += _onChanged; Ui.Changed += _onChanged; Render();
    }
    public override void _ExitTree() { Ui.Changed -= _onChanged; if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; }

    void Render()
    {
        foreach (var (_, row) in _rows) row.QueueFree(); _rows.Clear();
        if (_s.Projection is not { } p) return;
        foreach (var m in UnitMarkers.Of(p, _s.MaxWounds))
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            foreach (var i in m.Icons)
            {
                var c = i.Key is { } k && Ui.Icon(k) is not null ? Roster.Icon(k) : Roster.Chip(i.Text);
                c.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(c);
            }
            AddChild(row); _rows.Add((WorldFrame.ToGodot(m.Anchor) + Vector3.Up * 0.6f, row));
        }
    }

    public override void _Process(double delta)
    {
        var cam = _view.Camera; var shown = !CameraFocus.TableLevel(cam.Pitch);
        foreach (var (at, row) in _rows)
        {
            row.Visible = shown && !cam.IsPositionBehind(at);
            if (row.Visible) row.Position = cam.UnprojectPosition(at) - new Vector2(row.Size.X / 2, row.Size.Y);
        }
    }
}
```

  Create `client/src/Table/FloatingText.cs`:

```csharp
using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>Results rising over a unit for 1.2 s, each shown when the Animator plays the event that produced it. Bound before the Animator is made, so it hears each batch first.</summary>
public partial class FloatingText : Control
{
    Session _s = null!; TableView _view = null!; readonly FloatingResults _results = new();
    readonly Dictionary<long, IReadOnlyList<FloatingResult>> _pending = new(); readonly List<(Vector3 At, Control Label, double Age)> _live = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view; SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        if (session.Projection is { } p) _results.Seed(p);
        _onEvents = batch => { if (_s.Projection is { } q) foreach (var (seq, r) in _results.OnBatch(batch, q)) _pending[seq] = r; };
        _onBoard = () => { _results.OnBoard(); _pending.Clear(); };
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }
    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public void Show(GameEvent e)
    {
        if (!_pending.Remove(e.Seq, out var results)) return;
        var i = 0;
        foreach (var r in results.Where(r => r.At is not null))
        {
            var label = Roster.Chip(r.Text); label.MouseFilter = MouseFilterEnum.Ignore; AddChild(label);
            _live.Add((WorldFrame.ToGodot(r.At!.Value) + Vector3.Up * (2.2f + 0.6f * i++), label, 0));
        }
    }

    public override void _Process(double delta)
    {
        var cam = _view.Camera;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var (at, label, age) = _live[i]; age += delta;
            if (age >= FloatingResults.Seconds) { label.QueueFree(); _live.RemoveAt(i); continue; }
            _live[i] = (at, label, age);
            label.Visible = !cam.IsPositionBehind(at);
            label.Position = cam.UnprojectPosition(at + Vector3.Up * (float)age) - label.Size / 2;
            label.Modulate = label.Modulate with { A = (float)(1 - age / FloatingResults.Seconds) };
        }
    }
}
```

- [ ] **Step 6: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Add unit markers and floating results over the table" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

## Task 6: Range rings

**Files:** create `client/src/Core/RangeRings.cs`, `client/src/Table/RangeRingNode.cs`, `tests/Tabletop.Client.Tests/RangeRingsTests.cs`.

**Interfaces:** consumes `TableUx`, `Distance.EdgeToEdge(PlacedModel, Point2)` (public; `BaseOutline` is internal to
the library), `LineMesh` (`HoverLabels.cs`), `TableInput.Selected/SelectionChanged/IntentPressed`, `Intent.ToggleRangeRings`,
`ClientSettings.RangeRings` (plan 04 Task 4) and `Session.UpdateSettings`. Produces `RangeRings.Of/Inches/Points`,
`RangeRingNode.Bind(Session, TableInput)`.

- [ ] **Step 1: Failing test.** Create `tests/Tabletop.Client.Tests/RangeRingsTests.cs`:

```csharp
using Tabletop.Client.Core; using Tabletop.Geometry;
namespace Tabletop.Client.Tests;

public class RangeRingsTests
{
    /// <summary>Review Focus 4. Squad: rifles 24" on the models at (10,10) and (12,10), a pistol 12" at (14,10), a knife with no Range; 1" bases.</summary>
    [Fact]
    public void Rings_per_printed_range_around_their_carriers_as_one_outline()
    {
        var p = TableUx.After(TableUx.Deploy);
        var rings = RangeRings.Of(p, TableUx.Squad.Id);
        Assert.Equal(new[] { (12.0, "12\""), (24.0, "24\"") }, rings.Select(r => (r.Inches, r.Label)));
        Assert.Equal(RangeRings.Points, rings[0].Segments.Count);                                                       // one carrier, one whole ring
        Assert.All(rings[0].Segments, s => Assert.Equal(12.5, s.A.DistanceTo(new Point2(14, 10)), 3));                  // range from the base edge
        Point2[] rifles = [new(10, 10), new(12, 10)];
        Assert.InRange(rings[1].Segments.Count, RangeRings.Points / 2, 2 * RangeRings.Points - 1);                      // the overlap is cut out
        Assert.All(rings[1].Segments, s => Assert.All(rifles, c => Assert.True(c.DistanceTo(s.A) >= 24.5 - 1e-3 && c.DistanceTo(s.B) >= 24.5 - 1e-3)));
        Assert.Empty(RangeRings.Of(p, TableUx.Orcs.Id));                                                                 // melee only
        Assert.Equal(((double?)24.0, (double?)null, (double?)null), (RangeRings.Inches("24\""), RangeRings.Inches("-"), RangeRings.Inches("0")));
    }
}
```

  `dotnet test tests/Tabletop.Client.Tests --filter RangeRingsTests` → build fails: no `RangeRings`.

- [ ] **Step 2: Implement.** Create `client/src/Core/RangeRings.cs`:

```csharp
using System.Globalization; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record RangeRing(double Inches, string Label, IReadOnlyList<(Point2 A, Point2 B)> Segments);

/// <summary>Spec → Range rings: per distinct printed Range, the union outline of the rings at that distance from the base edge of each model on the table
/// carrying a weapon of that range. No Range, "-" or 0 is melee and draws none. The label is the printed value; a rule that changes a range is not shown.</summary>
public static class RangeRings
{
    public const int Points = 96;
    public static double? Inches(string? printed) => double.TryParse(printed?.Trim().TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : null;

    public static IReadOnlyList<RangeRing> Of(Projection p, Guid unitId)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable)
            .Select(m => (Ranges: m.Slot.Weapons.Select(w => Inches(w.Stats.GetValueOrDefault("Range"))).OfType<double>().ToHashSet(), Placed: new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
        var printed = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).SelectMany(m => m.Slot.Weapons).Select(w => w.Stats.GetValueOrDefault("Range")).OfType<string>()
            .Where(r => Inches(r) is not null).DistinctBy(r => Inches(r)).OrderBy(r => Inches(r));
        return printed.Select(r => { var d = Inches(r)!.Value; return new RangeRing(d, r.Trim(), Union(models.Where(m => m.Ranges.Contains(d)).Select(m => m.Placed).ToList(), d)); }).ToList();
    }

    /// <summary>Each ring sampled on Points rays from its model's centre; a segment stays when neither end is inside another carrier's ring.</summary>
    static IReadOnlyList<(Point2, Point2)> Union(IReadOnlyList<PlacedModel> models, double range)
    {
        var segments = new List<(Point2, Point2)>();
        for (var i = 0; i < models.Count; i++)
        {
            var ring = Enumerable.Range(0, Points).Select(k => Reach(models[i], range, 2 * Math.PI * k / Points)).ToList();
            bool Outside(Point2 q) => models.Where((_, j) => j != i).All(o => Distance.EdgeToEdge(o, q) >= range - 1e-6);
            for (var k = 0; k < Points; k++) if (Outside(ring[k]) && Outside(ring[(k + 1) % Points])) segments.Add((ring[k], ring[(k + 1) % Points]));
        }
        return segments;
    }

    /// <summary>The point on the ray at `range` from the base edge, bisected: a base is convex, so the edge distance grows along the ray.</summary>
    static Point2 Reach(PlacedModel m, double range, double angle)
    {
        var c = m.Placement.Position.Horizontal; var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
        double lo = 0, hi = Math.Max(m.Body.Base.A, m.Body.Base.B) + range;
        for (var n = 0; n < 40; n++) { var mid = (lo + hi) / 2; if (Distance.EdgeToEdge(m, c + dir * mid) < range) lo = mid; else hi = mid; }
        return c + dir * lo;
    }
}
```

- [ ] **Step 3: Pass.** `dotnet test tests/Tabletop.Client.Tests --filter RangeRingsTests` → 1 passed.

- [ ] **Step 4: Node.** Create `client/src/Table/RangeRingNode.cs`:

```csharp
using Godot; using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>My selected unit's range rings when V has them on (ClientSettings.RangeRings, so the choice is remembered), each labelled with its printed range.</summary>
public partial class RangeRingNode : Node3D
{
    Session _s = null!; TableInput _input = null!; readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() }; readonly Node3D _labels = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged; Action<Intent>? _onIntent;

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input; AddChild(_lines); AddChild(_labels);
        _onEvents = _ => Draw(); _onChanged = Draw;
        _onIntent = i => { if (i == Intent.ToggleRangeRings) { _s.UpdateSettings(x => x with { RangeRings = !x.RangeRings }); Draw(); } };
        session.EventsApplied += _onEvents; input.SelectionChanged += _onChanged; input.IntentPressed += _onIntent; Draw();
    }
    public override void _ExitTree()
    {
        if (_s is null) return; _s.EventsApplied -= _onEvents;
        if (IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.IntentPressed -= _onIntent; }
    }

    void Draw()
    {
        _lines.Mesh = null; foreach (var c in _labels.GetChildren()) c.QueueFree();
        if (!_s.Settings.RangeRings || _s.Projection is not { } p || _s.Game is not { } game || _input.Selected?.PieceId is not { } unit || p.Units.GetValueOrDefault(unit) is not { } u) return;
        if (game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId is not { } team || team != _s.MyTeamId) return;   // mine only
        var colour = Palette.Team(game.Teams.First(t => t.Id == team).Ordinal);
        var rings = RangeRings.Of(p, unit);
        Vector3 At(Point2 q, double z) => WorldFrame.ToGodot(new Point3(q.X, q.Y, z));
        _lines.Mesh = LineMesh.Of(rings.SelectMany(r => r.Segments).Select(s => (At(s.A, 0.05), At(s.B, 0.05), colour)));
        foreach (var r in rings.Where(r => r.Segments.Count > 0))
            _labels.AddChild(new Label3D { Text = r.Label, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true,
                Modulate = colour, Position = At(r.Segments.MaxBy(s => s.A.Y).A, 0.4) });                                             // at the outline's far edge
    }
}
```

- [ ] **Step 5: Build, commit.** `dotnet build client/Tabletop.csproj` → 0 errors;
  `git add client/src tests/Tabletop.Client.Tests && git commit -m "Draw range rings for my selected unit" -m $'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW'`

Tasks 7–8 continue in [`05-table-ux-3.md`](05-table-ux-3.md).
