# Same Calls, Part 3: The Calls Fixture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `viewer/tests/fixtures/calls.json` holds cases of (game state, the player's choice) → the exact message Godot's Core builders send, with the decisions behind it, written by a C# test from real game logs.

**Architecture:** As the combat log agreement does (`tests/Tabletop.Client.Tests/CombatLogViewerFixtureTests.cs` → `viewer/tests/fixtures/combatlog.json` → `viewer/tests/combatLog.agreement.test.ts`). `tests/Tabletop.Viewer.Tests` plays seven sim games in process and writes their frames as call logs; `CallsViewerFixtureTests` in `tests/Tabletop.Client.Tests` reads them and the two recorded fixtures, cuts each log at a chosen seq, applies a choice through the part 1–2 Core builders and writes the normalised message. Part 4 replays the cases through the browser.

**Tech Stack:** C# / .NET 10, xUnit, `System.Text.Json`; the sim's `InProcessDriver` (call logs only).

**Spec:** `docs/superpowers/specs/2026-10-25-same-calls-design.md`

**Build order:** part 1 (`-1-core.md`), part 2 (`-2-edits.md`), this part (Tasks 7–9), part 4 (`-4-replay.md`, Tasks 10–13), part 5 (`-5-ambush.md`, Task 14).

## Global Constraints

- "The fixture is written from Godot's code, never by hand, and the browser is fixed to match it, not the reverse, unless the case shows a Godot bug; then the fix lands in both with a test."
- Written only with `TABLETOP_WRITE_FIXTURES=1`; a plain run fails when the checked-in file differs, naming the stale case and the command, as `CombatLogViewerFixtureTests` does.
- States come from real logs: the recorded `skirmish` and `terrain` frames and the call logs below. `Samples` and `TableUx` mint ids at run time (`Guid.CreateVersion7()`), so a checked-in fixture cannot use them; a lobby or setup case takes a log's own game and table.
- Normalisation: ids are stable within a fixture (they come from the log files); message ids (`CommandId`, `EditId`) are excluded by writing the `Command` or `Edit` alone; an id a builder mints is renamed `new:1`, `new:2`… in order of first appearance; null properties are dropped; keys are sorted; numbers are rounded to 9 decimal places, `-0` written `0`.
- Facts the server sends in `ModelFacts` (`maxWounds`, `deployInside`) and management data (`grantable`) are case inputs: taken from what the log shows the server accepted, else empty.
- The call logs are regenerated deliberately; their ids differ between runs, so they are never diffed against a previous version, and `calls.json` is regenerated after them.

## Review Focus

- A builder that mints ids (`AddTerrain`, `AddSetupMarker`, `AddGrant`, `PlaceMarker`, an asset's `OverrideTerrainVolumes`) must normalise to the same `new:n` on both sides: the rename runs over the canonical (key-sorted) serialisation, never the builder's own order (Task 8 test `Fresh_ids_are_numbered_in_canonical_order`).
- A player holding two teams: every answer case in the `selfplay` log names the acting team, and `decisions.acting` and `decisions.myTeams` are written (Task 9 test `Every_required_case_kind_is_present` lists `selfplay/answer`).
- A deploy placement after a reconnect is not a case: the ghost stash is presentation; the placement case is the click and the drags (Task 9).
- A disembark's facing differs from the logged answer by design (Godot answers `ExitYawAt`); `Every_ask_case_rebuilds_the_logged_answer` compares a disembarking model's position, not its facing (Task 9).
- A disembark committed without a drag is not a case: Godot starts the ghosts on the container, the browser in rings (`DeployView.Around`), a presentation difference the spec's non-goals cover; every disembark case drags each model (Task 9).

---

### Task 7: The call logs

Seven sim games, played in process, written as the frames a socket delivers (welcome, board, events), with no recording.

**Files:**
- Create: `tests/Tabletop.Viewer.Tests/CallLogFixtureTests.cs`
- Modify: `tests/Tabletop.Viewer.Tests/FixtureWriter.cs` (add `WriteFramesAsync`, and `RunAsync` takes an optional welcome `RulesOptionCatalog`)
- Create (generated): `viewer/tests/fixtures/calls/<name>.welcome.json`, `.board.json`, `.events.json` for each name below

**Interfaces:**
- Consumes: `FixtureWriter.RunAsync(Scenario, CancellationToken)`, `SuiteCatalog.All` (`TestGame.Scenario`), `ScenarioCatalog.Get`, `GfOptions.Catalog`.
- Produces:
  ```csharp
  // FixtureWriter:
  /// <summary>The three frames a socket delivers on connect, under `name`; the welcome carries `options`.</summary>
  public static Task WriteFramesAsync(Fixture fixture, string name, string directory, RulesOptionCatalog options, CancellationToken ct);
  // CallLogFixtureTests:
  public static readonly (string Name, Func<Scenario> Scenario)[] CallLogs;
  ```
  `CallLogs`, each a game named in `sim/AGENTS.md` or `SuiteCatalog`:

  | Name | Game | What the cases need from it |
  | --- | --- | --- |
  | `ambush` | `special-scouts-and-ambushers` | the roster's Set aside before the set-aside events, Return to deployment after, the `deploy_ambusher` pick and the Ambusher's placement |
  | `transport` | `core-transport-run` | an embark (`Destinations` to `Inside`), a disembark (`Destinations` from `Inside`) |
  | `passengers` | `core-transport-deploy` | Put in transport and Take out of transport (the roster menu), a deploy pick that leaves out a unit inside |
  | `selfplay` | `core-self-play` with `RoundCap(2)` | one account holding both teams |
  | `gameover` | `final-round-warn` | the `game_over` Ask |
  | `casting` | `casting` | a `TokenSpend` Ask, a `cast` choice |
  | `objectives` | `objectives` with `RoundCap(2)` | objective markers: the marker menu, a play marker's drag, a setup marker's drop and removal |

- [ ] **Step 1: Write the failing test**

```csharp
public class CallLogFixtureTests
{
    public static TheoryData<string> Names() => new(CallLogs.Select(c => c.Name));

    [Theory, MemberData(nameof(Names))]
    public async Task The_call_log_exists_and_holds_what_its_cases_need(string name)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var (_, scenario) = CallLogs.Single(c => c.Name == name);
            await FixtureWriter.WriteFramesAsync(await FixtureWriter.RunAsync(scenario(), CancellationToken.None), name,
                Path.Combine(RepoPaths.Fixtures, "calls"), GfOptions.Catalog, CancellationToken.None);
        }
        var (welcome, board, events) = Read(name);       // each file present and parsed with WireJson
        Assert.True(Holds(name, welcome, board, events), $"calls/{name} lacks what its cases need");
    }
}
```

`Holds` per name: `ambush`: a `ConditionSet` of `MenuModel.SetAside`'s `"set-aside"` on a unit, present, and an `AskOpened` with `AskQuestions.DeployAmbusher`; `transport`: an `AskClosed` answer `Destinations` with an `Inside` destination and one whose relocation is from `Inside`; `passengers`: a `ModelsMoved` from `OnShelf` to `Inside`; `selfplay`: two participants with teams sharing one `AccountId`; `gameover`: an `AskOpened` with `AskQuestions.GameOver`; `casting`: an `AskOpened` whose `Legal` is `TokenSpend`; `objectives`: the board's table holds an `Objective` marker. The welcome's `Options` equals `GfOptions.Catalog`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Tabletop.Viewer.Tests --filter "FullyQualifiedName~CallLogFixtureTests"`
Expected: FAIL, the files are missing.

- [ ] **Step 3: Implement `WriteFramesAsync` and `CallLogs`; write the logs**

`WriteFramesAsync` writes the same three files `WriteAsync` writes, without the recording, the welcome built `new Welcome(fixture.RecorderParticipantId, fixture.Game) { Options = options }`. A `TestGame`'s scenario is `SuiteCatalog.All.Single(g => g.Name == "...").Scenario`; `core-self-play`'s is that `with { RoundCap = 2 }`.

Run: `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter "FullyQualifiedName~CallLogFixtureTests"`
Expected: PASS, twenty-one files under `viewer/tests/fixtures/calls/`. If a `Holds` fails, the game did not produce the state; pick its seed or script within that game (never a hand-made log) until it does, and note the change in the test.

- [ ] **Step 4: Run without writing**

Run: `dotnet test tests/Tabletop.Viewer.Tests`
Expected: PASS (the existing agreement tests unchanged).

- [ ] **Step 5: Commit**

```bash
git add tests/Tabletop.Viewer.Tests/CallLogFixtureTests.cs tests/Tabletop.Viewer.Tests/FixtureWriter.cs viewer/tests/fixtures/calls
git commit -m "Viewer tests: call logs for the calls agreement (ambush, transport, passengers, selfplay, gameover, casting, objectives)"
```

### Task 8: The normalisation, on both sides

One algorithm in C# and TypeScript, pinned by the same literal cases in both tests.

**Files:**
- Create: `tests/Tabletop.Client.Tests/CallNormalise.cs`, `tests/Tabletop.Client.Tests/CallNormaliseTests.cs`
- Create: `viewer/tests/callNormalise.ts`, `viewer/tests/callNormalise.test.ts`

**Interfaces:**
- Produces:
  ```csharp
  public static class CallNormalise
  {
      /// <summary>Every GUID-shaped string in these JSON texts (lower case).</summary>
      public static HashSet<string> KnownIds(params string[] json);
      /// <summary>Keys sorted, nulls dropped, numbers rounded, unknown ids renamed new:n in canonical order.</summary>
      public static JsonNode Of(JsonNode message, IReadOnlySet<string> known);
      /// <summary>The canonical text: compact, keys sorted, JavaScriptEncoder.UnsafeRelaxedJsonEscaping.</summary>
      public static string Text(JsonNode normalised);
  }
  ```
  ```ts
  export function knownIds(...json: string[]): Set<string>;
  export function normaliseCall(message: unknown, known: ReadonlySet<string>): unknown;
  export function canonical(value: unknown): string;   // JSON.stringify of the key-sorted value
  ```

The algorithm, both sides: (1) drop every property whose value is null; (2) sort object keys ordinally, recursively; (3) a number becomes `sign(x) · round(|x| · 1e9) / 1e9` with halves away from zero, and `-0` becomes `0`; (4) walk the sorted value depth first, keys in order, array items in order; each string, and each object key, that matches `^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$` (case-insensitive) and is not in `known` is replaced by `new:k`, the same id by the same `k`, `k` counting from 1 in order of first meeting.

- [ ] **Step 1: Write the failing tests**

Both test files hold these three cases verbatim and assert `Text(Of(input, known))` / `canonical(normaliseCall(input, known))` equals the expected string:

```text
1. Nulls_go_numbers_round_keys_sort
   input   {"kind":"add_setup_marker","marker":{"id":"019a0000-0000-7000-8000-00000000000f","kind":"objective","position":{"x":0.30000000000000004,"y":-0.0,"z":3},"yaw":{"degrees":0},"diameter":3,"label":null,"visual":{"builtin":"objective-ring"}}}
   known   {}
   expect  {"kind":"add_setup_marker","marker":{"diameter":3,"id":"new:1","kind":"objective","position":{"x":0.3,"y":0,"z":3},"visual":{"builtin":"objective-ring"},"yaw":{"degrees":0}}}
2. Fresh_ids_are_numbered_in_canonical_order
   input   {"z":"019a0000-0000-7000-8000-0000000000aa","a":["019a0000-0000-7000-8000-0000000000bb","019a0000-0000-7000-8000-0000000000aa"]}
   known   {}
   expect  {"a":["new:1","new:2"],"z":"new:2"}
3. Known_ids_stay_and_keys_are_checked_too
   input   {"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}
   known   {"019a0000-0000-7000-8000-000000000001"}
   expect  {"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}
```

and `KnownIds_finds_every_guid_in_the_texts` (two texts, three ids, upper case folded).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~CallNormaliseTests"` and `cd viewer && npx vitest run tests/callNormalise.test.ts`
Expected: both FAIL, not defined.

- [ ] **Step 3: Implement both**

- [ ] **Step 4: Run the tests to verify they pass**

Same commands. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Tabletop.Client.Tests/CallNormalise.cs tests/Tabletop.Client.Tests/CallNormaliseTests.cs viewer/tests/callNormalise.ts viewer/tests/callNormalise.test.ts
git commit -m "Calls agreement: one normalisation in C# and TypeScript"
```

### Task 9: `CallsViewerFixtureTests`

The writer: each case is a log cut at a seq (or, for lobby and setup, a log's game and table), a player, a choice, and what Godot's Core builders send for it.

**Files:**
- Create: `tests/Tabletop.Client.Tests/CallsViewerFixtureTests.cs` (the facts, reading the logs, writing and the stale check)
- Create: `tests/Tabletop.Client.Tests/CallCases.cs` (`CallLog`, `CallState`, `CallCase`, the decisions), `CallCases.Asks.cs`, `CallCases.Menus.cs` (menus, undo, drops), `CallCases.Edits.cs` (lobby, setup)
- Create (generated): `viewer/tests/fixtures/calls.json`

**Interfaces:**
- Consumes: parts 1–2 (`AskAnswers`, `PiecePick`, `DeployView.Place`, `DeployView.Layout`, `HandMoves`, `Menus`, `MenuModel`, `CombatLog.UndoHere`, `UndoPlan.Of`, `GameOverView`, `LobbyEdits`, `SetupEdits`, `LobbyView`, `WaitingView`), `Seats.Teams`, `Seats.Acting`, `AskViewModel.Mine`, `DecisionView.Of`, `DeployView.MidDeploy`, `DeployView.Pickable`, Task 8's `CallNormalise`.
- Produces: `viewer/tests/fixtures/calls.json`, read by part 4. Its shape:
  ```jsonc
  {
    "logs": ["skirmish", "terrain", "calls/ambush", "calls/transport", "calls/passengers", "calls/selfplay", "calls/gameover", "calls/casting", "calls/objectives"],
    "cases": [{
      "name": "choose/key skirmish@57",          // kind, then the log and seq: unique
      "log": "skirmish",                         // tests/fixtures/<log>.{welcome,board,events}.json
      "upTo": 57,                                // events with seq <= upTo are applied; lobby and setup cases: the whole log is not applied
      "status": "playing",                       // "lobby" | "setup": the log's game with this status, its board's table as the draft
      "participantId": "…",                      // the player: a team's lowest-id participant
      "creator": false,                          // lobby cases: the player is the game's creator
      "maxWounds": {}, "deployInside": {}, "grantable": [],   // facts and management data as case inputs
      "choice": { "kind": "key", "key": 1 },
      "decisions": { "myTeams": ["…"], "acting": "…", "mine": { "askId": "…", "mode": "choose" }, "midDeploy": null,
                     "numbered": [[1, "opt"]], "pickable": ["…"], "menu": ["Set wounds", "…"], "undo": { "toSeq": 51, "here": [12, 40] } },
      "message": { "kind": "answer_ask", "askId": "…", "answer": { "kind": "chosen", "optionId": "opt" }, "teamId": "…" }   // or null: nothing is sent
    }]
  }
  ```
  `mode` is `AskMode` camel-cased (`choose`, `move`, `pickPieces`, `assign`, `allocate`, `tokenSpend`, `confirm`); `numbered`, `pickable`, `menu` and `undo` are written only for the kinds that read them. `message` is `WireJson.Serialize<Command>` or `Serialize<Edit>` of the built call, parsed, then `CallNormalise.Of` with the known ids of the log's three files and of the case's own inputs (the case object less `decisions` and `message`), the set part 4 computes the same way.

The state and the decisions, every playing case: `EventApplier(board, game)` over the events up to `upTo` gives `p` and `log`; `myTeams = Seats.Teams(game, participantId)`; `acting = Seats.Acting(p, myTeams, log)`; `mine = AskViewModel.Mine(p, game, acting)`; `midDeploy` = the first non-null `DeployView.MidDeploy(p, log, t)` for `t` in `[acting, ..myTeams]` (the decision box's order).

The choices, each with what the writer calls (Godot's path) and what part 4's replay presses (the browser's path):

| `choice.kind` | Fields | Godot's Core path | Browser's path (part 4) |
| --- | --- | --- | --- |
| `option` | `optionId` | `AskAnswers.Answer(ask, new Chosen(id), acting)` | `box.press('opt:' + id)` |
| `key` | `key` | `DecisionView.Of(...).Numbered` → `Chosen` | `box.number(key)` |
| `unitKey` | `unitId`, `key` | `DecisionView.Number(DecisionView.ChoicesOf(options, p, unitId))` → `Chosen` | `box.unit(unitId); box.number(key)` |
| `click` | `pieceId` | `AskAnswers.ChosenByClick` (null: no message) | `box.target(pieceId)` |
| `pass` | — | `Passed` | `box.press('pass')` |
| `confirm` | — | `Confirmed` | `box.enter()` |
| `spend` | `values` | `AskAnswers.Spent` | `box.spin('spin:' + id, n)` each, `box.enter()` |
| `pick` | `clicks`, `done` | `PiecePick.Toggle` each; `AnswersAtOnce` or `done` → `Answer()` | `box.target(id)` each; `done` → `box.enter()` |
| `pickKey` | `key` | the numbered id → `PiecePick.Toggle` | `box.number(key)` |
| `assign` | `steps` of `{pick: groupId}`, `{aim: unitId}`, `{clear: true}` | `TargetAssignment.PickOnly`, `Aim`, `Clear`; `Answer()` | `box.press('chip:' + g)`, `box.target(u)`, `box.cancel()`; `box.enter()` |
| `casualties` | `steps` of `{click: id}`, `{remove: id}`, `{auto: true}`, `{clear: true}` | `CasualtyPick.Click`, `Remove`, `Auto`, `Clear`; `Answer()` | `box.model(id, false)`, `box.model(id, true)`, `box.press('auto')`, `box.press('clear')`; `box.enter()` |
| `move` | `ghosts` (id → placement), `stop` | `AskAnswers.Move(p, legal, positions, yaws, stop, exit)`; `exit` for a disembark is `new ExitSite(containment.ExitWithin ?? 0, table.Terrain.SelectMany(t => t.WorldVolumes()) (as `TableView.Volumes`), table size)` | `box.dragged(id, placement)` each; `stop ? box.stop() : box.enter()` |
| `embark` | `containerId` | `AskAnswers.Embark` | `box.target(containerId)` |
| `deploy` | `at`, `ghosts` (optional) | `DeployView.Layout(shelved models by ordinal, at, size)`, each listed ghost replacing its model's placement → `DeployView.Place` | `box.placeAt(at)`; `box.dragged(id, placement)` each; `box.enter()` |
| `gameOver` | `optionId` | `GameOverView.Answer(GameOverView.Of(p, game, acting), id, acting)` | part 4's `answerOf` over `gameOverOf` |
| `menu` | `open` (`{piece, shift}`, `{team}`, `{roster}`, `{table}`), `label`, `input` | `Menus.Piece`/`Team`/`Roster`/`Table` over a `MenuContext`; the item by label; `Build(input)` | `pieceMenu`/`teamMenu`/`rosterMenu`/`tableMenu`; `choose(item, input)` |
| `undoButton` | — | `UndoPlan.Of(log, names, entries).ToSeq` → `Undo` | `undoPlanOf` → `UndoPrediction.undoTo` |
| `undoRow` | `seq` | the entry at `seq`: `CombatLog.UndoHere` → `Undo(seq)`, else null | `undoHere` (a roll row: `!undone && !watching`) → `undoTo` |
| `drop` | `pieceId`, `modelIds`, `grabs`, `current`, `yaws` | `HandMoves.Drop(status, p, draft, drop)` | part 4's `handMoveCommand`, `markerMove`, `dropEdit` |
| `edit` | `control`, its arguments | the `LobbyEdits`, `SetupEdits`, `LobbyView`, `WaitingView`, `MenuModel` function for the control | the browser function for the control (part 4, Task 10) |

How the writer picks the cases, from the logs only:

- **Answers.** For each `AskClosed` and each team in it, the state is the seq before that team's `AskAnswered` (else before the `AskClosed`), the player that team's lowest-id participant, the choice derived from the logged answer: `Chosen` → an `option` case, a `key` case when numbered, a `click` case when a piece of the option is named by no other option, a `unitKey` case for an activate option; a `click` case on a piece two options name (message null); `Passed` → `pass`; `Confirmed` → `confirm`; `TokensSpent` → `spend`; `SelectedPieces` → `pick` (and `pickKey` for a deploy question); `Assigned` → `assign` (one `aim` when every group shares a target, else `pick` then `aim` per group); `Allocated` → `casualties` (one `click` per wound, `maxWounds` each target's wounds plus its count, the least the server accepted); `Destinations` to `Inside` → `embark`; else → `move` with the logged placements as ghosts and `stop` the logged `Continues`; `game_over` → `gameOver`. At most two cases per log, question and kind.
- **Deployment.** For each deploy pick, the state is the seq before its `ModelsMoved` from the shelf: a `deploy` case at the first model's logged position with no ghosts, and one with every logged placement as ghosts. In `ambush`, the Ambusher's.
- **Menus, undo and drops**, at fixed points of the logs: the first state after deployment (piece menu, Shift, a team menu, the frame's Abandon mid-activation, a drop of one unit's models 1" along x); `ambush` before and after its set-aside `ConditionSet` (Set aside, Return to deployment); `passengers` before its moves inside (Put in transport, with `deployInside` from the logged move) and after (Take out of transport); `transport` at a move Ask with `Enterable` (Board transport); `objectives` (a marker's Give to Team 1, Make neutral, and its local Unlock, which sends nothing; a play marker's drop); the table menu's token labelled `smoke` at (5, 6, 0); inputs `"3"`, `" 3 "`, `"3.5"`, `"abc"` on Set wounds, `"Pinned"` on Add a condition, `"x,y,z"` from a model's position on a back-on-table item, a `grantable` of one literal entry on Add a grant; Undo at three states of `skirmish`, Undo to here on a line, a roll, an undone row (null).
- **Lobby and setup.** `selfplay`'s and `terrain`'s game with status `lobby` or `setup`, their board's table as the draft, the creator and a non-creator: every team button, Watch, Create team, a Players placement (choices 0 and 2), each army seat's pick, the Create game army, a waiting Join (the game less team 2's participants and their armies), Apply configuration (name `"  Friday  "`, player turn, strict, the first option's second value), Start setup, End setup; Set size 60×44, texture `desert`, Add built-in `ruin` (draws 0.5, 0.25), Add my terrain (a literal asset id, `ruin`'s volume; draws 0, 0.99), Random table (the `terrain` table's pieces laid, none unplaced), Clear terrain, Load layout (the `terrain` table), Add objective, Add token, Set zones `spearhead`, Swap 1 and 2, the setup menu's three turns (`"90"` for Set facing) and Remove for a piece and a marker, Scale ×1.5, Swap mesh (the catalog's first name), Swap for my asset, Remove, Remove marker, Reset volumes, and each volume field (kind, footprint a and b, bottom, top, Cover on and off, notes `"ford"` and `""`); setup drops of a terrain piece and a marker.

- [ ] **Step 1: Write the failing tests**

```csharp
public class CallsViewerFixtureTests
{
    [Fact] public void Calls_fixture_is_current() { /* as Combatlog_fixture_is_current: write with TABLETOP_WRITE_FIXTURES=1; else compare and name the first stale case */ }

    [Fact] public void Every_required_case_kind_is_present()
    {
        var names = CallCases.All().Select(c => c.Name.Split(' ')[0]).ToHashSet();
        Assert.Empty(CallCases.Required.Except(names));
    }

    [Fact] public void Every_ask_case_rebuilds_the_logged_answer()
    {
        // answer cases only: the built AnswerAsk's answer equals the logged one, but a disembarking model's facing (Godot answers ExitYawAt)
        foreach (var c in CallCases.Answers()) Assert.True(c.RebuildsLogged, c.Name);
    }

    [Fact] public void Case_names_are_unique() => Assert.Equal(CallCases.All().Count, CallCases.All().Select(c => c.Name).Distinct().Count());
}
```

`CallCases.Required` names, at least: `choose/option`, `choose/key`, `choose/unitKey`, `choose/click`, `choose/clickNone`, `pass`, `confirm`, `confirm/endRound`, `spend`, `pick`, `pick/deploy`, `pick/ambusher`, `pickKey`, `assign`, `casualties`, `move`, `move/stop`, `move/disembark`, `embark`, `deploy/click`, `deploy/ghosts`, `deploy/ambusher`, `gameOver`, `selfplay/answer`, `selfplay/deploy`, `menu/model`, `menu/unit`, `menu/marker`, `menu/team`, `menu/setAside`, `menu/return`, `menu/putIn`, `menu/takeOut`, `menu/abandon`, `menu/boarding`, `menu/token`, `menu/lock`, `menu/invalidInput`, `menu/grant`, `undo/button`, `undo/row`, `undo/roll`, `undo/undone`, `drop/models`, `drop/marker`, `drop/terrain`, `drop/setupMarker`, and `edit/<control>` for each control above.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~CallsViewerFixture"`
Expected: build FAIL, `CallCases` not defined.

- [ ] **Step 3: Implement the writer**

`CallLog.Read(name)` reads the three files under `viewer/tests/fixtures/` (`RepoRoot()` as `CombatLogViewerFixtureTests` finds it). A case records `RebuildsLogged` when it compared its built answer with the log's. Keep each file under 500 lines.

- [ ] **Step 4: Write the fixture and run the tests**

Run: `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~CallsViewerFixture"`, then `dotnet test tests/Tabletop.Client.Tests`
Expected: PASS; `viewer/tests/fixtures/calls.json` written. A required kind absent from the logs is not dropped from `Required`: the log that should hold it is fixed (Task 7, Step 3).

- [ ] **Step 5: Commit**

```bash
git add tests/Tabletop.Client.Tests/CallsViewerFixtureTests.cs tests/Tabletop.Client.Tests/CallCases*.cs viewer/tests/fixtures/calls.json
git commit -m "Client tests: CallsViewerFixtureTests writes the calls fixture from Godot's Core builders"
```

Part 3 is done when `calls.json` is current. Part 4 (`2026-10-25-same-calls-4-replay.md`) replays it.
