# Task 13: Create-game form

**Wave:** 2 · **Depends on:** — · **Model:** haiku

**Files:**
- Modify: `client/src/Screens/HomeScreen.cs` (`CreateDialog`: drop the *Wait between steps* checkbox; send `WaitsBetweenSteps = false`)
- Modify: `client/src/Panels/LobbyPanel.cs` (the lobby's configuration: drop the same checkbox; `SetConfiguration` sends `false`)

**Interfaces:**
- Consumes: `CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)` (`client/src/Core/ManagementDtos.cs`, unchanged);
  `SetConfiguration(…, bool WaitsBetweenSteps)` (`Tabletop.Protocol`, unchanged).
- Produces: nothing.

Spec: *Flow between steps*. "`WaitsBetweenSteps` … stays as a per-game flag for development and is not offered to
players: the client's create-game form drops it and always sends `false`." The flag stays on the wire, in
`CreateGameRequest` and in the schema. Godot UI: no unit test.

- [ ] **Step 1: Remove the checkbox**

In `client/src/Screens/HomeScreen.cs`, `CreateDialog`, delete this line:

```csharp
        var waits = new CheckBox { Text = "Wait between steps" }; form.AddChild(waits);
```

- [ ] **Step 2: Always send false**

In the same method, replace:

```csharp
            var game = await session.Management.CreateGame(new CreateGameRequest(name.Text, packs[pack.Selected].ContentHash,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), waits.ButtonPressed));
```

with:

```csharp
            // WaitsBetweenSteps is a development flag (spec: in-game UX → Flow between steps); players never get the "Continue" stops.
            var game = await session.Management.CreateGame(new CreateGameRequest(name.Text, packs[pack.Selected].ContentHash,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), WaitsBetweenSteps: false));
```

- [ ] **Step 3: The lobby's configuration**

In `client/src/Panels/LobbyPanel.cs`, delete:

```csharp
        var waits = new CheckBox { Text = "Wait between steps", ButtonPressed = game.WaitsBetweenSteps }; config.AddChild(waits);
```

and in the *Apply configuration* button's `SetConfiguration`, replace the last argument `waits.ButtonPressed` with
`WaitsBetweenSteps: false`.

- [ ] **Step 4: Build and check**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && grep -n "waits" client/src/Screens/HomeScreen.cs client/src/Panels/LobbyPanel.cs`
Expected: build succeeds with no new warnings; `check-client` prints nothing; the `grep` prints nothing.

- [ ] **Step 5: Look**

Run `./scripts/client`, log in, open *Create game*: the form shows name, properties set, scheduler shape,
enforcement and army, and no *Wait between steps*; the lobby's configuration shows none either. Create a game and
check the row:

```bash
docker compose exec -T postgres psql -U tabletop -d tabletop -c "select name, waits_between_steps from management.game order by id desc limit 1;"
```

Expected: the new game with `waits_between_steps = f`.

- [ ] **Step 6: Commit**

```bash
git add client/src/Screens/HomeScreen.cs client/src/Panels/LobbyPanel.cs
git commit -m "Stop offering Wait between steps on the create-game form and in the lobby; always send false" -m "Co-Authored-By: Claude Haiku <noreply@anthropic.com>"
```
