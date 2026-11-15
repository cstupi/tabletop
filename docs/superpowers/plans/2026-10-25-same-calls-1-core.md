# Same Calls, Part 1: Godot's Builders into Core — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every outbound socket message the Godot client sends is built by a Godot-free function in `client/src/Core/`, unit-tested in `tests/Tabletop.Client.Tests`, with the nodes calling it and behaving as before.

**Architecture:** The inventory below lists every message each client sends and where it is built. Where Godot builds one in `src/Hud`, `src/Table`, `src/Panels` or `src/Screens`, this part moves the building into a Core function (`AskAnswers`, `PiecePick`, `DeployView.Place`, `HandMoves`, `Menus`, `CombatLog.UndoHere`, `GameOverView.Answer`, `LobbyEdits`, `SetupEdits`) and the node calls it. Part 3 writes the agreement fixture from these functions, part 4 replays it through the browser, part 5 is the browser's Ambush stack test.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4.7 (the nodes only).

**Spec:** `docs/superpowers/specs/2026-10-25-same-calls-design.md`

**Parts and build order:**
1. `2026-10-25-same-calls-1-core.md` (this file): the inventory, then the decision box's answers, picks, placements and drops into Core (Tasks 1–3).
2. `2026-10-25-same-calls-2-edits.md`: menus, Undo to here, lobby and setup edits into Core (Tasks 4–6). Needs part 1.
3. `2026-10-25-same-calls-3-fixture.md`: the call logs, the one normalisation in C# and TypeScript, `CallsViewerFixtureTests` writing `viewer/tests/fixtures/calls.json` (Tasks 7–9). Needs parts 1 and 2.
4. `2026-10-25-same-calls-4-replay.md`: the browser builders the replay calls, `calls.agreement.test.ts`, the agreement fixes, the docs rule (Tasks 10–13). Needs part 3.
5. `2026-10-25-same-calls-5-ambush.md`: the browser Ambush stack test in `viewer/tests/e2e` (Task 14). Independent of parts 1–4; built last so a failure it finds is fixed against agreed builders.

## Global Constraints

- "No behaviour change in either client from the moves into Core; the existing client and viewer tests stay green."
- Core may reference only `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` and the BCL; no `using Godot` (`./scripts/check-client` fails on it).
- A moved builder keeps its exact output: the same record, fields, ids (fresh ids still `Guid.CreateVersion7()`), order of relocations and facings as the node produced.
- The rules engine lives only in `server/game/`; nothing here names a rule beyond what the node already did.
- Test-driven: each Core function has its xUnit test before it exists.
- The Godot nodes are hand tested (owner: "I am fine with needing to hand test godot"); `dotnet build client/Tabletop.csproj` and `./scripts/check-client` must pass after every task.

## Review Focus

- A disembarking model's facing: Godot answers `MoveRuler.ExitYawAt` at the drop point, never the ghost's own facing; `AskAnswers.FacingOf` must keep that (Task 1 test `Facing_of_a_disembarking_model_is_the_exit_yaw_at_its_drop`).
- A team acted for: `AnswerAsk.TeamId` is the team the Ask was rendered for (`_answering`), not the acting team at send time; `AskAnswers.Answer` takes it as a parameter and DecisionBox keeps passing `_answering` (Task 1).
- Relocation order: `Destinations.Moves` follows `MoveRegion.ModelIds`; a deploy's `MoveModels` follows the ghosts' insertion order (the layout's); a hand move follows `drag.ModelIds` (Tasks 1 and 3 assert order).
- A click on a piece two options name sends nothing (`ChosenByClick` null), and a pick of one at `Max == 1` answers at once (Tasks 1 and 2).
- A hand move's `From` is where each model was grabbed (`drag.Grabs`, the node as drawn, a pending drop included), not the projection's placement (Task 3 test `A_hand_move_goes_from_each_grab_to_its_landing`).

---

## The inventory

Every outbound message, where each client builds it, and whether Godot's site is Godot-free. "Core" is `client/src/Core/`; Godot-dependent sites are under `src/Hud`, `src/Table`, `src/Panels`, `src/Screens`. Browser paths are under `viewer/src/lib/`. The "Part 1 task" column is the task that moves a Godot-dependent site into Core; "—" means it is Core already.

| # | Message | Godot builds it in | Godot layer | Browser builds it in | Part 1 task |
| --- | --- | --- | --- | --- | --- |
| 1 | `AnswerAsk(Chosen)`: an option button, its 1–9 key, a key after a unit click narrows the list | `Hud/DecisionBox.cs` `RenderChoose`, `Answer` (`new AnswerAsk(ask.Id, answer, _answering)`) | Hud | `play/decisionBox.ts` `#renderChoose`, `#answer` | 1 |
| 2 | `AnswerAsk(Chosen)`: a table click on a piece one option names | `DecisionBox.RenderChoose` `_onTarget`, `DecisionBox.Resolve` | Hud | `decisionBox.ts` `handlers.onTarget`, `#resolve` | 1 |
| 3 | `AnswerAsk(Passed)`: Pass | `DecisionBox.RenderAsk` | Hud | `decisionBox.ts` `#renderAsk` | 1 |
| 4 | `AnswerAsk(Confirmed)`: Continue, End the round | `DecisionBox.RenderAsk` | Hud | `decisionBox.ts` `#renderAsk` | 1 |
| 5 | `AnswerAsk(TokensSpent)` | `DecisionBox.RenderSpend` (`AskViewModel.SpendableBy` is Core) | Hud | `decisionBox.ts` `#renderSpend` | 1 |
| 6 | `AnswerAsk(SelectedPieces)`: picks, the deploy pick, the Ambusher pick | `Hud/DecisionBox.Picks.cs` `RenderPick` (`DeployView.Pickable`, `OffTable` are Core) | Hud | `decisionBox.ts` `#renderPick` | 2 |
| 7 | `AnswerAsk(Assigned)` | `DecisionBox.Picks.RenderTargets` over `TargetAssignment` | Core answer, Hud flow | `decisionBox.ts` `#renderTargets`, `play/targetAssignment.ts` | — |
| 8 | `AnswerAsk(Allocated)` | `DecisionBox.Picks.RenderCasualties` over `CasualtyPick` | Core answer, Hud flow | `decisionBox.ts` `#renderCasualties`, `play/casualtyPick.ts` | — |
| 9 | `AnswerAsk(Destinations)`: a move's commit and stop, a disembark, a Spawn or Split shelf placement | `Hud/DecisionBox.Move.cs` `RenderMove.Relocate`, `YawOf`, `NodeYaw` | Hud | `play/decisionBox.move.ts` `renderMove` `relocate` | 1 |
| 10 | `AnswerAsk(Destinations)`: embark by clicking a container | `DecisionBox.Move.OfferEmbark` | Hud | `decisionBox.move.ts` `offerEmbark` | 1 |
| 11 | `AnswerAsk(Destinations)`: the menu's Board transport | `Core/MenuModel.ForBoarding` | Core | `play/menuModel.ts` `forBoarding` | 1 (shares `Embark`) |
| 12 | `AnswerAsk(Chosen)`: game over End or Continue | `Hud/EndMessage.Answer` | Hud | `play/GameOverModal.svelte` `choose` | 1 |
| 13 | `MoveModels`: deployment placement | `DecisionBox.Move.PlaceDeploy` (`DeployView.Layout` is Core) | Hud | `decisionBox.move.ts` `showDeploy` `place` (`play/deployView.ts` `layout`) | 3 |
| 14 | `MoveModels`: a hand move | `Table/TableInput.cs` `OnLeftUp` | Table | `play/playDrag.ts` `#handMove`, `play/handMove.ts` `handMoves` | 3 |
| 15 | `MoveMarker`: a play marker's drag | `TableInput.OnLeftUp` | Table | `playDrag.ts` `onDragEnd`, `handMove.ts` `markerMove` | 3 |
| 16 | Menu commands: `RecordAction`, `ClearActionRecord`, `SetCondition` (Set aside, Return to deployment included), `SetWoundsThisActivation`, `AddGrant`, `RemoveGrant`, `SetWounds`, `SetSpellTokens`, `MoveModels` (shelf, inside, back on the table, into and out of a transport), `SetScore`, `SetActiveTeam`, `SetRound`, `RemoveMarker`, `SetObjectiveControl`, `AbandonSequence` | items `Core/MenuModel`; which menu opens `Screens/GameScreen.Play.cs` `ItemsFor`, `OpenTeamMenu`, the roster's handler; sent by `Panels/PieceMenu` `item.Build(input)` | Core items, Screens composition | `play/menuModel.ts`, `play/menus.ts` `pieceMenu`, `teamMenu`, `rosterMenu`, `choose` | 4 |
| 17 | `PlaceMarker`: Drop a token here | `GameScreen.Play.Token` | Screens | `menuModel.ts` `token`, `menus.ts` `tableMenu` | 4 |
| 18 | `Undo`: the Undo button, Ctrl+Z | `Hud/UndoButton.Send` → `Core/Session.UndoToAsync` (`UndoPlan` is Core) | Core | `play/undo.ts` `UndoPrediction.undoTo`, `UndoButton.svelte` | — |
| 19 | `Undo`: a log row's Undo to here | `Hud/CombatLogPanel` (the condition inline) → `Session.UndoToAsync` | Hud condition | `play/CombatLogPanel.svelte`, `play/logRows.ts` `undoHere` | 5 |
| 20 | `PickTeam`, `LeaveTeam`: the lobby's team buttons | `Core/LobbyView.Teams` (`TeamChoice.Edit`), sent by `Panels/LobbyPanel` | Core | `lobby/lobbyView.ts` `teamChoices` (via `lobbyPage.ts` `teamCards`) | — |
| 21 | `PickTeam(null)`: Watch | `LobbyPanel` inline | Panels | `lobby/TeamCards.svelte` inline | 6 |
| 22 | `CreateTeam` | `LobbyPanel` inline | Panels | `lobby/LobbyPage.svelte` inline | 6 |
| 23 | `PickTeam(team, participant)`: the creator places a player | `Core/LobbyView.Place` | Core | `lobbyView.ts` `place` | — |
| 24 | `PickArmy(army, team)`: an army picker | `Core/LobbyView` `ArmySeat.Pick` (`LobbyPanel`, `WaitingPanel`) | Core | `lobbyView.ts` `pickArmy` | — |
| 25 | `PickArmy(army)`: the army chosen in Create game, once connected | `Screens/HomeScreen.cs` inline | Screens | `game/GamePage.svelte` inline | 6 |
| 26 | `PickTeam`: Join from the waiting panel | `Core/WaitingView` (`WaitingLine.Join.Edit`) | Core | `play/waiting.ts` `waitingLines` | — |
| 27 | `SetConfiguration` | `LobbyPanel` inline (`RulesOptions.MapOf` is Core) | Panels | `lobby/rulesOptions.ts` `configurationOf` | 6 |
| 28 | `StartSetup` | `LobbyPanel` inline | Panels | `LobbyPage.svelte` inline | 6 |
| 29 | `EndSetup` | `LobbyPanel` inline | Panels | `game/header.ts` `headerOf` | 6 |
| 30 | `SetTableSize` | `Panels/SetupToolbar` inline | Panels | `setup/SetupTools.svelte` inline | 6 |
| 31 | `SetTexture` | `SetupToolbar` inline | Panels | `SetupTools.svelte` inline | 6 |
| 32 | `AddTerrain` (built-in, beside the table) | `SetupToolbar` inline (`BuiltinContent.Place` Core, `Beside` in the panel) | Panels | `SetupTools.svelte`, `setup/catalog.ts` `placeBuiltin`, `setup/limits.ts` `beside` | 6 |
| 33 | `AddTerrain` (my asset) | `SetupToolbar.AddLibraryTerrain` inline | Panels | `SetupTools.svelte`, `catalog.ts` `placeAsset` | 6 |
| 34 | `ReplaceDraft`: Random table | `SetupToolbar` inline (`LayoutChoices.WithTerrain` Core) | Panels | `setup/layouts.ts` `randomEdit` | 6 |
| 35 | `ReplaceDraft`: Clear terrain | `SetupToolbar.ClearTerrain` inline | Panels | `setup/edits.ts` `clearTerrain` | 6 |
| 36 | `ReplaceDraft`: Load layout | `SetupToolbar.LoadLayout` inline | Panels | `layouts.ts` `loadEdit` | 6 |
| 37 | `AddSetupMarker`: objective, token | `SetupToolbar` inline (`SetupLimits.FreeSpot` Core) | Panels | `edits.ts` `objective`, `token` | 6 |
| 38 | `SetZoneStyle` | `SetupToolbar` inline | Panels | `SetupTools.svelte` inline | 6 |
| 39 | `SwapZones` | `SetupToolbar` inline | Panels | `SetupTools.svelte` inline | 6 |
| 40 | `MoveTerrain`: Turn left/right 15°, Set facing…, the card's facing field and ⟲/⟳ | `Core/MenuModel.ForSetupPiece`, `TurnInDraft`; `Hud/TerrainCard` calls `TurnInDraft` | Core | `edits.ts` `setupMenu`, `turnInDraft`; `setup/TerrainCard.svelte` | — |
| 41 | `RemoveTerrain`, `RemoveSetupMarker`: the menu's Remove, Delete or Backspace | `Core/MenuModel.RemoveFromDraft` | Core | `edits.ts` `removeFromDraft` | — |
| 42 | `ScaleTerrain` | `Hud/TerrainCard` inline | Hud | `TerrainCard.svelte` inline | 6 |
| 43 | `SwapTerrainMesh` (built-in, my asset) | `TerrainCard` inline | Hud | `TerrainCard.svelte` inline | 6 |
| 44 | `RemoveTerrain`: the card's Remove | `TerrainCard` inline | Hud | `TerrainCard.svelte` inline | 6 |
| 45 | `RemoveSetupMarker`: the card's Remove marker | `TerrainCard` inline | Hud | `TerrainCard.svelte` inline | 6 |
| 46 | `OverrideTerrainVolumes`: Reset volumes | `TerrainCard` inline | Hud | `TerrainCard.svelte` `resetVolumes` inline | 6 |
| 47 | `OverrideTerrainVolumes`: one volume field (kind, footprint, heights, a declaration, notes) | `TerrainCard.RenderVolumeEdits` inline | Hud | `edits.ts` `overrideVolume`, `withKind`, `withFootprint`, `withBottom`, `withTop`, `withDeclaration`, `withNotes` | 6 |
| 48 | `MoveTerrain`, `MoveSetupMarker`: a setup drop | `TableInput.OnLeftUp` | Table | `setup/drag.ts` `SetupDrag.end` | 3 |

Not in the agreement fixture:

- `SightQuery` (Godot `Table/SightLines`, browser `play/sightCheck.ts`): it changes nothing in the game and carries a fresh query id; when each side asks again is `SightKey`, ported and tested on both sides.
- `Connect`, `Reauth`: the connection's plumbing, not a player's call.

Sent by one client only:

- Godot only: `Presence` (cursor, held pieces, the tape measure's `Measurement`; `Table/TableInput.OnPresenceTick`) and `Ping` (`Table/TableInput.Keys`). The browser sends neither; the tape measure is a spec non-goal.
- Browser only, as an input path: Q or E on a selected setup terrain piece that is not held sends `MoveTerrain` through `turnInDraft` (±15°, Shift ±30°; `setup/SetupPage.svelte` `onKey`). Godot turns a piece only while held and sends at the drop (row 48). The message is the same kind as row 40 and goes through the same builder, which the fixture covers; Shift's 30° has no Godot counterpart.
- Neither client sends `SetDice`: dice are never edited by a player.

---

### Task 1: `AskAnswers` and `GameOverView.Answer`

The decision box's answers (rows 1–5, 9–12): the `AnswerAsk` wrapper, the click resolution, the token spend, the move's relocations and facings, the embark.

**Files:**
- Create: `client/src/Core/AskAnswers.cs`
- Modify: `client/src/Hud/DecisionBox.cs` (`Answer`, `Resolve`, `RenderChoose` `_onTarget`, `RenderSpend` `Spend`)
- Modify: `client/src/Hud/DecisionBox.Move.cs` (`Relocate`, `YawOf`, `OfferEmbark`)
- Modify: `client/src/Core/MenuModel.cs` (`ForBoarding` builds through `AskAnswers.Embark`)
- Modify: `client/src/Core/GameOverView.cs`, `client/src/Hud/EndMessage.cs` (`Answer`)
- Test: `tests/Tabletop.Client.Tests/AskAnswersTests.cs`, `tests/Tabletop.Client.Tests/GameOverViewTests.cs`

**Interfaces:**
- Consumes: `MoveRuler.ExitYawAt(ModelBody, PlacedModel, double, Point2, IReadOnlyList<WorldVolume>, TableSize)`, `DecisionView.Number`, `DecisionView.ChoicesOf` (all existing).
- Produces:
  ```csharp
  namespace Tabletop.Client.Core;
  /// <summary>What a disembark's facing is judged against: the Ask's exit distance and the board's volumes and size.</summary>
  public sealed record ExitSite(double Within, IReadOnlyList<WorldVolume> Volumes, TableSize Size);
  public static class AskAnswers
  {
      public static AnswerAsk Answer(Ask ask, AskAnswer answer, Guid? team);
      public static Guid? Resolve(Projection p, Guid clicked, IReadOnlyCollection<Guid> ids);
      public static Chosen? ChosenByClick(Options options, Projection p, Guid clicked);
      public static TokensSpent Spent(IReadOnlyDictionary<Guid, int> values);
      public static Yaw FacingOf(Projection p, Guid modelId, Yaw? ghostYaw, Point2 dropped, ExitSite? exit);
      public static Destinations Move(Projection p, MoveRegion legal, IReadOnlyDictionary<Guid, Point3> proposed,
          IReadOnlyDictionary<Guid, Yaw> ghostYaws, bool continues, ExitSite? exit);
      public static Destinations Embark(Projection p, MoveRegion legal, Guid container);
  }
  // GameOverView:
  public static AnswerAsk Answer(GameOverModel model, string optionId, Guid? team);
  ```

Semantics, each as the node does it now:
- `Answer`: `new AnswerAsk(ask.Id, answer, team)`.
- `Resolve`: `DecisionBox.Resolve` moved verbatim: the clicked id if listed; else its model's unit if listed; else, for a clicked unit, the listed model of that unit.
- `ChosenByClick`: the options whose `Pieces` `Resolve` the click; exactly one gives `new Chosen(its id)`, else null.
- `Spent`: the entries with a value above 0.
- `FacingOf`: `YawOf` moved. On the table: `ghostYaw ?? placement yaw`. Inside a container on the table: `MoveRuler.ExitYawAt(body, new PlacedModel(containerBody, containerPlacement), exit.Within, dropped, exit.Volumes, exit.Size) ?? container yaw`, the ghost's yaw ignored; `exit` null there gives the container's yaw. On the shelf: `ghostYaw ?? new Yaw(0)`. Anything else: `new Yaw(0)`.
- `Move`: one `ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(proposed[id], FacingOf(p, id, ghostYaws.TryGetValue(id, out var y) ? y : null, proposed[id].Horizontal, exit))))` per `legal.ModelIds`, in that order; `new Destinations(moves, continues)`. A missing `ghostYaws` entry is a null ghost yaw.
- `Embark`: one `ModelRelocation(id, p.Models[id].Location, new Inside(container))` per `legal.ModelIds`; `Continues` false.

- [ ] **Step 1: Write the failing tests**

In `AskAnswersTests.cs`, over `TableUx.After(TableUx.Deploy)` (Squad at (10,10), (12,10), (14,10); Orcs at (40,30)):

```csharp
[Fact] public void Answer_names_the_team_it_was_rendered_for()
    => Assert.Equal(new AnswerAsk(ask.Id, new Chosen("hold"), TableUx.T2), AskAnswers.Answer(ask, new Chosen("hold"), TableUx.T2));

[Fact] public void Resolve_names_the_clicked_id_its_unit_or_the_sets_model_of_the_unit()
{
    var p = TableUx.After(TableUx.Deploy); var m0 = TableUx.ModelOf(TableUx.Squad);
    Assert.Equal(m0, AskAnswers.Resolve(p, m0, [m0]));
    Assert.Equal(TableUx.Squad.Id, AskAnswers.Resolve(p, m0, [TableUx.Squad.Id]));
    Assert.Equal(m0, AskAnswers.Resolve(p, TableUx.Squad.Id, [m0]));
    Assert.Null(AskAnswers.Resolve(p, TableUx.ModelOf(TableUx.Orcs), [m0]));
}

[Fact] public void Click_on_a_piece_one_option_names_chooses_it()      // options "a" pieces [Squad], "b" pieces [Orcs]
    => Assert.Equal(new Chosen("b"), AskAnswers.ChosenByClick(options, p, TableUx.ModelOf(TableUx.Orcs)));

[Fact] public void Click_on_a_piece_two_options_name_chooses_nothing() // "a" and "b" both name Squad
    => Assert.Null(AskAnswers.ChosenByClick(both, p, TableUx.ModelOf(TableUx.Squad)));

[Fact] public void Spent_drops_models_that_spend_nothing()
    => Assert.Equal(new Dictionary<Guid, int> { [m1] = 2 }, AskAnswers.Spent(new Dictionary<Guid, int> { [m0] = 0, [m1] = 2 }).Counts);

[Fact] public void Move_follows_the_regions_model_order_from_where_each_stands()
{
    // legal.ModelIds = [m2, m0]; proposed m0 (11,15), m2 (15,15); ghost yaw for m2 only, 90°
    var d = AskAnswers.Move(p, legal, proposed, new Dictionary<Guid, Yaw> { [m2] = new(90) }, continues: false, exit: null);
    Assert.Equal([m2, m0], d.Moves.Select(m => m.ModelId));
    Assert.Equal(p.Models[m2].Location, d.Moves[0].From);
    Assert.Equal(new OnTable(new Placement(new Point3(15, 15, 0), new Yaw(90))), d.Moves[0].To);
    Assert.Equal(new OnTable(new Placement(new Point3(11, 15, 0), new Yaw(0))), d.Moves[1].To);   // its placement's yaw
    Assert.False(d.Continues);
}

[Fact] public void Stop_sends_the_ghosts_as_they_stand_and_continues()
    => Assert.True(AskAnswers.Move(p, legal, proposed, new Dictionary<Guid, Yaw>(), continues: true, exit: null).Continues);

[Fact] public void Facing_of_a_disembarking_model_is_the_exit_yaw_at_its_drop()
{
    // m1 moved Inside m0 (ModelsMoved), m0 at (10,10); dropped at (13,10), within 6
    var site = new ExitSite(6, [], new TableSize(72, 48));
    var expected = MoveRuler.ExitYawAt(body1, new PlacedModel(body0, at0), 6, new Point2(13, 10), [], site.Size);
    Assert.Equal(expected, AskAnswers.FacingOf(p, m1, new Yaw(135), new Point2(13, 10), site));   // the ghost's 135° ignored
    Assert.Equal(at0.Yaw, AskAnswers.FacingOf(p, m1, null, new Point2(40, 40), site));           // no fitting facing: the container's
}

[Fact] public void Facing_of_a_shelf_model_is_its_ghosts_or_zero()
{
    Assert.Equal(new Yaw(45), AskAnswers.FacingOf(p, reserveModel, new Yaw(45), new Point2(5, 5), null));
    Assert.Equal(new Yaw(0), AskAnswers.FacingOf(p, reserveModel, null, new Point2(5, 5), null));
}

[Fact] public void Embark_moves_every_model_of_the_region_inside_the_container()
{
    var d = AskAnswers.Embark(p, legal, container);
    Assert.Equal(legal.ModelIds, d.Moves.Select(m => m.ModelId));
    Assert.All(d.Moves, m => Assert.Equal(new Inside(container), m.To));
    Assert.False(d.Continues);
}

[Fact] public void Board_transport_item_is_the_embark_answer()
{
    var item = Assert.Single(MenuModel.ForBoarding(p, ask, m0));
    Assert.Equal(new AnswerAsk(ask.Id, AskAnswers.Embark(p, (MoveRegion)ask.Legal, container)), item.Build(null));
}
```

`Destinations` and `AnswerAsk` compare with `Assert.Equivalent` where they hold lists (record equality on `IReadOnlyList` is by reference). In `GameOverViewTests.cs`:

```csharp
[Fact] public void Answer_is_the_chosen_option_for_the_game_over_ask_and_team()
    => Assert.Equivalent(new AnswerAsk(model.AskId!.Value, new Chosen("end"), TableUx.T1), GameOverView.Answer(model, "end", TableUx.T1));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~AskAnswersTests|FullyQualifiedName~GameOverViewTests"`
Expected: build FAIL, `AskAnswers` and `GameOverView.Answer` not defined.

- [ ] **Step 3: Implement `AskAnswers` and `GameOverView.Answer`**

The bodies are the node code moved, per the semantics above. `MenuModel.ForBoarding`'s `Build` becomes `_ => new AnswerAsk(ask.Id, AskAnswers.Embark(p, region, c))` (still no team id, as now).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~AskAnswersTests|FullyQualifiedName~GameOverViewTests|FullyQualifiedName~PassengerMenuTests"`
Expected: PASS.

- [ ] **Step 5: The nodes call Core**

- `DecisionBox.Answer`: `await _s.SendCommandAsync(AskAnswers.Answer(ask, answer, _answering), _lastCommand.Value);`
- `DecisionBox.Resolve` is deleted; its callers (`RenderChoose`, `OfferEmbark`, `RenderPick`, `RenderTargets`) call `AskAnswers.Resolve(p, clicked, ids)` with `_s.Projection` (null projection: no hit, as now).
- `RenderChoose._onTarget`: `if (_s.Projection is { } p && AskAnswers.ChosenByClick(options, p, clicked) is { } chosen) Send(mine.Ask, chosen);`
- `RenderSpend.Spend`: `Send(mine.Ask, AskAnswers.Spent(spends.ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)));`
- `RenderMove.Relocate`: `var ghostYaws = legal.ModelIds.Select(id => (id, yaw: NodeYaw(id))).Where(x => x.yaw is not null).ToDictionary(x => x.id, x => x.yaw!.Value);` then `var answer = AskAnswers.Move(p, legal, proposed, ghostYaws, continues, MoveRuler.IsDisembark(legal) ? new ExitSite(_exitWithin, _volumes, _size) : null); _sent.Remember(ask.Id, answer.Moves); Send(ask, answer);`. `YawOf` is deleted.
- `OfferEmbark`: `Send(ask, AskAnswers.Embark(p, legal, container));`
- `EndMessage.Answer`: `_s.SendCommandAsync(GameOverView.Answer(model, optionId, team))`, with `Fill` passing the model it rendered.

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: build succeeds, check-client prints no failure, every test passes.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/AskAnswers.cs client/src/Core/MenuModel.cs client/src/Core/GameOverView.cs client/src/Hud/DecisionBox.cs client/src/Hud/DecisionBox.Move.cs client/src/Hud/EndMessage.cs tests/Tabletop.Client.Tests/AskAnswersTests.cs tests/Tabletop.Client.Tests/GameOverViewTests.cs
git commit -m "Client: the decision box's answers are built in Core (AskAnswers, GameOverView.Answer)"
```

### Task 2: `PiecePick`

`RenderPick`'s state (row 6): toggling a pick, the `Max` cap, the at-once answer of one of one, Done's gate and answer.

**Files:**
- Create: `client/src/Core/PiecePick.cs`
- Modify: `client/src/Hud/DecisionBox.Picks.cs` (`RenderPick`)
- Test: `tests/Tabletop.Client.Tests/PiecePickTests.cs`

**Interfaces:**
- Consumes: `DeployView.Pickable(Projection, Ask)` (existing).
- Produces:
  ```csharp
  public enum PickChange { Added, Removed, Unchanged }
  public sealed class PiecePick(PieceSelection selection, IReadOnlyList<Guid> eligible)
  {
      public IReadOnlyList<Guid> Picked { get; }
      /// <summary>An eligible id toggles; a new pick past Max, or an id not eligible, is Unchanged.</summary>
      public PickChange Toggle(Guid id);
      /// <summary>Max is 1 and one is picked: the box answers without Done.</summary>
      public bool AnswersAtOnce { get; }
      public bool Ready { get; }                       // Min <= Picked.Count <= Max
      public SelectedPieces? Answer();                 // null unless Ready; the picks in the order made
      public string Count(Names names);                // the one pick's name at Max 1, else "{n} of {Min}–{Max}"
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Toggling_adds_then_removes_in_order()
{
    var pick = new PiecePick(new PieceSelection([a, b, c], 1, 2), [a, b, c]);
    Assert.Equal(PickChange.Added, pick.Toggle(b)); Assert.Equal(PickChange.Added, pick.Toggle(a));
    Assert.Equal([b, a], pick.Picked);
    Assert.Equal(PickChange.Unchanged, pick.Toggle(c));          // past Max
    Assert.Equal(PickChange.Removed, pick.Toggle(b));
    Assert.Equivalent(new SelectedPieces([a]), pick.Answer());
}
[Fact] public void Not_eligible_is_unchanged()                     // eligible [a] only (a deploy pick less a unit with nothing on the shelf)
    => Assert.Equal(PickChange.Unchanged, new PiecePick(new PieceSelection([a, b], 1, 1), [a]).Toggle(b));
[Fact] public void One_of_one_answers_at_once()
{
    var pick = new PiecePick(new PieceSelection([a, b], 1, 1), [a, b]);
    pick.Toggle(a);
    Assert.True(pick.AnswersAtOnce);
}
[Fact] public void Done_waits_for_the_minimum()
{
    var pick = new PiecePick(new PieceSelection([a, b, c], 2, 3), [a, b, c]);
    pick.Toggle(a);
    Assert.False(pick.Ready); Assert.Null(pick.Answer()); Assert.Equal("1 of 2–3", pick.Count(names));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~PiecePickTests"`
Expected: build FAIL, `PiecePick` not defined.

- [ ] **Step 3: Implement `PiecePick`**

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~PiecePickTests"`
Expected: PASS.

- [ ] **Step 5: `RenderPick` over `PiecePick`**

`var pick = new PiecePick(sel, DeployView.Pickable(p, ask));`. `Toggle(id)`: `switch (pick.Toggle(id))` sets `Palette.Ghost` on Added, `Palette.Selected` on Removed, returns on Unchanged; then `if (pick.AnswersAtOnce) { Done(); return; }` and `Refresh()`. `Done()`: `if (pick.Answer() is { } answer) Send(ask, answer);`. `Refresh()`: `count.Text = pick.Count(names); done.Disabled = !pick.Ready;`. The shelved entries, number keys and table clicks keep calling `Toggle`.

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/PiecePick.cs client/src/Hud/DecisionBox.Picks.cs tests/Tabletop.Client.Tests/PiecePickTests.cs
git commit -m "Client: a pick's toggles and answer are Core (PiecePick)"
```

### Task 3: `DeployView.Place` and `HandMoves`

The deployment placement (row 13), the hand move and the play marker's drag (rows 14, 15), and the setup drop (row 48).

**Files:**
- Modify: `client/src/Core/DeployView.cs` (add `Place`)
- Create: `client/src/Core/HandMoves.cs`
- Modify: `client/src/Hud/DecisionBox.Move.cs` (`PlaceDeploy`), `client/src/Table/TableInput.cs` (`OnLeftUp`)
- Test: `tests/Tabletop.Client.Tests/DeployViewTests.cs` (add), `tests/Tabletop.Client.Tests/HandMovesTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // DeployView:
  /// <summary>One MoveModels from where each ghost's model is now to its ghost, in the ghosts' order.</summary>
  public static MoveModels Place(Projection p, IReadOnlyList<(Guid ModelId, Placement At)> ghosts);

  /// <summary>One drop's call: a command or a setup edit, never both.</summary>
  public sealed record Call(Command? Command, Edit? Edit);
  /// <summary>What a drag holds when it drops: the piece under the cursor, the held models, where each was grabbed, where it lands, its yaw.</summary>
  public sealed record HeldDrop(Guid PieceId, IReadOnlyList<Guid> ModelIds, IReadOnlyDictionary<Guid, Placement> Grabs,
      IReadOnlyDictionary<Guid, Point3> Current, IReadOnlyDictionary<Guid, Yaw> Yaws);
  public static class HandMoves
  {
      /// <summary>From each grab to its landing and yaw, in ModelIds order.</summary>
      public static MoveModels Models(HeldDrop drop);
      /// <summary>OnLeftUp's dispatch: the held models' MoveModels when the projection has them all; else in setup a draft terrain piece's
      /// MoveTerrain or a draft marker's MoveSetupMarker; else a play or table marker's MoveMarker; else null.</summary>
      public static Call? Drop(GameStatus? status, Projection? p, TableState? draft, HeldDrop drop);
  }
  ```
  `Call` lives in `HandMoves.cs`; part 2's writer serializes whichever field is set.

- [ ] **Step 1: Write the failing tests**

```csharp
// DeployViewTests
[Fact] public void Place_moves_each_ghost_from_the_shelf_in_the_ghosts_order()
{
    var p = TableUx.After();                                            // nothing deployed: every model on the shelf
    var mm = DeployView.Place(p, [(m2, Samples.At(14, 10)), (m0, Samples.At(10, 10) with { Yaw = new Yaw(30) })]);
    Assert.Equal([m2, m0], mm.Moves.Select(m => m.ModelId));
    Assert.All(mm.Moves, m => Assert.IsType<OnShelf>(m.From));
    Assert.Equal(new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(30))), mm.Moves[1].To);
}

// HandMovesTests
[Fact] public void A_hand_move_goes_from_each_grab_to_its_landing() { /* ModelIds [m1, m0]; From = OnTable(grab), To = OnTable(current, yaw), in that order */ }
[Fact] public void Drop_of_models_the_projection_holds_is_a_move_models() => Assert.IsType<MoveModels>(HandMoves.Drop(GameStatus.Playing, p, null, drop)!.Command);
[Fact] public void Drop_of_a_draft_terrain_piece_in_setup_is_move_terrain()
    => Assert.Equal(new MoveTerrain(t, at, yaw), HandMoves.Drop(GameStatus.Setup, null, draft, pieceDrop)!.Edit);
[Fact] public void Drop_of_a_draft_marker_in_setup_is_move_setup_marker() { /* MoveSetupMarker(id, current, yaw) */ }
[Fact] public void Drop_of_a_table_marker_in_play_is_move_marker() { /* p.Board.Table.Markers holds it: MoveMarker(id, current, yaw) */ }
[Fact] public void Drop_of_nothing_known_is_null() => Assert.Null(HandMoves.Drop(GameStatus.Playing, p, null, unknownDrop));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~DeployViewTests|FullyQualifiedName~HandMovesTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement `DeployView.Place`, `Call`, `HeldDrop`, `HandMoves`**

`Drop`'s order is `OnLeftUp`'s: models first (`drag.ModelIds.All(p.Models.ContainsKey)`), then setup terrain, setup marker, then `p.Markers.ContainsKey(id) || p.Board.Table.Markers.Any(m => m.Id == id)`.

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: PASS.

- [ ] **Step 5: The nodes call Core**

- `PlaceDeploy`: `var moves = DeployView.Place(p, _deployAt.Select(kv => (kv.Key, new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)))).ToList()); _sent.Remember(unit, moves.Moves); ... _s.SendCommandAsync(moves, id)`.
- `OnLeftUp`: after the `Proposing` return, `var drop = new HeldDrop(drag.PieceId, drag.ModelIds, drag.Grabs, drag.Current, drag.Yaws);` and `switch (HandMoves.Drop(status, p, draft, drop))`: `{ Command: MoveModels mm }` keeps today's `freeMove`, `_drops.Add(held, mm.Moves)`, `SendCommandAsync(mm, held)`, `HandMoved`; `{ Edit: { } e }` `SendEditAsync(e)`; `{ Command: { } c }` `SendCommandAsync(c)`. The catch and redraw stay.

- [ ] **Step 6: Build, check and run the client suite**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client && dotnet test tests/Tabletop.Client.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/Core/DeployView.cs client/src/Core/HandMoves.cs client/src/Hud/DecisionBox.Move.cs client/src/Table/TableInput.cs tests/Tabletop.Client.Tests/DeployViewTests.cs tests/Tabletop.Client.Tests/HandMovesTests.cs
git commit -m "Client: deploy placements and drops are built in Core (DeployView.Place, HandMoves)"
```

Part 1 is done after Task 3. Part 2 (`2026-10-25-same-calls-2-edits.md`) follows.
