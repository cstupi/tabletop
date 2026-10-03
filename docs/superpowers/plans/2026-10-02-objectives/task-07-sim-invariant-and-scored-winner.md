# Task 7: `sim/`: invariant 7 and a scored winner

**Files:**
- Modify: `sim/Invariants/Invariants.cs`, `sim/Scenarios/Expectation.cs`, `sim/Scenarios/ScenarioCatalog.cs`
- Test: `tests/Tabletop.Sim.Tests/InvariantTests.cs`, `ExpectationTests.cs`, `ScenarioTests.cs`

**Interfaces:**
- Consumes: `RoundEnded`, `ObjectiveControlSet` (Task 1); the round-end step and the winners (Tasks 4 and 5); `RunOutcome.Ended`, invariant 6 and `ScenarioTests.The_final_round_scenarios_meet_their_expectations` (enforcement plan Tasks 7 and 8); `CompiledScenario.Board`, `.Info`.
- Produces: `Invariants.ControlFollowsTheMarkers(IReadOnlyList<GameEvent> log, CompiledScenario scenario) : InvariantResult`, seventh in `CheckAll`; `Expectation.Won(int TeamOrdinal)`; catalog scenario `objectives`.

Spec: objectives spec → *sim*. Invariant 7: every `objective_control_set` in the effective log, alone or inside another event's changes, names a marker the game holds at that point (a setup marker, or one placed and not removed) and, when it names a team, a team of the game. It reads the log and the board only. The spell tree's next invariant is 8.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.Sim.Tests/InvariantTests.cs`, inside the class (`E(seq, payload, actor)` is the helper the enforcement plan's Task 8 added):

```csharp
    [Fact]
    public void Control_names_only_a_marker_the_game_holds_and_a_team_it_has()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("objectives"));
        var setup = c.Board.Table.Markers[0].Id;
        var placed = c.Board.Table.Markers[0] with { Id = Guid.CreateVersion7() };
        Assert.True(Invariants.Invariants.ControlFollowsTheMarkers([
            E(1, new RoundEnded(1, [new ObjectiveControlSet(setup, c.TeamIds[0])])),
            E(2, new MarkerPlaced(placed)), E(3, new ObjectiveControlSet(placed.Id, null), Guid.CreateVersion7())], c).Held);

        var gone = Invariants.Invariants.ControlFollowsTheMarkers([E(1, new MarkerPlaced(placed)), E(2, new MarkerRemoved(placed.Id)),
            E(3, new RoundEnded(1, [new ObjectiveControlSet(placed.Id, c.TeamIds[0])]))], c);
        Assert.False(gone.Held);
        Assert.Contains("seq 3", gone.Detail);

        Assert.False(Invariants.Invariants.ControlFollowsTheMarkers([E(1, new ObjectiveControlSet(setup, Guid.CreateVersion7()))], c).Held);
    }
```

and in `Every_invariant_holds_over_a_whole_game` change the count to `Assert.Equal(7, results.Count);`.

Append to `tests/Tabletop.Sim.Tests/ExpectationTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task Won_needs_a_game_ended_naming_exactly_that_team()
    {
        var failures = await Check(new Expectation.Won(1));                              // the known run is a capped stalemate: no game_ended
        Assert.Contains(failures, f => f.Contains("expected team 1 to win"));
    }
```

In `tests/Tabletop.Sim.Tests/ScenarioTests.cs`, add `[InlineData("objectives")]` to `The_final_round_scenarios_meet_their_expectations` (and rename it `The_end_of_game_scenarios_meet_their_expectations`).

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "InvariantTests|ExpectationTests|ScenarioTests"`
Expected: compile errors: `ControlFollowsTheMarkers`, `Expectation.Won` do not exist.

- [ ] **Step 3: Invariant 7**

In `sim/Invariants/Invariants.cs`, add `ControlFollowsTheMarkers(log, scenario),` as the seventh entry of `CheckAll`, and:

```csharp
    /// <summary>
    /// Every objective_control_set, alone or inside another event's changes, names a marker the game holds at that point — a setup
    /// marker, or one placed and not removed — and, when it names a team, a team of the game (objectives spec → sim).
    /// </summary>
    public static InvariantResult ControlFollowsTheMarkers(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var markers = scenario.Board.Table.Markers.Select(m => m.Id).ToHashSet();
        var teams = scenario.Info.Teams.Select(t => t.Id).ToHashSet();
        foreach (var e in EventApplier.EffectiveLog(log))
            foreach (var change in ChangesOf(e.Payload))
            {
                switch (change)
                {
                    case MarkerPlaced m: markers.Add(m.Marker.Id); break;
                    case MarkerRemoved m: markers.Remove(m.MarkerId); break;
                    case ObjectiveControlSet o when !markers.Contains(o.MarkerId):
                        return new InvariantResult(nameof(ControlFollowsTheMarkers), false, $"seq {e.Seq} gives marker {o.MarkerId:N}, which the game does not hold");
                    case ObjectiveControlSet { TeamId: { } team } when !teams.Contains(team):
                        return new InvariantResult(nameof(ControlFollowsTheMarkers), false, $"seq {e.Seq} gives a marker to {team:N}, which is not a team of this game");
                }
            }
        return new InvariantResult(nameof(ControlFollowsTheMarkers), true, null);
    }

    private static IEnumerable<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StateChange c => [c],
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        StepCompleted s => s.Changes,
        _ => [],
    };
```

- [ ] **Step 4: The expectation**

`sim/Scenarios/Expectation.cs`:

```csharp
    /// <summary>The live game_ended names exactly this team as the winner (p.6: most markers after round 4).</summary>
    public sealed record Won(int TeamOrdinal) : Expectation;
```

and in `Expectations.Check`:

```csharp
                case Expectation.Won(var ordinal):
                    var team = scenario.Info.Teams.Single(t => t.Ordinal == ordinal).Id;
                    var end = EventApplier.EffectiveLog(log).LastOrDefault(e => e.Payload is GameEnded)?.Payload as GameEnded;
                    if (end is null || end.Winners.Count != 1 || end.Winners[0] != team)
                        failures.Add($"expected team {ordinal} to win, got {(end is null ? "no game_ended" : $"winners [{string.Join(", ", end.Winners.Select(w => scenario.Info.Teams.Single(t => t.Id == w).Ordinal))}]")}");
                    break;
```

- [ ] **Step 5: The scenario, first without its winner**

In `sim/Scenarios/ScenarioCatalog.cs`, add (with `using Tabletop.Content;` and `using Tabletop.Geometry;` if absent):

```csharp
        // The core mission scored (p.6): three markers across the middle, one deep in Red's frontline zone. Ends after round 4 under strict
        // with the team holding most markers named on game_ended.
        ["objectives"] = () => new ScenarioBuilder("objectives", seed: 1)
            .Army("Red", 3, 3).Army("Blue", 3, 3)
            .Marker(MarkerKind.Objective, new Point3(18, 24, 0), label: "West")
            .Marker(MarkerKind.Objective, new Point3(36, 24, 0), label: "Centre")
            .Marker(MarkerKind.Objective, new Point3(54, 24, 0), label: "East")
            .Marker(MarkerKind.Objective, new Point3(36, 4, 0), label: "Red home")
            .Expect(new Expectation.Outcome("ended"), new Expectation.ReachedRound(4), new Expectation.EventOfKind("round_ended"))
            .Build(),
```

- [ ] **Step 6: Find a seed with a sole winner, and name it**

Run:

```bash
dotnet run --project sim -- objectives --seeds 1..20 --out /tmp/objectives-runs
python3 - <<'PY'
import json, glob
for path in sorted(glob.glob('/tmp/objectives-runs/objectives-*.json'), key=lambda p: int(p.rsplit('-', 1)[1][:-5])):
    meta = json.load(open(path))['meta']
    won = meta.get('winningTeamId')
    ordinal = next((t['ordinal'] for t in meta['teams'] if t['id'] == won), None)
    print(path.rsplit('/', 1)[1], meta['outcome'], 'winner ordinal', ordinal)
PY
```

Expected: seeds 3, 4, 7, 8, 10, 13, 19 and 20 never seize and fail `EventOfKind("round_ended")`; the other twelve end at round 4. Take the lowest seed whose winner ordinal is not `None`: that is seed 1, winner ordinal 2. Set the scenario's `seed:` to it and append `new Expectation.Won(2)` to its `.Expect(...)`. The scenario is pinned to that seed and is not swept across seeds.

- [ ] **Step 7: Run the sim suite**

Run: `dotnet test tests/Tabletop.Sim.Tests && dotnet run --project sim -- objectives --seed <the seed> --no-recording`
Expected: PASS; the console row is `ended`, round 4, and the run passes its `Won` expectation.

- [ ] **Step 8: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests
git commit -m "Assert control follows the markers, and add a scenario that ends with a scored winner"
```
