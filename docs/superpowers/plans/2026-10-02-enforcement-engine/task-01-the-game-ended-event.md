# Task 1: The `game_ended` event, everywhere it is read

**Files:**
- Modify: `libs/Tabletop.Protocol/Events.cs`, `libs/Tabletop.Protocol/Projection.cs`, `libs/Tabletop.Protocol/EventApplier.cs`
- Modify: `client/src/Core/LogText.cs`, `sim/Recording/LogLines.cs`
- Modify: `viewer/src/lib/protocol/wire.generated.ts` (regenerated), `viewer/src/lib/live/apply.ts`, `viewer/src/lib/live/logLines.ts`
- Test: `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, `tests/Tabletop.Client.Tests/ViewModelTests.cs`, `tests/Tabletop.Sim.Tests/RecordingTests.cs`, `viewer/tests/apply.test.ts`

**Interfaces:**
- Consumes: `EventPayload`, `RoundStarted`, `EventApplier.ApplyTo`, `Names.Team` (client), `LogLines.Of(GameEvent, Projection, IReadOnlyDictionary<Guid,int>)` (sim), `lineOf` (viewer).
- Produces: `public sealed record GameEnded(string Reason, IReadOnlyList<Guid> Winners) : EventPayload` (wire kind `game_ended`); `Projection.Ended : GameEnded?` with an `internal` setter; the viewer's `Projection.ended: GameEnded | undefined`.

Spec: enforcement spec → *The `game_ended` event*, *What clients see*, *The wire*. A new `EventPayload` kind fails `GeneratedTypesTests` and `npm run check` until the generated file and the viewer's applier handle it, so the whole kind lands in one task.

- [ ] **Step 1: Write the failing Protocol tests**

Append to `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, inside the class:

```csharp
    [Fact]
    public void Game_ended_sets_ended_and_a_round_start_clears_it()
    {
        var a = Fresh();
        var end = new GameEnded("4 rounds have been played", [Samples.Team1]);
        a.Apply(Ev(1, end));
        Assert.Equal(end, a.Projection.Ended);
        a.Apply(Ev(2, new RoundStarted(5, [Samples.Team1, Samples.Team2], [])));
        Assert.Null(a.Projection.Ended);
    }

    [Fact]
    public void Undoing_past_game_ended_clears_it()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new GameEnded("over", [])), Ev(3, new Undone(1))]);
        Assert.Null(a.Projection.Ended);
        Assert.Null(a.ProjectTo(1).Ended);
    }
```

`EventTests.Every_payload_is_registered_once_including_state_changes` already fails for any concrete `EventPayload` that is not registered, so it covers the registration.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Protocol.Tests --filter "EventApplierTests|EventTests"`
Expected: compile error, `The type or namespace name 'GameEnded' could not be found`.

- [ ] **Step 3: Add the event, the property and the applier branches**

In `libs/Tabletop.Protocol/Events.cs`, add the registration after `[JsonDerivedType(typeof(RoundStarted), "round_started")]`:

```csharp
[JsonDerivedType(typeof(GameEnded), "game_ended")]
```

and the record after `PhaseStarted`:

```csharp
/// <summary>The game is over (enforcement spec → The game_ended event). Reason is the sentence players read. Winners: one team is the
/// winner, several are a tie between them, none is no result. Actor is null when the engine ended it.</summary>
public sealed record GameEnded(string Reason, IReadOnlyList<Guid> Winners) : EventPayload;
```

In `libs/Tabletop.Protocol/Projection.cs`, after `public long LastSeq { get; internal set; }`:

```csharp
    /// <summary>The live game_ended, or null. A round_started after it clears it: a round started from the menu reopens play.</summary>
    public GameEnded? Ended { get; internal set; }
```

In `libs/Tabletop.Protocol/EventApplier.cs`, `ApplyTo`: in the `case RoundStarted r:` branch add `p.Ended = null;` as its first statement, and add a branch beside it:

```csharp
            case GameEnded g: p.Ended = g; break;
```

- [ ] **Step 4: Run the Protocol tests**

Run: `dotnet test tests/Tabletop.Protocol.Tests`
Expected: PASS.

- [ ] **Step 5: Write the failing log-line tests**

Append to `tests/Tabletop.Client.Tests/ViewModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void Game_ended_names_the_winner_or_the_tie()
    {
        var names = new Names(Game, Applier().Projection);
        string Line(params Guid[] winners) => LogText.Describe(new GameEvent(9, null, new GameEnded("4 rounds have been played", winners), null), names);
        Assert.Equal("the game ended the game: 4 rounds have been played; Team 1 won", Line(Samples.Team1));
        Assert.Equal("the game ended the game: 4 rounds have been played; tied between Team 1, Team 2", Line(Samples.Team1, Samples.Team2));
        Assert.Equal("the game ended the game: 4 rounds have been played", Line());
    }
```

Append to `tests/Tabletop.Sim.Tests/RecordingTests.cs`, inside the class (add `using Tabletop.Protocol;` and `using Tabletop.Sim.Scenarios;` if absent):

```csharp
    [Fact]
    public void Game_ended_reads_the_same_in_the_recording_as_in_the_viewer()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
        var ordinals = c.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);
        var after = Projection.Initial(c.Board, c.Info);
        string Line(params Guid[] winners) => Recording.LogLines.Of(new GameEvent(9, null, new GameEnded("over", winners), null), after, ordinals);
        Assert.Equal("Game ended: over; Team 1 won", Line(c.TeamIds[0]));
        Assert.Equal("Game ended: over; tied between Team 1, Team 2", Line(c.TeamIds[0], c.TeamIds[1]));
        Assert.Equal("Game ended: over", Line());
    }
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter Game_ended && dotnet test tests/Tabletop.Sim.Tests --filter Game_ended_reads`
Expected: FAIL; the client prints `GameEnded` (its `var other` arm) and the sim prints `game_ended`.

- [ ] **Step 7: Add the two C# lines**

In `client/src/Core/LogText.cs`, in `Describe`'s switch, before `var other => ...`:

```csharp
            GameEnded g => $"ended the game: {g.Reason}{Result(g.Winners, n)}",
```

and a helper in the class:

```csharp
    /// <summary>One winner, a tie, or nothing when the ruleset recorded no result.</summary>
    static string Result(IReadOnlyList<Guid> winners, Names n) => winners.Count switch
    {
        0 => "",
        1 => $"; {n.Team(winners[0])} won",
        _ => $"; tied between {string.Join(", ", winners.Select(n.Team))}",
    };
```

In `sim/Recording/LogLines.cs`, in `Of`'s switch, before `_ => EventKinds.Of(e.Payload)`:

```csharp
        GameEnded g => $"Game ended: {g.Reason}{Result(ordinals, g.Winners)}",
```

and the helper:

```csharp
    private static string Result(IReadOnlyDictionary<Guid, int> ordinals, IReadOnlyList<Guid> winners) => winners.Count switch
    {
        0 => "",
        1 => $"; {Team(ordinals, winners[0])} won",
        _ => $"; tied between {string.Join(", ", winners.Select(w => Team(ordinals, w)))}",
    };
```

- [ ] **Step 8: Run the C# tests**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests`
Expected: PASS.

- [ ] **Step 9: Regenerate the wire types and watch the viewer fail**

Run: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests && cd viewer && npm run check`
Expected: `wire.generated.ts` now has `GameEnded` in the `EventPayload` union; `npm run check` FAILS in `src/lib/live/apply.ts` (`Type 'GameEnded' is not assignable to type 'never'`) and in `src/lib/live/logLines.ts`.

- [ ] **Step 10: Write the failing viewer tests**

Append to `viewer/tests/apply.test.ts` (it already defines `T1`, `T2`, `game`, `board` and `const ev = (seq, payload)` at file scope):

```ts
describe('game_ended', () => {
  it('sets ended, and a round start clears it', () => {
    const a = new Applier(board, game);
    a.apply(ev(1, { kind: 'game_ended', reason: 'over', winners: [T1] }));
    expect(a.projection.ended).toEqual({ kind: 'game_ended', reason: 'over', winners: [T1] });
    a.apply(ev(2, { kind: 'round_started', round: 5, order: [T1, T2], changes: [] }));
    expect(a.projection.ended).toBeUndefined();
  });

  it('reads as the recording reads it', () => {
    const p = initialProjection(board, game);
    const ordinals = new Map([[T1, 1], [T2, 2]]);
    const line = (winners: string[]) => lineOf(ev(9, { kind: 'game_ended', reason: 'over', winners }), p, ordinals);
    expect(line([T1])).toBe('Game ended: over; Team 1 won');
    expect(line([T1, T2])).toBe('Game ended: over; tied between Team 1, Team 2');
    expect(line([])).toBe('Game ended: over');
  });
});
```

- [ ] **Step 11: Port the branch and the line**

In `viewer/src/lib/live/apply.ts`: add `GameEnded` to the type import from `../protocol/wire.generated`; add `ended: GameEnded | undefined;` to `Projection` after `lastSeq: number;`; add `ended: undefined,` to the object `initialProjection` builds (after `lastSeq: 0`); in `applyTo`'s `case 'round_started':` add `p.ended = undefined;` as the first statement; and add the branch:

```ts
    case 'game_ended': p.ended = payload; break;
```

In `viewer/src/lib/live/logLines.ts`, in `lineOf`'s switch before `default`:

```ts
    case 'game_ended': return `Game ended: ${e.reason}${result(ordinals, e.winners)}`;
```

and the helper beside `team`:

```ts
function result(ordinals: ReadonlyMap<string, number>, winners: readonly string[]): string {
  if (winners.length === 0) return '';
  if (winners.length === 1) return `; ${team(ordinals, winners[0]!)} won`;
  return `; tied between ${winners.map((w) => team(ordinals, w)).join(', ')}`;
}
```

`team(ordinals, id)` already prints `Team {n}` as `LogLines.Team` does.

- [ ] **Step 12: Run everything this task touched**

Run: `dotnet test tests/Tabletop.Viewer.Tests && cd viewer && npm run check && npm test`
Expected: PASS; `svelte-check` 0 errors.

- [ ] **Step 13: Commit**

```bash
git add libs/Tabletop.Protocol client/src/Core/LogText.cs sim/Recording/LogLines.cs viewer/src viewer/tests tests/Tabletop.Protocol.Tests tests/Tabletop.Client.Tests tests/Tabletop.Sim.Tests
git commit -m "Add game_ended, which sets Projection.Ended until a round starts"
```
