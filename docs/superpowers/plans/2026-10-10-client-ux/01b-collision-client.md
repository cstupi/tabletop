# Physical collision, Tasks 8–12: client, sim, documents, validation

Part of [`01-collision.md`](01-collision.md); its Global Constraints, Rulings and Review Focus apply. Tasks 1–7 have landed.

### Task 8: What a drag collides with (client core)

**Model:** sonnet

**Files:** create `client/src/Core/DragBodies.cs`, `tests/Tabletop.Client.Tests/DragBodiesTests.cs`; modify `client/src/Core/MoveRuler.cs`.

**Interfaces:**
- Consumes: `Collision.Separate`, `Collision.Slide(piece, …)`, `Collision.RestHeight`, `MoveRegion.ImpassableVolumeIds/PassesModels`, `TerrainPlacement.WorldVolumes()`.
- Produces: `public sealed record HeldBody(Guid Id, PlacedModel At, Point2 To)`; `public static class DragBodies` with
  `IReadOnlyList<PlacedModel> Stationary(Projection p, IReadOnlyCollection<Guid> held)`, `IReadOnlyList<WorldVolume> Walls(IReadOnlyList<WorldVolume> volumes, MoveRegion? region)`,
  `IReadOnlyDictionary<Guid, Point3> Models(IReadOnlyList<HeldBody> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> volumes, MoveRegion? region, bool free)`,
  `Point3 Piece(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Point2 to, bool free)`;
  `MoveRuler.Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size, IReadOnlyList<PlacedModel>? others = null)`.

- [ ] **Step 0: Elevated built-ins (01 Rulings), committed on its own as `Declare the elevated built-ins impassable`.** In `tests/Tabletop.Client.Tests/BuiltinContentTests.cs` add, and run red:

```csharp
    [Fact]
    public void Every_builtin_solid_over_three_inches_is_impassable() =>   // p.11 elevated terrain
        Assert.All(BuiltinContent.Terrain.SelectMany(t => t.Volumes).Where(v => v.Kind == VolumeKind.Solid && v.Top > 3),
            v => Assert.True(v.Declarations.HasFlag(TerrainDeclarations.Impassable)));

    [Fact]
    public void The_ruin_is_walk_in_difficult_ground_seen_into_and_out_of() =>   // owner ruling: passable, difficult, not seen through
        Assert.Equal((VolumeKind.Area, TerrainDeclarations.Difficult | TerrainDeclarations.Cover | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut),
            BuiltinContent.Terrain.Single(t => t.Id == "ruin").Volumes.Select(v => (v.Kind, v.Declarations)).Single());
```

  Then in `client/src/Core/BuiltinContent.cs` add `Impassable |` to `large-sight-blocker`'s declarations with the comment
  `// p.11: over 3" is elevated and impassable`, and make `ruin` `V(Area, Footprint.Rect(4, 4), 4, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut)`
  (owner ruling: ruins are passable and difficult; seen into and out of, not through). Match the test's member names to `BuiltinContent`'s actual
  catalogue shape (read the file first; `Terrain`/`Volumes` are the expected names). Run green.
- [ ] **Step 1: Failing test** — `tests/Tabletop.Client.Tests/DragBodiesTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class DragBodiesTests
{
    static readonly Guid A = Guid.CreateVersion7();
    static PlacedModel M(double x, double y) => new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(x, y, 0), new Yaw(0)));
    static WorldVolume Wall(TerrainDeclarations d, double top = 6) => new(Guid.CreateVersion7(),
        new Volume(VolumeKind.Solid, Footprint.Rect(0.5, 20), new Point2(0, 0), new Yaw(0), 0, top, d, null), new TerrainTransform(new Point3(10, 10, 0), new Yaw(0), 1));
    static TerrainPlacement Piece(double x, VolumeKind kind, Footprint f, double top, TerrainDeclarations d) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"),
        new Point3(x, 30, 0), new Yaw(0), 1, [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, f, new Point2(0, 0), new Yaw(0), 0, top, d, null))]);
    static Point3 Drag(PlacedModel from, Point2 to, IReadOnlyList<PlacedModel> others, IReadOnlyList<WorldVolume> volumes, MoveRegion? region = null, bool free = false) =>
        DragBodies.Models([new HeldBody(A, from, to)], others, volumes, region, free)[A];

    [Fact]
    public void A_drag_stops_at_an_impassable_wall_and_climbs_anything_else()
    {
        Assert.Equal(9.12, Drag(M(8, 10), new(12, 10), [], [Wall(TerrainDeclarations.Impassable)]).X, 2);
        Assert.Equal(new Point3(10, 10, 2), Drag(M(8, 10), new(10, 10), [], [Wall(TerrainDeclarations.None, 2)]));
    }

    [Fact] public void Alt_places_freely_and_still_rests() =>
        Assert.Equal(new Point3(10, 10, 2), Drag(M(8, 10), new(10, 10), [M(9, 10)], [Wall(TerrainDeclarations.Impassable, 2)], free: true));

    [Fact]
    public void A_move_ask_names_the_walls_and_a_flyer_passes_models()
    {
        var wall = Wall(TerrainDeclarations.None);
        var region = new MoveRegion([A], 6, [wall.Id], [], null);
        Assert.Equal(9.12, Drag(M(8, 10), new(12, 10), [], [wall], region).X, 2);
        Assert.True(Drag(M(8, 4), new(12, 4), [M(10, 4)], [], region).X < 10 - 1.25);
        Assert.Equal(12, Drag(M(8, 4), new(12, 4), [M(10, 4)], [], region with { PassesModels = true }).X, 6);
    }

    [Fact]
    public void A_terrain_piece_rests_on_a_hill_and_stops_at_a_building()
    {
        var hill = Piece(20, VolumeKind.Solid, Footprint.Rect(6, 6), 2, TerrainDeclarations.None);
        var building = Piece(40, VolumeKind.Solid, Footprint.Rect(6, 6), 8, TerrainDeclarations.Impassable);
        var wood = Piece(2, VolumeKind.Area, Footprint.Circle(3), 6, TerrainDeclarations.None);
        Assert.Equal(new Point3(20, 30, 2), DragBodies.Piece([hill, building, wood], wood.Id, new(20, 30), free: false));
        Assert.True(DragBodies.Piece([hill, building, wood], wood.Id, new(40, 30), free: false).X <= 34 + 1e-3);
    }

    [Fact]
    public void The_drawn_region_goes_around_models_unless_the_unit_passes_them()
    {
        var region = new MoveRegion([A], 6, [], [], null);
        Assert.True(MoveRuler.Region(M(10, 10), region, [], new TableSize(72, 48), [M(13, 10)])[0].X <= 11.74 + 1e-6);
        Assert.Equal(16, MoveRuler.Region(M(10, 10), region with { PassesModels = true }, [], new TableSize(72, 48), [M(13, 10)])[0].X, 6);
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests --filter DragBodiesTests` — fails to compile.
- [ ] **Step 3: Implement** — `client/src/Core/DragBodies.cs`:

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One held model: its body where it stands now and where the cursor puts it.</summary>
public sealed record HeldBody(Guid Id, PlacedModel At, Point2 To);

/// <summary>What a drag collides with (client UX spec → Physical collision → Client). Alt places freely; the drop still rests.</summary>
public static class DragBodies
{
    public static IReadOnlyList<PlacedModel> Stationary(Projection p, IReadOnlyCollection<Guid> held) =>
        p.Models.Values.Where(m => !held.Contains(m.Id) && m.Location is OnTable)
            .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();

    /// <summary>What a body slides along: inside a move Ask the volumes it lists as impassable, otherwise every volume declared Impassable. Collision keeps the Solid ones.</summary>
    public static IReadOnlyList<WorldVolume> Walls(IReadOnlyList<WorldVolume> volumes, MoveRegion? region) => region is not null
        ? volumes.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList()
        : volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();

    public static IReadOnlyDictionary<Guid, Point3> Models(IReadOnlyList<HeldBody> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> volumes, MoveRegion? region, bool free)
    {
        if (free) return held.ToDictionary(h => h.Id, h => new Point3(h.To.X, h.To.Y, Collision.RestHeight(h.At with { Placement = h.At.Placement with { Position = new Point3(h.To.X, h.To.Y, 0) } }, volumes)));
        var ends = Collision.Separate(held.Select(h => (h.At, h.To)).ToList(), region is { PassesModels: true } ? [] : stationary, Walls(volumes, region), volumes);
        return held.Select((h, i) => (h.Id, End: ends[i])).ToDictionary(x => x.Id, x => x.End);
    }

    /// <summary>A setup terrain drag: slides against the other pieces declared Impassable, rests on the Solid tops under its footprints.</summary>
    public static Point3 Piece(IReadOnlyList<TerrainPlacement> terrain, Guid pieceId, Point2 to, bool free)
    {
        var others = terrain.Where(t => t.Id != pieceId).SelectMany(t => t.WorldVolumes()).ToList();
        return Collision.Slide(terrain.Single(t => t.Id == pieceId).WorldVolumes(), to, free ? [] : Walls(others, null), others).Position;
    }
}
```

  `MoveRuler.Region` gains `IReadOnlyList<PlacedModel>? others = null` and passes `legal.PassesModels ? null : others` as `Movement.Region`'s `models`.
- [ ] **Step 4: Run** `dotnet test tests/Tabletop.Client.Tests` — passes.
- [ ] **Step 5: Commit** `Compute what a drag slides along and rests on`, with the two trailer lines.

### Task 9: Every drag slides and rests

**Model:** opus

**Files:** modify `client/src/Table/TableInput.cs`, `client/src/Table/TableInput.Hold.cs`, `client/src/Hud/DecisionBox.cs`, `client/src/Hud/DecisionBox.Move.cs`, `client/src/Hud/KeyOverlay.cs`.

The key overlay (F1) gains the row `Alt (while dragging): place freely` beside the Shift row, and the move hint line in
`DecisionBox.Move` appends `· Alt: place freely`.

**Interfaces:** Consumes Task 8. Produces `TableInput.Region` (`public MoveRegion? Region { get; set; }`, beside `Allowance`): the open move
Ask's region, null outside one.

- [ ] **Step 1: Wire the region.** `DecisionBox.Move.cs`: `_input.Region = legal;` beside `_input.Allowance = remaining;` (line 51), `_input.Region = null;`
  beside the clears at line 33 and `DecisionBox.cs:273`; the region drawn at line 46 becomes
  `MoveRuler.Region(start, legal with { Allowance = remaining }, _view.Volumes, _view.Size, DragBodies.Stationary(p, legal.ModelIds))`.
- [ ] **Step 2: The drag.** `TableInput.cs`: a field `IReadOnlyList<PlacedModel> _stationary = [];` set where the drag starts (line 238):
  `_stationary = projection is null ? [] : DragBodies.Stationary(projection, models);`. `TableInput.Hold.cs` `Place` computes every end
  before the loop, and the loop reads `drag.Current[id] = ends[id];` in place of the `Settling` line:

```csharp
        var free = Input.IsKeyPressed(Key.Alt);                                          // Alt places freely, for corrections; the drop still rests
        var p = _session.Projection;
        var ends = p is not null && drag.ModelIds.All(p.Models.ContainsKey)
            ? DragBodies.Models(drag.ModelIds.Select(id => new HeldBody(id, new PlacedModel(new ModelBody(p.Models[id].Slot.Base, p.Models[id].Slot.Height),
                new Placement(drag.Current[id], drag.Yaw)), targets[id].Horizontal)).ToList(), _stationary, _view.Volumes, Region, free)
            : targets.ToDictionary(kv => kv.Key, kv => _session.Draft is { } d && d.Terrain.Any(t => t.Id == kv.Key)
                ? DragBodies.Piece(d.Terrain, kv.Key, kv.Value.Horizontal, free)
                : new Point3(kv.Value.X, kv.Value.Y, Collision.RestHeight(kv.Value.Horizontal, _settleAgainst)));   // a marker rests on the Solid under its point
```

  Update `Place`'s summary: the held models slide and rest through `DragBodies`; the 0.5" step-up is gone.
- [ ] **Step 3: Check** `dotnet build client/Tabletop.csproj` and `./scripts/check-client` (prints nothing).
- [ ] **Step 4: On screen** (owner, `./scripts/client`): a model dragged onto a hill stands on it; into a building stops and slides along
  it; into another unit stops at contact; a unit dragged against a wall compresses; Alt drops it anywhere and it still rests; a wood
  dragged in setup onto a hill sits on it and stops at a building; the move circle goes around models.
- [ ] **Step 5: Commit** `Slide and rest every drag; Alt places freely`, with the two trailer lines.

### Task 10: The sim's answers respect contact; `Settling` goes

**Model:** opus

**Files:** create `tests/Tabletop.Sim.Tests/ContactAnswerTests.cs`; modify `sim/Answers/PolicyView.cs`, `sim/Answers/AnswerPolicy.cs`,
`sim/Run/HandPlacement.cs`; delete `libs/Tabletop.Geometry/Settling.cs`, `tests/Tabletop.Geometry.Tests/SettlingTests.cs`.

**Interfaces:**
- Consumes: `Collision.RestHeight`, `MoveRegion.Separation/PassesModels`, `Overlap.Bases`, `Bodies.AsBlockingVolume`, `Movement.Check`, `Distance.EdgeToEdge`;
  in the test, `GfMoves.ContactBreach` and `Armies` (`Tabletop.Sim.Tests` references `Tabletop.GameServer.Tests`).
- Produces: `PolicyView.RestOf(Guid modelId, Point2 at)` (replaces `SurfaceAt`): the model's rest height with its base there.
  `AnswerPolicy` samples each model in `ModelIds` order and keeps a candidate only when it is `Clear` of the landings already taken and the
  models standing; `HandPlacement.IntoZone` skips an anchor where any base would touch a model on the table.

- [ ] **Step 1: Failing test** — `tests/Tabletop.Sim.Tests/ContactAnswerTests.cs`:

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

/// <summary>Every sim scenario defaults to strict: a sampled move the server judges a contact breach would be refused and the run would stick.</summary>
public class ContactAnswerTests
{
    [Fact]
    public void Sampled_moves_never_breach_contact()
    {
        // Five A models in a 1.5" column at x 30, B's three at x 33: most bearings cross B, land on it or end within 1" of it.
        var a = Armies.Unit("A", "4+", "4+", [], 5, []); var b = Armies.Unit("B", "4+", "4+", [], 3, []);
        var state = Armies.State([a], [b]);
        var placed = a.Models.Select((m, i) => (m.Id, X: 30.0, Y: 20 + 1.5 * i)).Concat(b.Models.Select((m, i) => (m.Id, X: 33.0, Y: 21 + 1.5 * i)))
            .Select(p => new ModelRelocation(p.Id, state.Projection.Models[p.Id].Location, new OnTable(new Placement(new Point3(p.X, p.Y, 0), new Yaw(0))))).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(placed), null));
        var region = new MoveRegion(a.Models.Select(m => m.Id).ToList(), 6, [], [], null, Separation: 1);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Guid.CreateVersion7()], region, false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, new PolicyView(state.Projection, state.Board)));
            Assert.Null(GfMoves.ContactBreach(state, region, answer));
        }
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Sim.Tests --filter ContactAnswerTests` — fails: a seed's answer breaches.
- [ ] **Step 3: Implement.** `PolicyView.cs`:
  `public double RestOf(Guid modelId, Point2 at) => Collision.RestHeight(new PlacedModel(BodyOf(modelId), new Placement(new Point3(at.X, at.Y, 0), new Yaw(0))), Volumes);`
  and `AnswerPolicy.cs:171` reads `view.RestOf(id, destination.Value)`. In `AnswerMove`, the `destinations` dictionary is built in a loop
  that fills `taken` (each landing as a `PlacedModel`, a model left standing at its own placement) and passes
  `at => Clear(id, start, at, m, view, taken)` to `Sample` (a new trailing `Func<Point2, bool>? clear = null`, tested after the landing
  check) and to `SampleExit` (same parameter, tested after `Lands`); the difficult-cap re-sample removes the model's `taken` entry, passes
  the same predicate and records the new landing. Add:

```csharp
    /// <summary>
    /// The region's own contact fields, as the server reads them (p.7): the landing on no base, the swept path across no model standing
    /// (unless PassesModels; one it already overlaps is left), and Separation from every other unit's model. A geometry test, as Lands is.
    /// </summary>
    private static bool Clear(Guid id, PlacedModel? start, Point2 at, MoveRegion m, PolicyView view, IReadOnlyDictionary<Guid, PlacedModel> taken)
    {
        var landing = new PlacedModel(view.BodyOf(id), new Placement(new Point3(at.X, at.Y, view.RestOf(id, at)), start?.Placement.Yaw ?? new Yaw(0)));
        var standing = view.Projection.Models.Keys.Where(x => x != id && !m.ModelIds.Contains(x)).Select(x => (Id: x, At: view.PlacedOf(x)))
            .Where(x => x.At is not null).Select(x => (x.Id, At: x.At!.Value)).ToList();
        var everyone = standing.Concat(taken.Where(kv => kv.Key != id).Select(kv => (Id: kv.Key, At: kv.Value))).ToList();
        if (everyone.Any(o => Overlap.Bases(landing, o.At))) return false;
        if (start is { } s && !m.PassesModels && Movement.Check(s, at, 0,
                standing.Where(o => !Overlap.Bases(s, o.At)).Select(o => Bodies.AsBlockingVolume(o.Id, o.At)).ToList(), [], null, null).CrossesImpassable) return false;
        var unit = view.ModelOf(id).UnitId;
        return m.Separation is not { } gap || everyone.All(o => view.ModelOf(o.Id).UnitId == unit || Distance.EdgeToEdge(landing, o.At) >= gap + 1e-6);
    }
```

  `HandPlacement.ChooseAnchor` takes the bodies on the table (`projection.Models.Values.Where(m => m.Location is OnTable)` as
  `PlacedModel`s) and `continue`s when `placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))`: 2D, because the server
  rests the new base on what is under it. Update its summary: placement is judged for overlap (`move_overlap`), so it avoids other bases.
  Delete `Settling.cs` and `SettlingTests.cs`; `grep -rn "Settling\." --include='*.cs' client server sim libs tests` prints nothing.
- [ ] **Step 4: Run** `dotnet test tests/Tabletop.Sim.Tests` and `dotnet test tests/Tabletop.Geometry.Tests` — pass.
- [ ] **Step 5: Commit** `Sample sim moves clear of p.7's contact clauses; retire Settling`, with the two trailer lines.

### Task 11: Documents

**Model:** sonnet

**Files:** `docs/architecture.md`, `docs/design/geometry/design.md`, `docs/design/client/design.md`, `docs/design/game-server/design.md`,
`docs/design/interpreter/design.md`, `docs/design/interpreter/opr-sequences.md`, `docs/design/simulation/design.md`,
`docs/design/contracts/design.md`, `docs/design/README.md`, `libs/Tabletop.Geometry/AGENTS.md`, `client/AGENTS.md`, `server/game/AGENTS.md`, `sim/AGENTS.md`.

- [ ] **Step 1: Find every stale fact:**
  `grep -rn -i "flagged rather than prevented\|flagged, never prevented\|never prevented\|SurfaceHeightAt\|Settling\|client-flagged\|client flags\|separation and coherency\|ten grouped\|hand-move positions are copied" docs AGENTS.md */AGENTS.md */*/AGENTS.md libs/*/AGENTS.md`.
- [ ] **Step 2: State the current decision at each hit, no history.** `docs/architecture.md:141`: base overlap, terrain intersection
  and a path through a model are prevented by the client while dragging (Alt overrides) and judged by the server under the enforcement
  setting (`move_overlap`, `move_through_models`, `move_separation`); coherency stays flagged. `:75` and `:291`: `Collision` (rest, slide,
  separate) replaces surface settling. Geometry design §6 becomes *Collision*: `RestHeight` over the outline, Area floors, `Slide` walls
  named by the caller, `Separate` in drag order, `Movement.Region` around models; the drag sequence diagrams call `DragBodies`. Game-server
  and interpreter designs: `Battlefield.Settle(state, modelId, placement)` ignores the client's Z, hand moves and draft terrain edits rest,
  `ContactBreach` joins the answer judges, `PlacementBreach` the hand-move judge, `MoveRegion.Separation/PassesModels`. Simulation design:
  `RestOf`, `Clear`, anchors clear of other bases. `libs/Tabletop.Geometry/AGENTS.md`: `Collision.cs`/`Collision.Slide.cs` replace
  `Settling.cs`; "Overlap and coherency are flagged, never prevented" becomes "Coherency is flagged; collision keeps bodies apart and the
  server judges overlap". `client/AGENTS.md`: `DragBodies`, Alt, eleven grouped meshes from `scripts/terrain-meshes`. `server/game/AGENTS.md`:
  the three breaches in the `AnswerBreach` list, `PlacementBreach` with the wrappers' forwarded members. `sim/AGENTS.md`: `AnswerPolicy` reads
  the region's contact fields.
- [ ] **Step 3: Re-run the grep**: every remaining hit is a current statement.
- [ ] **Step 4: Commit** `Document physical collision`, with the two trailer lines.

### Task 12: Validate end to end

**Model:** sonnet

- [ ] **Step 1:** `docker compose up -d`, then `dotnet build && dotnet test` — all pass, the viewer's generated types current.
- [ ] **Step 2:** `dotnet run --project sim -- suite` (in-process) — every game passes or fails only on its pinned known defect.
- [ ] **Step 3:** `dotnet run --project sim -- suite --driver socket` (the stack's `game` container rebuilt by `dotnet watch`) — the same.
  A game that sticks on `move_overlap`, `move_through_models` or `move_separation` is a sampler gap: fix `Clear` or `HandPlacement`, never the breach.
- [ ] **Step 4:** `dotnet run --project sim -- skirmish --seeds 1..20` and `-- terrain --seeds 1..20` — no `Stuck` outcome.
- [ ] **Step 5:** `./scripts/check-client` prints nothing; `cd viewer && npm run check && npm test` pass.
- [ ] **Step 6:** hand the owner Task 9's on-screen list. No commit unless a step changed a file.
