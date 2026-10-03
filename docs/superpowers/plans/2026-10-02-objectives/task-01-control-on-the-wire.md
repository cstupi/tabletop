# Task 1: Control on the wire and in the projection

**Files:**
- Modify: `libs/Tabletop.Protocol/Events.cs`, `StateChanges.cs`, `Commands.cs`, `Projection.cs`, `EventApplier.cs`
- Modify: `client/src/Core/Names.cs`, `client/src/Core/LogText.cs`, `sim/Recording/LogLines.cs`
- Modify: `viewer/src/lib/protocol/wire.generated.ts` (regenerated), `viewer/src/lib/live/apply.ts`, `viewer/src/lib/live/logLines.ts`
- Test: `tests/Tabletop.Protocol.Tests/Samples.cs`, `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, `tests/Tabletop.Client.Tests/ViewModelTests.cs`, `tests/Tabletop.Sim.Tests/RecordingTests.cs`, `viewer/tests/apply.test.ts`

**Interfaces:**
- Consumes: `EventApplier.ApplyTo`/`ApplyChange`, `MarkerRemoved`, `Names`, `LogLines.Of`, `lineOf`.
- Produces: `RoundEnded(int Round, IReadOnlyList<StateChange> Changes) : EventPayload` (`round_ended`); `ObjectiveControlSet(Guid MarkerId, Guid? TeamId) : StateChange` (`objective_control_set`, registered on `StateChange` and on `EventPayload`); `SetObjectiveControl(Guid MarkerId, Guid? TeamId) : Command` (`set_objective_control`); `Projection.Control : IReadOnlyDictionary<Guid, Guid>`; `Names.Marker(Guid) : string`; the viewer's `Projection.control: Map<string, string>`.

Spec: objectives spec → *Events, changes and the projection*, *What players see* (log lines), *The wire*.

| Log | `round_ended` | `objective_control_set` |
| --- | --- | --- |
| client (`LogText`) | `ended round 4` then `: Team 1 took A, B became neutral` when it carries changes | `Team 1 took A` / `A became neutral` |
| sim and viewer (one text) | `Round 4 ended` | `A to Team 1` / `A neutral` |

A marker is named by its label, from the play markers or else the setup markers, or by its id's first eight characters.

- [ ] **Step 1: Write the failing Protocol tests**

Append to `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, inside the class:

```csharp
    [Fact]
    public void Control_follows_its_changes_alone_or_inside_a_round_end_and_leaves_with_the_marker()
    {
        var a = Fresh();
        var objective = Samples.Board.Table.Markers.First(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
        a.Apply(Ev(1, new RoundEnded(1, [new ObjectiveControlSet(objective, Samples.Team1), new ObjectiveControlSet(Samples.Token.Id, Samples.Team2)])));
        Assert.Equal(Samples.Team1, a.Projection.Control[objective]);
        a.Apply(Ev(2, new ObjectiveControlSet(objective, null)));
        Assert.False(a.Projection.Control.ContainsKey(objective));
        a.Apply(Ev(3, new MarkerPlaced(Samples.Token)));
        a.Apply(Ev(4, new MarkerRemoved(Samples.Token.Id)));
        Assert.Empty(a.Projection.Control);
    }
```

`Samples.Board`'s table is `TableStateTests.Sample()`, which carries one objective. An `ObjectiveControlSet` names a marker the applier does not look up (an assignment cannot miss, as `score_set`), so naming a marker not yet placed applies.

Append `new RoundEnded(4, [new ObjectiveControlSet(Token.Id, Team1)]), new ObjectiveControlSet(Token.Id, null),` to `Samples.Events` and `new SetObjectiveControl(Token.Id, Team1)` to the command samples in `tests/Tabletop.Protocol.Tests/Samples.cs`.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.Protocol.Tests --filter Control_follows`
Expected: compile errors for `RoundEnded`, `ObjectiveControlSet`, `Control`.

- [ ] **Step 3: The types and the applier**

`libs/Tabletop.Protocol/StateChanges.cs`: register and declare, after `MarkerRemoved`:

```csharp
[JsonDerivedType(typeof(ObjectiveControlSet), "objective_control_set")]
```

```csharp
/// <summary>Who controls an objective marker (GF p.6); TeamId null is neutral. Written by the round-end step and by the menu.</summary>
public sealed record ObjectiveControlSet(Guid MarkerId, Guid? TeamId) : StateChange;
```

`libs/Tabletop.Protocol/Events.cs`: on `EventPayload`'s list add `[JsonDerivedType(typeof(RoundEnded), "round_ended")]` after `round_started`, and `[JsonDerivedType(typeof(ObjectiveControlSet), "objective_control_set")]` after `marker_removed`; declare after `RoundStarted`:

```csharp
/// <summary>The end of a round, after a player ended it and before the next round starts: the ruleset's changes at that moment (OPR p.6: objectives seized, contested, kept). Written only when there are changes.</summary>
public sealed record RoundEnded(int Round, IReadOnlyList<StateChange> Changes) : EventPayload;
```

`libs/Tabletop.Protocol/Commands.cs`: `[JsonDerivedType(typeof(SetObjectiveControl), "set_objective_control")]` after `set_round`, and after `SetRound`:

```csharp
/// <summary>The menu's override of who controls an objective marker; TeamId null makes it neutral.</summary>
public sealed record SetObjectiveControl(Guid MarkerId, Guid? TeamId) : Command;
```

`libs/Tabletop.Protocol/Projection.cs`: beside `ScoresMut`, `internal readonly Dictionary<Guid, Guid> ControlMut = new();`, and beside `Scores`:

```csharp
    /// <summary>Objective marker id to the team that controls it; a neutral marker is absent.</summary>
    public IReadOnlyDictionary<Guid, Guid> Control => ControlMut;
```

`libs/Tabletop.Protocol/EventApplier.cs`: in `ApplyTo`, beside `case RoundStarted r:`

```csharp
            case RoundEnded r: foreach (var c in r.Changes) ApplyChange(p, c); break;
```

and in `ApplyChange`, replace the `MarkerRemoved` arm and add one:

```csharp
            case MarkerRemoved m: p.MarkersMut.Remove(m.MarkerId); p.ControlMut.Remove(m.MarkerId); break;
            case ObjectiveControlSet o: if (o.TeamId is { } team) p.ControlMut[o.MarkerId] = team; else p.ControlMut.Remove(o.MarkerId); break;   // an assignment cannot miss
```

- [ ] **Step 4: Run the Protocol tests**

Run: `dotnet test tests/Tabletop.Protocol.Tests`
Expected: PASS, the registration tests (`EventTests`, `StateChangeTests`, `CommandTests`) included.

- [ ] **Step 5: Write the failing log-line tests**

Append to `tests/Tabletop.Client.Tests/ViewModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void Control_reads_as_taking_a_marker_or_making_it_neutral()
    {
        var a = Applier(new MarkerPlaced(Samples.Token));
        var names = new Names(Game, a.Projection);
        string Line(EventPayload p) => LogText.Describe(new GameEvent(9, null, p, null), names);
        Assert.Equal("the game ended round 4: Team 1 took smoke", Line(new RoundEnded(4, [new ObjectiveControlSet(Samples.Token.Id, Samples.Team1)])));
        Assert.Equal("the game ended round 4", Line(new RoundEnded(4, [])));
        Assert.Equal("the game smoke became neutral", Line(new ObjectiveControlSet(Samples.Token.Id, null)));
        var setup = Samples.Board.Table.Markers[0].Id;
        Assert.Equal($"the game Team 2 took {Names.Short(setup)}", Line(new ObjectiveControlSet(setup, Samples.Team2)));   // a setup marker with no label
    }
```

Append to `tests/Tabletop.Sim.Tests/RecordingTests.cs`, inside the class:

```csharp
    [Fact]
    public void Control_reads_the_same_in_the_recording_as_in_the_viewer()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
        var ordinals = c.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);
        var after = Projection.Initial(c.Board, c.Info);
        var marker = Guid.Parse("0198b7ac-0000-7000-8000-000000000041");
        string Line(EventPayload p) => Recording.LogLines.Of(new GameEvent(9, null, p, null), after, ordinals);
        Assert.Equal("Round 4 ended", Line(new RoundEnded(4, [new ObjectiveControlSet(marker, c.TeamIds[0])])));
        Assert.Equal("0198b7ac to Team 1", Line(new ObjectiveControlSet(marker, c.TeamIds[0])));
        Assert.Equal("0198b7ac neutral", Line(new ObjectiveControlSet(marker, null)));
    }
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter Control_reads && dotnet test tests/Tabletop.Sim.Tests --filter Control_reads`
Expected: FAIL; both fall through to their default arms.

- [ ] **Step 7: The C# lines**

`client/src/Core/Names.cs`:

```csharp
    /// <summary>A marker by its label: placed in play, or else from setup.</summary>
    public string Marker(Guid id) => (projection?.Markers.GetValueOrDefault(id) ?? projection?.Board.Table.Markers.FirstOrDefault(m => m.Id == id))?.Label ?? Short(id);
```

`client/src/Core/LogText.cs`, in `Describe`'s switch before `var other`:

```csharp
            RoundEnded r => $"ended round {r.Round}{(r.Changes.Count == 0 ? "" : $": {string.Join(", ", r.Changes.Select(c => Change(c, n)))}")}",
            ObjectiveControlSet o => Change(o, n),
```

and the helper:

```csharp
    /// <summary>A round end's change in words. Only objective control is written there today; anything else reads by its kind.</summary>
    static string Change(StateChange c, Names n) => c switch
    {
        ObjectiveControlSet { TeamId: { } team } o => $"{n.Team(team)} took {n.Marker(o.MarkerId)}",
        ObjectiveControlSet o => $"{n.Marker(o.MarkerId)} became neutral",
        _ => EventKinds.Of(c),
    };
```

`sim/Recording/LogLines.cs`, in `Of`'s switch before the default arm:

```csharp
        RoundEnded r => $"Round {r.Round} ended",
        ObjectiveControlSet { TeamId: { } team } o => $"{Marker(after, o.MarkerId)} to {Team(ordinals, team)}",
        ObjectiveControlSet o => $"{Marker(after, o.MarkerId)} neutral",
```

and the helper:

```csharp
    private static string Marker(Projection p, Guid id) =>
        (p.Markers.GetValueOrDefault(id) ?? p.Board.Table.Markers.FirstOrDefault(m => m.Id == id))?.Label ?? id.ToString("N")[..8];
```

- [ ] **Step 8: Run the C# tests**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests`
Expected: PASS.

- [ ] **Step 9: Regenerate, and write the failing viewer tests**

Run: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then append to `viewer/tests/apply.test.ts`:

```ts
describe('objective control', () => {
  it('follows its changes, alone or inside a round end, and leaves with the marker', () => {
    const a = new Applier(board, game);
    a.apply(ev(1, { kind: 'round_ended', round: 1, changes: [{ kind: 'objective_control_set', markerId: K1, teamId: T1 }] }));
    expect(a.projection.control.get(K1)).toBe(T1);
    a.apply(ev(2, { kind: 'objective_control_set', markerId: K1 }));
    expect(a.projection.control.has(K1)).toBe(false);
    a.apply(ev(3, { kind: 'objective_control_set', markerId: K9, teamId: T2 }));
    a.apply(ev(4, { kind: 'marker_removed', markerId: K9 }));
    expect(a.projection.control.size).toBe(0);
  });

  it('reads as the recording reads it', () => {
    const p = initialProjection(board, game);
    const ordinals = new Map([[T1, 1], [T2, 2]]);
    const line = (payload: unknown) => lineOf(ev(9, payload), p, ordinals);
    expect(line({ kind: 'round_ended', round: 4, changes: [] })).toBe('Round 4 ended');
    expect(line({ kind: 'objective_control_set', markerId: K1, teamId: T1 })).toBe('A to Team 1');   // the board's setup marker, labelled A
    expect(line({ kind: 'objective_control_set', markerId: K9 })).toBe(`${K9.slice(0, 8)} neutral`);
  });
});
```

`K9.slice(0, 8)` is `0198b7ac`, the same first eight characters C# prints for `id.ToString("N")[..8]`, because the first group of a GUID has no hyphen.

Run: `cd viewer && npm run check`
Expected: FAIL in `apply.ts` (`STATE_CHANGE_KINDS` misses `objective_control_set`; `round_ended` reaches `never`) and in `logLines.ts`.

- [ ] **Step 10: Port the branches and the lines**

`viewer/src/lib/live/apply.ts`:

- `Projection` gains `readonly control: Map<string, string>;` after `scores`; `initialProjection` builds `control: new Map(),` beside `scores`.
- `STATE_CHANGE_KINDS` gains `objective_control_set: true,`.
- `applyChange`: the `marker_removed` arm becomes `case 'marker_removed': p.markers.delete(change.markerId); p.control.delete(change.markerId); return;`, and a new arm:

```ts
    case 'objective_control_set':
      if (change.teamId === undefined) p.control.delete(change.markerId); else p.control.set(change.markerId, change.teamId);
      return;
```

- `applyTo`: beside `round_started`,

```ts
    case 'round_ended': for (const change of payload.changes) applyChange(p, change); break;
```

`viewer/src/lib/live/logLines.ts`, before `default`:

```ts
    case 'round_ended': return `Round ${e.round} ended`;
    case 'objective_control_set':
      return e.teamId === undefined ? `${marker(after, e.markerId)} neutral` : `${marker(after, e.markerId)} to ${team(ordinals, e.teamId)}`;
```

and the helper beside `team`:

```ts
const marker = (p: Projection, id: string): string =>
  (p.markers.get(id) ?? p.board.table.markers.find((m) => m.id === id))?.label ?? id.slice(0, 8);
```

(`Projection` is already imported into `logLines.ts` for `lineOf`'s signature.)

- [ ] **Step 11: Run everything this task touched**

Run: `dotnet test tests/Tabletop.Viewer.Tests && cd viewer && npm run check && npm test`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add libs/Tabletop.Protocol client/src/Core sim/Recording/LogLines.cs viewer/src viewer/tests tests/Tabletop.Protocol.Tests tests/Tabletop.Client.Tests tests/Tabletop.Sim.Tests
git commit -m "Add objective control to the wire and the projection, with round_ended to carry a round's changes"
```
