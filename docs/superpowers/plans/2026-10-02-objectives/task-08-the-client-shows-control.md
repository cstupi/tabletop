# Task 8: The client shows control

**Files:**
- Modify: `client/src/Core/MenuModel.cs`, `client/src/Screens/GameScreen.Play.cs`, `client/src/Table/MarkerNode.cs`, `client/src/Table/TableView.cs`, `client/src/Panels/TeamPanel.cs`
- Test: `tests/Tabletop.Client.Tests/ViewModelTests.cs` (the Godot layer is tested by running it)

**Interfaces:**
- Consumes: `SetObjectiveControl`, `Projection.Control` (Task 1); the menu override on the server (Task 2); `Palette.Team(int ordinal)`.
- Produces: `MenuModel.ForMarker(GameInfo game, Projection p, Guid markerId) : IReadOnlyList<MenuItem>`, replacing `ForMarker(Guid)`: "Remove" for a marker placed in play (as today), and for any objective marker, from setup or play, "Give to Team {n}" per team and "Make neutral"; `MarkerNode.Set(Marker m, Color? team = null)`; the team panel's line shows `held {n}` when the team controls any marker.

Spec: objectives spec → *What players see* (the client). Today `GameScreen.Play.ItemsFor` offers a marker menu only for play markers (`p.Markers`), so a setup objective has none.

- [ ] **Step 1: Write the failing test**

In `tests/Tabletop.Client.Tests/ViewModelTests.cs`, replace the line

```csharp
        Assert.IsType<RemoveMarker>(MenuModel.ForMarker(Samples.Token.Id).Single(i => i.Label == "Remove").Build(null));
```

with

```csharp
        var placed = Applier(new MarkerPlaced(Samples.Token)).Projection;
        Assert.IsType<RemoveMarker>(MenuModel.ForMarker(Game, placed, Samples.Token.Id).Single(i => i.Label == "Remove").Build(null));
```

and append a test inside the class:

```csharp
    [Fact]
    public void An_objective_from_setup_or_play_can_be_given_to_a_team_or_made_neutral_and_a_token_cannot()
    {
        var p = Applier(new MarkerPlaced(Samples.Token)).Projection;
        var setup = p.Board.Table.Markers.Single(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
        var items = MenuModel.ForMarker(Game, p, setup);
        Assert.Equal(["Give to Team 1", "Give to Team 2", "Make neutral"], items.Select(i => i.Label));    // no Remove: a setup row is not a play marker
        Assert.Equal(new SetObjectiveControl(setup, Samples.Team2), items[1].Build(null));
        Assert.Equal(new SetObjectiveControl(setup, null), items[2].Build(null));
        Assert.Equal(["Remove"], MenuModel.ForMarker(Game, p, Samples.Token.Id).Select(i => i.Label));      // a token holds nothing
        Assert.Empty(MenuModel.ForMarker(Game, p, Guid.CreateVersion7()));
    }
```

`Applier(...)` builds its own `Samples.Board`, and `p.Board` is that instance, so `setup` is an id the projection's board holds.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter ViewModelTests`
Expected: compile error, no `ForMarker(GameInfo, Projection, Guid)`.

- [ ] **Step 3: The menu**

In `client/src/Core/MenuModel.cs`, add `using Tabletop.Content;` and replace `ForMarker`:

```csharp
    /// <summary>
    /// Remove a marker placed in play (a setup row is not removable from here, as today). Any objective, from setup or play, can be given to a
    /// team or made neutral: the override of who controls it (objectives spec → The menu).
    /// </summary>
    public static IReadOnlyList<MenuItem> ForMarker(GameInfo game, Projection p, Guid markerId)
    {
        var items = new List<MenuItem>();
        if (p.Markers.ContainsKey(markerId)) items.Add(new("Remove", MenuInput.None, _ => new RemoveMarker(markerId)));
        var marker = p.Markers.GetValueOrDefault(markerId) ?? p.Board.Table.Markers.FirstOrDefault(m => m.Id == markerId);
        if (marker?.Kind != MarkerKind.Objective) return items;
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
            items.Add(new($"Give to Team {team.Ordinal}", MenuInput.None, _ => new SetObjectiveControl(markerId, team.Id)));
        items.Add(new("Make neutral", MenuInput.None, _ => new SetObjectiveControl(markerId, null)));
        return items;
    }
```

In `client/src/Screens/GameScreen.Play.cs`, `ItemsFor`, replace `return p.Markers.ContainsKey(id) ? MenuModel.ForMarker(id) : [];` with

```csharp
        return _session.Game is { } game ? MenuModel.ForMarker(game, p, id) : [];
```

- [ ] **Step 4: Run the client tests**

Run: `dotnet test tests/Tabletop.Client.Tests && ./scripts/check-client`
Expected: PASS; `check-client` prints nothing.

- [ ] **Step 5: The table and the team panel**

`client/src/Table/MarkerNode.cs`, `Set`: take the controlling team's colour:

```csharp
    /// <summary>A controlled objective takes its team's colour; a neutral one keeps the objective gold.</summary>
    public void Set(Marker m, Color? team = null)
```

and in the disc's material: `AlbedoColor = team ?? (m.Kind == MarkerKind.Objective ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.9f, 0.9f))`.

`client/src/Table/TableView.cs`, `Redraw`: replace the markers' `Sync` line with

```csharp
        Color? ControlColour(Marker m) => _session.Projection?.Control.TryGetValue(m.Id, out var holder) == true
            && _session.Game?.Teams.FirstOrDefault(t => t.Id == holder) is { } team ? Palette.Team(team.Ordinal) : null;
        Sync(_markers, markers, m => m.Id, m => { var n = new MarkerNode { MarkerId = m.Id }; n.Set(m, ControlColour(m)); return n; }, (MarkerNode n, Marker m) => n.Set(m, ControlColour(m)));
```

`TableView` already redraws on every applied batch (`_onEvents`), so a seizure recolours the disc when `round_ended` arrives.

`client/src/Panels/TeamPanel.cs`, `Refresh`: after `var score = ...`,

```csharp
        var held = s.Projection?.Control.Count(kv => kv.Value == Team.Id) ?? 0;
```

and in the text, after the score: `{(held == 0 ? "" : $"  held {held}")}`.

- [ ] **Step 6: Build and run the client by hand**

Run: `dotnet build && ./scripts/check-client && ./scripts/client`
Expected: 0 build errors. In a game with an objective on the table: right-click it, "Give to Team 1" turns the disc Team 1's colour and the Team 1 panel reads `held 1`; "Make neutral" turns it gold again; the log shows "Clay … Team 1 took …" and "… became neutral". A unit standing within 3" when a round is ended seizes it, and the log shows the `ended round n: Team n took …` line. (This step is the owner's checkpoint; the server side is covered by Tasks 2 to 5.)

- [ ] **Step 7: Commit**

```bash
git add client/src tests/Tabletop.Client.Tests
git commit -m "Show who controls each objective on the table and the team panels, and let the menu change it"
```
