# Same Calls, Part 2: Menus and Edits into Core — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The play menus' composition, the log's Undo to here, and every lobby and setup edit Godot builds inline are built by Core functions the nodes call, with no behaviour change.

**Architecture:** Continues part 1 (`2026-10-25-same-calls-1-core.md`, whose inventory rows these tasks name). `Menus` mirrors the browser's `play/menus.ts`; `LobbyEdits` and `SetupEdits` hold one function per control.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4.7 (the nodes only).

**Spec:** `docs/superpowers/specs/2026-10-25-same-calls-design.md`

**Build order:** part 1 (`-1-core.md`), this part (Tasks 4–6), part 3 (`-3-fixture.md`), part 4 (`-4-replay.md`), part 5 (`-5-ambush.md`).

## Global Constraints

- "No behaviour change in either client from the moves into Core; the existing client and viewer tests stay green."
- Core may reference only `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` and the BCL; no `using Godot` (`./scripts/check-client` fails on it).
- A moved builder keeps its exact output: the same record, fields, ids (fresh ids still `Guid.CreateVersion7()`), order of relocations and facings as the node produced.
- The rules engine lives only in `server/game/`; nothing here names a rule beyond what the node already did.
- Test-driven: each Core function has its xUnit test before it exists.
- The Godot nodes are hand tested (owner: "I am fine with needing to hand test godot"); `dotnet build client/Tabletop.csproj` and `./scripts/check-client` must pass after every task.

## Review Focus

- A watcher's menus: Godot guarded each open with `!IsWatching`; `Menus` returns `[]` for a watcher and the table menu still offers Leave (Task 4 test `A_watcher_gets_no_menu`, Step 5).
- Undo to here on a roll row lives in the row's expansion, on a block or line on the row; `UndoHere` decides only whether (Task 5).
- `Beside` keeps its spots: two draws, x first (Task 6 test `Beside_spots_follow_the_draws`).
- Volume edits carry the placement's whole list in order with the other volumes untouched (Task 6 test `Changing_one_volume_keeps_the_others`).
- Notes over 500 characters are clipped and an empty field clears them, as the `LineEdit`'s cap did (Task 6 test `Notes_are_clipped_and_empty_clears_them`).

---

### Task 4: `Menus` and `MenuModel.Token`

Which play menu opens with which items (rows 16, 17): `GameScreen.Play`'s `ItemsFor`, `OpenTeamMenu`, the roster's handler and `Token`, as one Core class that mirrors the browser's `play/menus.ts`.

**Files:**
- Create: `client/src/Core/Menus.cs`
- Modify: `client/src/Core/MenuModel.cs` (add `Token`)
- Modify: `client/src/Screens/GameScreen.Play.cs` (`_onPieceRight`, `_onTableRight`, `OpenTeamMenu`, the roster's `UnitMenuRequested`; delete `ItemsFor` and `Token`)
- Test: `tests/Tabletop.Client.Tests/MenusTests.cs`

**Interfaces:**
- Consumes: every `MenuModel.For*`, `MenuModel.KnownActionNames`, `MarkerLocks` (existing).
- Produces:
  ```csharp
  // MenuModel:
  public static MenuItem Token(Point3 at);   // "Drop a token here", Text: PlaceMarker(new Marker(new id, Token, at, Yaw 0, 1, label, chip))

  /// <summary>What every play menu reads: the browser's MenuContext.</summary>
  public sealed record MenuContext(GameInfo Game, Projection P, IReadOnlyList<GameEvent> Log, bool Watching, Guid? MyTeam, AskView? Mine,
      IReadOnlyList<GrantChoice> Grantable, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside, MarkerLocks Locks);
  public static class Menus
  {
      /// <summary>A model: boarding, then Shift the unit's items, else the model's and the frame's; else the marker's; [] for a watcher.</summary>
      public static IReadOnlyList<MenuItem> Piece(MenuContext ctx, Guid id, bool shift);
      /// <summary>The team's items, then each of its models off the table; [] for a watcher.</summary>
      public static IReadOnlyList<MenuItem> Team(MenuContext ctx, Guid teamId);
      /// <summary>MenuModel.ForRosterUnit for MyTeam; [] for a watcher.</summary>
      public static IReadOnlyList<MenuItem> Roster(MenuContext ctx, Guid unitId);
      /// <summary>The table's right-click: the token; [] for a watcher.</summary>
      public static IReadOnlyList<MenuItem> Table(MenuContext ctx, Point3 at);
  }
  ```
  `GameScreen.Play` builds the context once per open: `new MenuContext(game, p, _session.Log, _session.IsWatching, _session.MyTeamId, AskViewModel.Mine(p, game, _session.MyTeamId, _session.Settings.MutedWindowKinds), _session.Grantable, _session.DeployInside, Input.Locks)`.

- [ ] **Step 1: Write the failing tests**

Over `TableUx.After(TableUx.Deploy)`:

```csharp
[Fact] public void A_watcher_gets_no_menu() { /* Watching true: Piece, Team, Roster, Table all empty */ }
[Fact] public void A_model_menu_is_the_models_items_then_the_frames()
    => Assert.Equal(MenuModel.ForModel(p, m0, known).Concat(MenuModel.ForFrame(p)).Select(i => i.Label), Menus.Piece(ctx, m0, shift: false).Select(i => i.Label));
[Fact] public void Shift_gives_the_units_items() { /* labels equal MenuModel.ForUnit(p, Squad.Id, known, []) */ }
[Fact] public void Boarding_leads_a_model_menu_during_its_move() { /* Mine is a move Ask with Containment.Enterable [rhino]: first label "Board transport: …" */ }
[Fact] public void A_marker_menu_is_the_markers_items() { /* id of a table marker: labels equal MenuModel.ForMarker(game, p, id, locks) */ }
[Fact] public void The_team_menu_is_the_teams_items_then_its_models_off_the_table() { /* labels equal ForTeam + OffTable */ }
[Fact] public void The_table_menu_drops_a_token_there()
{
    var item = Assert.Single(Menus.Table(ctx, new Point3(5, 6, 0)));
    var place = Assert.IsType<PlaceMarker>(item.Build("smoke"));
    Assert.Equal((MarkerKind.Token, new Point3(5, 6, 0), "smoke", "chip", 1.0), (place.Marker.Kind, place.Marker.Position, place.Marker.Label, place.Marker.Visual.Builtin, place.Marker.Diameter));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~MenusTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement `MenuModel.Token`, `MenuContext`, `Menus`**

`Token` takes a table-frame `Point3`; `GameScreen.Play` passes `WorldFrame.FromGodot(at)`.

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: PASS.

- [ ] **Step 5: `GameScreen.Play` opens `Menus`**

`_onPieceRight`: `_menu?.Open(Menus.Piece(ctx, id, shift), at)` when the context builds (a game and a projection); `_onTableRight`: `[.. Menus.Table(ctx, WorldFrame.FromGodot(at)), _leave]` while not replaying (a watcher gets `[_leave]`, as now); `OpenTeamMenu`: `Menus.Team(ctx, teamId)`; the roster: `Menus.Roster(ctx, id)`. The `!_session.IsWatching` guards go: the context's `Watching` gives `[]`, and `PieceMenu.Open` of an empty list opens nothing (check `Open`; if it shows an empty popup, keep the guard there).

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/Menus.cs client/src/Core/MenuModel.cs client/src/Screens/GameScreen.Play.cs tests/Tabletop.Client.Tests/MenusTests.cs
git commit -m "Client: which play menu opens with which items is Core (Menus, MenuModel.Token)"
```

### Task 5: `CombatLog.UndoHere`

Which log row offers Undo to here (row 19).

**Files:**
- Modify: `client/src/Core/CombatLog.cs` (add `UndoHere`)
- Modify: `client/src/Hud/CombatLogPanel.cs` (both Undo to here sites)
- Test: `tests/Tabletop.Client.Tests/CombatLogRowsTests.cs` (add)

**Interfaces:**
- Produces: `public static bool UndoHere(LogEntry e, bool watching) => !watching && !e.Undone && e.Event.Payload is not Undone;` A roll row offers it in its expansion, a block or line row on the row itself; the panel keeps where it draws the button.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Undo_to_here_is_offered_on_a_live_row() { /* a LineEntry, not undone, not watching: true */ }
[Fact] public void Undo_to_here_is_not_offered_to_a_watcher_on_an_undone_row_or_on_an_undo_marker() { /* three false asserts */ }
[Fact] public void A_live_roll_offers_undo_to_here() { /* a RollEntry not undone: true */ }
```

Build the entries from `CombatLog.Build(TableUx.Events(...), names)` with an `Undone` marker in the log.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~CombatLogRowsTests"`
Expected: build FAIL, `UndoHere` not defined.

- [ ] **Step 3: Implement `UndoHere`**

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: PASS.

- [ ] **Step 5: `CombatLogPanel` asks Core**

The row site: `if (e is not RollEntry && CombatLog.UndoHere(e, _s.IsWatching)) AddButton(...)`. The roll expansion: `if (!CombatLog.UndoHere(r, _s.IsWatching)) return;` before the actions row.

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/CombatLog.cs client/src/Hud/CombatLogPanel.cs tests/Tabletop.Client.Tests/CombatLogRowsTests.cs
git commit -m "Client: which log row offers Undo to here is Core (CombatLog.UndoHere)"
```

### Task 6: `LobbyEdits` and `SetupEdits`

The lobby and setup edits the panels and the terrain card build inline (rows 21, 22, 25, 27–39, 42–47). Each function is what the control sends today; the panels call it.

**Files:**
- Create: `client/src/Core/LobbyEdits.cs`, `client/src/Core/SetupEdits.cs`
- Modify: `client/src/Panels/LobbyPanel.cs`, `client/src/Panels/SetupToolbar.cs` (delete `Beside`), `client/src/Hud/TerrainCard.cs`, `client/src/Screens/HomeScreen.cs`
- Test: `tests/Tabletop.Client.Tests/LobbyEditsTests.cs`, `tests/Tabletop.Client.Tests/SetupEditsTests.cs`

**Interfaces:**
- Consumes: `RulesOptions.MapOf`, `BuiltinContent.Place`, `SetupLimits.FreeSpot`, `ZoneStyles.ShapesOf`, `LayoutChoices.WithTerrain`, `TextLimits.Notes` (existing).
- Produces:
  ```csharp
  public static class LobbyEdits
  {
      public static PickTeam Watch();                                    // PickTeam(null)
      public static CreateTeam CreateTeam();
      public static StartSetup StartSetup();
      public static EndSetup EndSetup();
      /// <summary>Apply configuration: the trimmed name, the game's properties hash, WaitsBetweenSteps false, the options map without defaults.</summary>
      public static SetConfiguration Configure(GameInfo game, string name, SchedulerShape shape, Enforcement enforcement,
          RulesOptionCatalog catalog, IReadOnlyDictionary<string, string> chosen);
      /// <summary>The army chosen in Create game, picked once the creator is connected: no team named.</summary>
      public static PickArmy ArmyFromCreate(Guid armyId);
  }
  public static class SetupEdits
  {
      /// <summary>Beside the table: x = W + 4 + 8·⌊3r⌋, y = 4 + 8·⌊4r'⌋, z 0, from two draws in [0, 1).</summary>
      public static Point3 Beside(TableSize size, Func<double> random);
      public static SetTableSize TableSize(double width, double length);
      public static SetTexture Texture(string builtin);
      public static AddTerrain AddBuiltin(string name, TableSize size, Func<double> random);       // BuiltinContent.Place(name, Beside, Yaw 0)
      public static AddTerrain AddAsset(Guid assetId, IReadOnlyList<Volume> volumes, TableSize size, Func<double> random);   // new ids, scale 1, Yaw 0
      public static AddSetupMarker Objective(TableState draft);    // Objective, FreeSpot(size, markers, 3), diameter 3, no label, objective-ring
      public static AddSetupMarker Token(TableState draft);        // Token, FreeSpot(size, markers, 1), diameter 1, "token", chip
      public static SetZoneStyle Zones(string style);              // ShapesOf(style)
      public static SwapZones SwapZones(int teamOrdinalA, int teamOrdinalB);
      public static ReplaceDraft ClearTerrain(TableState draft);   // draft with no terrain
      public static ReplaceDraft Random(TableState draft, RandomTable laid);   // LayoutChoices.WithTerrain
      public static ReplaceDraft Load(TableState layout);
      public static ScaleTerrain Scale(Guid placementId, double scale);
      public static SwapTerrainMesh SwapBuiltin(Guid placementId, string builtin);
      public static SwapTerrainMesh SwapAsset(Guid placementId, Guid assetId);
      public static RemoveTerrain RemoveTerrain(Guid placementId);
      public static RemoveSetupMarker RemoveMarker(Guid markerId);
      /// <summary>Reset volumes: the given volumes under new ids.</summary>
      public static OverrideTerrainVolumes ResetVolumes(Guid placementId, IReadOnlyList<Volume> volumes);
      /// <summary>The placement's whole volume list with one volume changed.</summary>
      public static OverrideTerrainVolumes ChangeVolume(TerrainPlacement piece, Guid volumeId, Func<Volume, Volume> change);
      public static Func<Volume, Volume> WithKind(VolumeKind kind);
      public static Func<Volume, Volume> WithFootprintA(double value);   // a circle sets A and B
      public static Func<Volume, Volume> WithFootprintB(double value);   // a rect's B
      public static Func<Volume, Volume> WithBottom(double bottom);
      public static Func<Volume, Volume> WithTop(double top);
      public static Func<Volume, Volume> WithDeclaration(TerrainDeclarations flag, bool on);
      public static Func<Volume, Volume> WithNotes(string text);          // clipped to TextLimits.Notes; "" is null
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
// LobbyEditsTests
[Fact] public void Configure_trims_the_name_and_drops_default_options()
{
    var edit = LobbyEdits.Configure(game, "  Friday  ", SchedulerShape.PlayerTurn, Enforcement.Strict, catalog, chosen);
    Assert.Equal(("Friday", game.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Strict, false), (edit.Name, edit.PropertiesHash, edit.SchedulerShape, edit.Enforcement, edit.WaitsBetweenSteps));
    Assert.Equal(RulesOptions.MapOf(catalog, chosen), edit.OptionalRules);
}
[Fact] public void Watch_is_pick_team_with_no_team() => Assert.Equal(new PickTeam(null), LobbyEdits.Watch());
[Fact] public void The_create_games_army_names_no_team() => Assert.Equal(new PickArmy(army, null), LobbyEdits.ArmyFromCreate(army));

// SetupEditsTests
[Fact] public void Beside_spots_follow_the_draws()
{
    var draws = new Queue<double>([0.0, 0.0, 0.99, 0.99]);
    Assert.Equal(new Point3(76, 4, 0), SetupEdits.Beside(new TableSize(72, 48), draws.Dequeue));
    Assert.Equal(new Point3(92, 28, 0), SetupEdits.Beside(new TableSize(72, 48), draws.Dequeue));
}
[Fact] public void A_built_in_piece_is_placed_beside_the_table_at_yaw_zero() { /* AddBuiltin("ruin", size, draws): Placement equals BuiltinContent.Place("ruin", Beside, Yaw 0) but its id */ }
[Fact] public void An_asset_piece_takes_each_volume_under_a_new_id() { /* AddAsset: VisualRef.OfAsset(id), scale 1, volumes' Volume values in order, ids distinct and new */ }
[Fact] public void Markers_go_to_a_free_spot() { /* Objective: FreeSpot(size, markers, 3), diameter 3, Label null, "objective-ring"; Token: FreeSpot(.., 1), "token", "chip" */ }
[Fact] public void Zones_send_the_styles_shapes() => Assert.Equal(ZoneStyles.ShapesOf("frontline"), SetupEdits.Zones("frontline").Shapes);
[Fact] public void Clear_terrain_keeps_everything_but_the_terrain() { /* Draft with Terrain [] and the same markers, zones, size */ }
[Fact] public void Changing_one_volume_keeps_the_others() { /* two volumes; ChangeVolume(piece, v2.Id, WithTop(5)): v1 unchanged, v2.Top 5, same ids and order */ }
[Fact] public void A_circle_footprint_change_sets_both_halves() { /* WithFootprintA(3) on a circle: A 3, B 3; on a rect: A 3, B kept */ }
[Fact] public void A_declaration_toggles_its_flag() { /* WithDeclaration(Cover, true) then false */ }
[Fact] public void Notes_are_clipped_and_empty_clears_them() { /* WithNotes(new string('x', 600)).Notes length TextLimits.Notes; WithNotes("") Notes null */ }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~LobbyEditsTests|FullyQualifiedName~SetupEditsTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement `LobbyEdits` and `SetupEdits`**

`Beside`: `new(size.Width + 4 + Math.Floor(random() * 3) * 8, 4 + Math.Floor(random() * 4) * 8, 0)`, the x draw first.

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: PASS.

- [ ] **Step 5: The panels and the card call Core**

- `LobbyPanel`: Watch, Create team, Start setup, End setup, Apply configuration (`LobbyEdits.Configure(game, name.Text, shape.Selected == 0 ? AlternatingActivation : PlayerTurn, (Enforcement)enforcement.Selected, s.Options, chosen)`; the name check stays before it).
- `HomeScreen.PickWhenConnected`: `session.SendEditAsync(LobbyEdits.ArmyFromCreate(id))`.
- `SetupToolbar`: Set size, texture, Add built-in (`SetupEdits.AddBuiltin(terrainNames[terrain.Selected], SizeNow(s), Random.Shared.NextDouble)`), Add my terrain (`SetupEdits.AddAsset(asset.Id, volumes.Select(v => v.Volume).ToList(), SizeNow(s), Random.Shared.NextDouble)`), Random table, Clear terrain, Load layout, Add objective and Add token (`SetupEdits.Objective(s.Draft!)`, `SetupEdits.Token(s.Draft!)`: `SetupLimits.CannotAddMarker` gates both buttons on a draft), Set zones, Swap.
- `TerrainCard`: Scale, Swap mesh, Swap for my asset, Reset volumes (`SetupEdits.ResetVolumes(t.Id, builtin ? BuiltinContent.VolumesOf(name).Select(v => v.Volume).ToList() : (await assetVolumes).Select(v => v.Volume).ToList())`), Remove, Remove marker, and every volume field through `ChangeVolume` with the `With*` change.

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/LobbyEdits.cs client/src/Core/SetupEdits.cs client/src/Panels/LobbyPanel.cs client/src/Panels/SetupToolbar.cs client/src/Hud/TerrainCard.cs client/src/Screens/HomeScreen.cs tests/Tabletop.Client.Tests/LobbyEditsTests.cs tests/Tabletop.Client.Tests/SetupEditsTests.cs
git commit -m "Client: lobby and setup edits are built in Core (LobbyEdits, SetupEdits)"
```

Part 2 is done when every row of part 1's inventory names a Core builder on Godot's side. Part 3 (`2026-10-25-same-calls-3-fixture.md`) follows.
