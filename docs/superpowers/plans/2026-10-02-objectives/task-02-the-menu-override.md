# Task 2: The menu override

**Files:**
- Modify: `server/game/Dispatch/CommandDispatcher.cs` (`Classify`), `server/game/Dispatch/MenuTranslation.cs`
- Test: `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs`

**Interfaces:**
- Consumes: `SetObjectiveControl`, `ObjectiveControlSet`, `Projection.Control` (Task 1).
- Produces: `SetObjectiveControl` in `CommandClass.Override`; `MenuTranslation` turns it into `ObjectiveControlSet` when the marker exists among the setup markers (`state.Board.Table.Markers`) or the play markers (`Projection.Markers`), and the team, when one is named, is a team of the game. A missing marker or team is `Refused(Unknown)`, as every override's missing reference is.

Spec: objectives spec → *Events, changes and the projection*, *The menu*. "Every property is editable" (game-flow spec, *Nothing is an invariant*): the seize step and the menu write the same change through the same applier.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs`, inside the class (not in `Overrides()`, whose `State()` places no token):

```csharp
    [Fact]
    public void Control_is_overridden_from_the_menu_for_a_setup_or_a_placed_marker_under_every_setting()
    {
        foreach (var e in new[] { Enforcement.Silent, Enforcement.Warn, Enforcement.Strict })
        {
            var state = State(e, new MarkerPlaced(Samples.Token));
            var setup = state.Board.Table.Markers.First(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
            var given = Assert.IsType<ObjectiveControlSet>(Assert.Single(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new SetObjectiveControl(setup, Samples.Team2), Watcher)).Events));
            Assert.Equal((setup, (Guid?)Samples.Team2), (given.MarkerId, given.TeamId));
            var neutral = Assert.IsType<ObjectiveControlSet>(Assert.Single(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new SetObjectiveControl(Samples.Token.Id, null), Watcher)).Events));
            Assert.Null(neutral.TeamId);
        }
        Assert.Equal(CommandClass.Override, CommandDispatcher.Classify(new SetObjectiveControl(Guid.CreateVersion7(), null)));
    }

    [Fact]
    public void Control_of_no_marker_or_for_no_team_is_refused_as_unknown()
    {
        var state = State();
        var setup = state.Board.Table.Markers.First().Id;
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new SetObjectiveControl(Guid.CreateVersion7(), Samples.Team1), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new SetObjectiveControl(setup, Guid.CreateVersion7()), Clay)).Reason);
    }
```

`State()` takes one `Samples.Board` and hands the same instance to the applier and the state, so `state.Board.Table.Markers` holds the ids the projection's board holds.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "Control_is_overridden|Control_of_no_marker"`
Expected: FAIL: `SetObjectiveControl` classifies as `Engine` and `InertEngine` refuses it.

- [ ] **Step 3: Classify and translate**

`server/game/Dispatch/CommandDispatcher.cs`, `Classify`: add `or SetObjectiveControl` to the override pattern, after `SetRound`.

`server/game/Dispatch/MenuTranslation.cs`: beside `HasMarker`,

```csharp
        bool HasObjective(Guid id) => p.Markers.ContainsKey(id) || state.Board.Table.Markers.Any(m => m.Id == id) || Missing(ref miss, $"no marker {id}");
```

and in the switch, before `_ => null`:

```csharp
            SetObjectiveControl o when HasObjective(o.MarkerId) && (o.TeamId is not { } team || HasTeam(team)) => new ObjectiveControlSet(o.MarkerId, o.TeamId),
```

- [ ] **Step 4: Run the dispatcher tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter CommandDispatcherTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add server/game/Dispatch tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs
git commit -m "Let the menu set who controls an objective marker"
```
