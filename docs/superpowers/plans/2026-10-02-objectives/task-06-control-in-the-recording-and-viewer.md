# Task 6: Control in the recording and the viewer

**Files:**
- Modify: `sim/Recording/Recording.cs`, `sim/Recording/FrameProjector.cs`, `sim/Invariants/ProjectionComparer.cs`
- Modify: `viewer/src/lib/protocol/recording.generated.ts` (regenerated), `viewer/src/lib/live/project.ts`, `viewer/src/lib/board/frame.ts`, `viewer/src/lib/board/draw.ts`, `viewer/src/lib/components/Scoreboard.svelte`
- Test: `tests/Tabletop.Sim.Tests/RecordingTests.cs`, `tests/Tabletop.Sim.Tests/InvariantTests.cs`, `viewer/tests/frame.test.ts`, `viewer/tests/apply.test.ts`

**Interfaces:**
- Consumes: `Projection.Control`, the viewer's `Projection.control` (Task 1).
- Produces: `RecordedMarkerState(Guid Id, string Kind, double Diameter, string? Label, double X, double Y, double Z, double Yaw, Guid? ControlledBy = null)`; `RecordedFrame.Markers` is the setup markers (in board order, those not also in play) then the play markers, in `FrameProjector.Add` and in the viewer's `FrameProjector.add` alike; `ProjectionComparer` compares `Control`; the viewer's `MarkerShape` gains `teamOrdinal?: number`, set when the marker is controlled and the controlling team has a model on the board.

Spec: objectives spec → *What players see* (the viewer), *The wire*. A frame's markers are today `Projection.Markers` only, so a setup objective appears in no frame and the viewer never draws it. Null `ControlledBy` is not written (`RecordingJson` ignores nulls) and the checked-in fixtures hold no markers, so they stay byte-identical and `RecordingJson.SchemaVersion` stays.

- [ ] **Step 1: Write the failing C# tests**

Append to `tests/Tabletop.Sim.Tests/RecordingTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_frame_lists_setup_markers_then_play_markers_each_with_its_controller()
    {
        var scenario = ScenarioCatalog.Get("stalemate") with { Markers = [new ScenarioMarker(Tabletop.Content.MarkerKind.Objective, new Tabletop.Geometry.Point3(36, 24, 0), 2, "A")] };
        var c = ScenarioCompiler.Compile(scenario);
        var projector = new FrameProjector(c);
        var setup = c.Board.Table.Markers.Single().Id;
        var placed = new Tabletop.Content.Marker(Guid.CreateVersion7(), Tabletop.Content.MarkerKind.Token, new Tabletop.Geometry.Point3(4, 4, 0), new Tabletop.Geometry.Yaw(0), 1, "smoke", Tabletop.Content.VisualRef.OfBuiltin("chip"));
        projector.Add(new GameEvent(1, null, new MarkerPlaced(placed), null));
        var frame = projector.Add(new GameEvent(2, null, new ObjectiveControlSet(setup, c.TeamIds[1]), null));
        Assert.Equal([setup, placed.Id], frame.Markers.Select(m => m.Id));
        Assert.Equal((Guid?)c.TeamIds[1], frame.Markers[0].ControlledBy);
        Assert.Null(frame.Markers[1].ControlledBy);
        Assert.DoesNotContain("controlledBy", System.Text.Json.JsonSerializer.Serialize(projector.Frames[0], RecordingJson.Options));   // null is not written
    }
```

Append to `tests/Tabletop.Sim.Tests/InvariantTests.cs`, inside the class:

```csharp
    [Fact]
    public void The_comparer_sees_a_difference_in_control()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
        var held = new EventApplier(c.Board, c.Info);
        held.Apply(new GameEvent(1, null, new ObjectiveControlSet(Guid.CreateVersion7(), c.TeamIds[0]), null));
        var none = new EventApplier(c.Board, c.Info);
        none.Apply(new GameEvent(1, null, new ScoreSet(c.TeamIds[0], 0), null));
        Assert.Contains(ProjectionComparer.Differences(held.Projection, none.Projection), d => d.StartsWith("Control"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "A_frame_lists_setup_markers|The_comparer_sees_a_difference_in_control"`
Expected: compile error, `RecordedMarkerState` has no `ControlledBy`.

- [ ] **Step 3: The recording and the comparer**

`sim/Recording/Recording.cs`: `RecordedMarkerState` gains the trailing parameter, documented:

```csharp
/// <param name="ControlledBy">The team that controls this objective marker (Projection.Control); null when neutral or not an objective.</param>
public sealed record RecordedMarkerState(
    Guid Id, string Kind, double Diameter, string? Label, double X, double Y, double Z, double Yaw, Guid? ControlledBy = null);
```

`sim/Recording/FrameProjector.cs`: in `Add`, replace `p.Markers.Values.Select(MarkerState).ToList(),` with

```csharp
            // Setup markers first (a setup objective is in no play store), then those placed in play; the play entry wins a shared id.
            p.Board.Table.Markers.Where(m => !p.Markers.ContainsKey(m.Id)).Concat(p.Markers.Values).Select(m => MarkerState(m, p.Control)).ToList(),
```

and `MarkerState` becomes:

```csharp
    private static RecordedMarkerState MarkerState(Marker m, IReadOnlyDictionary<Guid, Guid> control) => new(
        m.Id, m.Kind.ToString().ToLowerInvariant(), m.Diameter, m.Label,
        m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees,
        control.TryGetValue(m.Id, out var team) ? team : null);
```

`sim/Invariants/ProjectionComparer.cs`, after the `Scores` check:

```csharp
        Check("Control", string.Join(",", expected.Control.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value:N}")),
                         string.Join(",", actual.Control.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value:N}")));
```

- [ ] **Step 4: Run the C# tests and regenerate**

Run: `dotnet test tests/Tabletop.Sim.Tests && TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests && dotnet test tests/Tabletop.Viewer.Tests`
Expected: PASS; `recording.generated.ts` gains `readonly controlledBy?: string;` on `RecordedMarkerState`. `AgreementFixtureTests` still passes: the fixtures have no markers.

- [ ] **Step 5: Write the failing viewer tests**

Append to `viewer/tests/frame.test.ts`:

```ts
describe('a controlled objective', () => {
  it('takes its team ordinal from the board models, and a neutral one has none', () => {
    const held = { ...frame, markers: [{ id: 'k1', kind: 'objective', diameter: 2, label: 'A', x: 36, y: 24, z: 0, yaw: 0, controlledBy: 't2' }] } as RecordedFrame;
    expect(sceneOf(board, held).markers[0]!.teamOrdinal).toBe(2);
    expect(sceneOf(board, frame).markers[0]!.teamOrdinal).toBeUndefined();
  });
});
```

Append to `viewer/tests/apply.test.ts`:

```ts
describe('the frame projector lists markers', () => {
  it('setup first, then play, each with its controller', () => {
    const projector = new FrameProjector(board, game);
    projector.add(ev(1, { kind: 'marker_placed', marker: { id: K9, kind: 'token', position: { x: 4, y: 4, z: 0 }, yaw: { degrees: 0 }, diameter: 1, visual: { builtin: 'chip' } } }));
    const frame = projector.add(ev(2, { kind: 'objective_control_set', markerId: K1, teamId: T2 }));
    expect(frame.markers.map((m) => m.id)).toEqual([K1, K9]);
    expect(frame.markers[0]!.controlledBy).toBe(T2);
    expect('controlledBy' in frame.markers[1]!).toBe(false);
  });
});
```

Run: `cd viewer && npm test`
Expected: FAIL: `teamOrdinal` is undefined, and the projector lists only `K9`.

- [ ] **Step 6: Port the marker list, the colour and the count**

`viewer/src/lib/live/project.ts`, in `add`, replace the `markers:` entry with:

```ts
      // Setup markers first (a setup objective is in no play store), then those placed in play; the play entry wins a shared id.
      markers: [...p.board.table.markers.filter((m) => !p.markers.has(m.id)), ...p.markers.values()].map((m) => ({
        id: m.id, kind: m.kind, diameter: m.diameter, ...(m.label === undefined ? {} : { label: m.label }),
        x: m.position.x, y: m.position.y, z: m.position.z, yaw: m.yaw.degrees,
        ...(p.control.has(m.id) ? { controlledBy: p.control.get(m.id)! } : {}),
      })),
```

`viewer/src/lib/board/frame.ts`: `MarkerShape` gains `readonly teamOrdinal?: number;`, and in `sceneOf`:

```ts
  const ordinalOfTeam = new Map(board.models.map((m) => [m.teamId, m.teamOrdinal]));
  const markers: MarkerShape[] = frame.markers.map((state) => {
    const ordinal = state.controlledBy === undefined ? undefined : ordinalOfTeam.get(state.controlledBy);
    return {
      id: state.id, kind: state.kind, label: state.label ?? state.id,
      centre: { x: state.x, y: state.y }, radius: state.diameter / 2,
      ...(ordinal === undefined ? {} : { teamOrdinal: ordinal }),
    };
  });
```

`viewer/src/lib/board/draw.ts`, in the marker loop: `ctx.strokeStyle = marker.teamOrdinal === undefined ? markerColour(marker.kind) : teamColour(marker.teamOrdinal);` and `ctx.lineWidth = marker.teamOrdinal === undefined ? 1.5 : 3;` (`teamColour` is already imported).

`viewer/src/lib/components/Scoreboard.svelte`: in the script, add

```ts
  const held = (teamId: string): number => (frame?.markers ?? []).filter((m) => m.controlledBy === teamId).length;
```

and after the score's `<strong>`:

```svelte
        {#if held(team.id) > 0}<span class="tag">{held(team.id)} held</span>{/if}
```

- [ ] **Step 7: Run the viewer and the agreement test**

Run: `cd viewer && npm run check && npm test && cd .. && dotnet test tests/Tabletop.Viewer.Tests`
Expected: PASS; 0 svelte-check errors.

- [ ] **Step 8: Commit**

```bash
git add sim/Recording sim/Invariants/ProjectionComparer.cs viewer/src viewer/tests tests/Tabletop.Sim.Tests
git commit -m "Record and draw setup markers and who controls each objective"
```
