# Same Calls, Part 4: The Browser Replay — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `viewer/tests/calls.agreement.test.ts` replays every case of `calls.json` through the browser's own modules and gets byte-equal normalised messages and equal decisions; the browser is fixed where it differs; both modules' docs say a new outbound message needs a case.

**Architecture:** The replay drives the browser's `DecisionBox` (with D2's `moveHooks` over the test WebAssembly geometry) exactly as the page's keys, clicks and drags do, and calls the browser's menu, undo, drop, lobby and setup builders for the other kinds, capturing what reaches `send`. The builders still inline in Svelte components move into `.ts` modules first, so the replay calls what the pages call.

**Tech Stack:** TypeScript, Vitest, Svelte 5 (the pages only), the `Tabletop.Geometry` WebAssembly; C# for the one geometry move in Task 12.

**Spec:** `docs/superpowers/specs/2026-10-25-same-calls-design.md`

**Build order:** parts 1–3, then this part (Tasks 10–13), then part 5 (`-5-ambush.md`).

## Global Constraints

- "The fixture is written from Godot's code, never by hand, and the browser is fixed to match it, not the reverse, unless the case shows a Godot bug; then the fix lands in both with a test."
- "No behaviour change in either client from the moves into Core; the existing client and viewer tests stay green." A builder moved out of a Svelte component sends what the component sent.
- "A new outbound message in either client is not done until it has a fixture case (written into `client/AGENTS.md` and `viewer/AGENTS.md`)."
- The browser judges nothing: the replay runs the box with enforcement `silent`, so no gate stands between a choice and its send.
- `npm test` needs `./scripts/geometry-wasm` run first (the replay loads `loadTestGeometry`).

## Review Focus

- A case whose `message` is null must send nothing: the replay fails on any captured send (Task 11 test `sends nothing when Godot sends nothing`).
- The self-play player: `decisions.acting` and the message's `teamId` come from `acting(p, teamsOf(game, participantId), log)`, never a fixed first team (Task 11).
- A turned disembarking ghost: the browser sends the exit yaw at the drop, as Godot does (Task 12 test `a disembark answers the exit yaw at the drop, not the ghost's`).
- An invalid menu input (`"3.5"`, `"abc"`) sends nothing on either side (`menu/invalidInput` cases, Task 11).
- A fresh id in a browser message (`crypto.randomUUID()`) normalises like Godot's `Guid.CreateVersion7()`: the replay's known set is built from the same files and case inputs as the writer's (Task 11).

---

### Task 10: The browser builders the replay calls

The sends still built inside Svelte components (inventory rows 12, 14, 21, 22, 25, 28–31, 38, 39, 42–46, 48) move into `.ts` modules beside their siblings; the components call them.

**Files:**
- Create: `viewer/src/lib/lobby/lobbyEdits.ts`
- Modify: `viewer/src/lib/setup/edits.ts`, `viewer/src/lib/play/handMove.ts`, `viewer/src/lib/setup/drag.ts`, `viewer/src/lib/play/gameOverView.ts`
- Modify: `viewer/src/lib/lobby/TeamCards.svelte`, `viewer/src/lib/lobby/LobbyPage.svelte`, `viewer/src/lib/game/header.ts`, `viewer/src/lib/game/GamePage.svelte`, `viewer/src/lib/setup/SetupTools.svelte`, `viewer/src/lib/setup/TerrainCard.svelte`, `viewer/src/lib/play/playDrag.ts`, `viewer/src/lib/play/GameOverModal.svelte`
- Test: `viewer/tests/lobbyEdits.test.ts`, `viewer/tests/setupEdits.test.ts`, `viewer/tests/handMove.test.ts`, `viewer/tests/setupDrag.test.ts`, `viewer/tests/gameOverView.test.ts` (add to the existing files where they exist)

**Interfaces:**
- Produces (each mirrors part 2's C# function of the same name):
  ```ts
  // lobby/lobbyEdits.ts
  export const watch = (): PickTeam => ({ kind: 'pick_team' });
  export const createTeam = (): CreateTeam; export const startSetup = (): StartSetup; export const endSetup = (): EndSetup;
  export const armyFromCreate = (armyId: string): PickArmy;                       // no teamId
  // setup/edits.ts
  export function tableSize(width: number, length: number): SetTableSize;
  export function texture(builtin: string): SetTexture;
  export function addBuiltin(entry: BuiltinTerrain, size: TableSize, random?: () => number): AddTerrain;   // placeBuiltin(entry, beside(size, random), yaw 0)
  export function addAsset(assetId: string, volumes: readonly Volume[], size: TableSize, random?: () => number): AddTerrain;
  export function zones(style: string): SetZoneStyle;                             // zoneShapes(style)
  export function swapZones(a: number, b: number): SwapZones;
  export function scale(placementId: string, value: number): ScaleTerrain;
  export function swapBuiltin(placementId: string, builtin: string): SwapTerrainMesh;
  export function swapAsset(placementId: string, assetId: string): SwapTerrainMesh;
  export function removeTerrain(placementId: string): RemoveTerrain;
  export function removeMarker(markerId: string): RemoveSetupMarker;
  export function resetVolumes(placementId: string, volumes: readonly Volume[]): OverrideTerrainVolumes;   // new ids
  // play/handMove.ts
  export function handMoveCommand(grabs: ReadonlyMap<string, Placement>, landings: ReadonlyMap<string, Placement>): MoveModels;
  // setup/drag.ts
  export function dropEdit(draft: TableState, pieceId: string, position: Point3, yaw: Yaw): MoveTerrain | MoveSetupMarker | null;
  // play/gameOverView.ts
  export function answerOf(model: GameOverModel, optionId: string, team: string | null): AnswerAsk;   // teamId omitted when null
  ```

- [ ] **Step 1: Write the failing tests**

One test per function, asserting the exact object the component sent before, e.g.:

```ts
it('watch is pick_team with no team', () => expect(watch()).toEqual({ kind: 'pick_team' }));
it('the create game army names no team', () => expect(armyFromCreate(A)).toEqual({ kind: 'pick_army', armyId: A }));
it('a built-in piece lands beside the table at yaw 0', () => {
  const draws = [0.5, 0.25];
  const e = addBuiltin(ruin, { width: 72, length: 48 }, () => draws.shift()!);
  expect(e.placement.position).toEqual({ x: 84, y: 12, z: 0 });
  expect(e.placement.yaw).toEqual({ degrees: 0 });
});
it('reset volumes takes each volume under a new id', () => { /* ids distinct, volumes in order */ });
it('a hand move goes from each grab to its landing', () => expect(handMoveCommand(grabs, landings)).toEqual({ kind: 'move_models', moves: handMoves(grabs, landings) }));
it('a dropped draft piece is move_terrain, a marker move_setup_marker, anything else null', () => { /* three asserts */ });
it('a game over answer names its team', () => expect(answerOf(model, 'end', T1)).toEqual({ kind: 'answer_ask', askId: model.askId, answer: { kind: 'chosen', optionId: 'end' }, teamId: T1 }));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd viewer && npx vitest run tests/lobbyEdits.test.ts tests/setupEdits.test.ts tests/handMove.test.ts tests/setupDrag.test.ts tests/gameOverView.test.ts`
Expected: FAIL, the new exports missing.

- [ ] **Step 3: Implement them; the components call them**

`SetupDrag.end` returns `dropEdit(draft, held.id, <its landing>, held.yaw)`; `PlayDrags.#handMove` sends `handMoveCommand(...)`; `GameOverModal.choose` sends `answerOf(m, optionId, team)`; `header.ts`'s primary edit is `endSetup()`; the Svelte buttons call the functions in place of their object literals.

- [ ] **Step 4: Run the viewer suite and the type check**

Run: `cd viewer && npm test && npm run check`
Expected: PASS, no type errors.

- [ ] **Step 5: Commit**

```bash
git add viewer/src/lib/lobby/lobbyEdits.ts viewer/src/lib/setup/edits.ts viewer/src/lib/play/handMove.ts viewer/src/lib/setup/drag.ts viewer/src/lib/play/gameOverView.ts viewer/src/lib/lobby/TeamCards.svelte viewer/src/lib/lobby/LobbyPage.svelte viewer/src/lib/game/header.ts viewer/src/lib/game/GamePage.svelte viewer/src/lib/setup/SetupTools.svelte viewer/src/lib/setup/TerrainCard.svelte viewer/src/lib/play/playDrag.ts viewer/src/lib/play/GameOverModal.svelte viewer/tests
git commit -m "Viewer: every send the pages build inline is a function in a module"
```

### Task 11: `calls.agreement.test.ts`

**Files:**
- Create: `viewer/tests/calls.agreement.test.ts`, `viewer/tests/callRig.ts` (the state, the box and the drivers)

**Interfaces:**
- Consumes: `viewer/tests/fixtures/calls.json` (part 3, Task 9's shape and choice table), Task 8's `knownIds`, `normaliseCall`, `canonical`, Task 10's builders, and the browser modules: `Applier`, `teamsOf`, `acting`, `mine` (`askView.ts`), `decisionOf`, `midDeploy`, `pickable`, `DecisionBox`, `moveHooks`, `pieceMenu`, `teamMenu`, `rosterMenu`, `tableMenu`, `choose`, `undoPlanOf`, `UndoPrediction`, `undoHere`, `build` (`combatLog.ts`), `gameOverOf`, `answerOf`, `markerMove`, `handMoveCommand`, `dropEdit`, and the lobby and setup functions.
- Produces:
  ```ts
  // callRig.ts
  export interface CallLogFiles { readonly welcome: string; readonly board: string; readonly events: string }   // the raw texts
  export function readLog(name: string): CallLogFiles;                       // tests/fixtures/<name>.*.json, cached
  export function stateOf(c: CallCase, log: CallLogFiles): ConnectionState;  // playing: Applier over seq <= upTo; lobby/setup: the game with c.status, draft = board.table
  export function decisionsOf(c: CallCase, st: ConnectionState): Decisions;   // only the keys the case's decisions hold
  export async function replay(c: CallCase, st: ConnectionState, g: Geometry): Promise<unknown | null>;   // what reached send, or null
  ```

`replay` builds a `DecisionBox` as `tests/moveRig.ts` does, with `moveHooks({ geometry: () => g, violations: new Violations(), enforcement: () => 'silent' })`, `rules()` `RuleTexts.empty`, and a `send` that records the command and resolves `{ id: 'm1', refused: null }`; it calls `box.render()`, then presses per the choice table of part 3, Task 9. The other kinds call the module functions in that table. A `menu` case builds `MenuContext` from the state (`watching` false, `myTeam` acting, `mine` from `mine(...)`, `grantable`, `deployInside` from the case, `locks` a new `MarkerLocks`).

- [ ] **Step 1: Write the test**

```ts
const fixture = JSON.parse(readFileSync('tests/fixtures/calls.json', 'utf8')) as { logs: string[]; cases: CallCase[] };
let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe.each(fixture.cases)('calls agreement', (c) => {
  const log = readLog(c.log);
  const known = knownIds(log.welcome, log.board, log.events, JSON.stringify({ ...c, decisions: undefined, message: undefined }));

  it(`${c.name}: the decisions agree`, () => {
    expect(decisionsOf(c, stateOf(c, log))).toEqual(c.decisions);
  });

  it(`${c.name}: the browser sends what Godot sends`, async () => {
    const sent = await replay(c, stateOf(c, log), g);
    if (c.message === null) { expect(sent).toBeNull(); return; }      // sends nothing when Godot sends nothing
    expect(canonical(normaliseCall(sent, known))).toBe(canonical(c.message));
  });
});

it('the fixture covers every kind the writer requires', () => {
  expect(new Set(fixture.cases.map((c) => c.name.split(' ')[0]))).toContain('deploy/ambusher');
  expect(fixture.cases.length).toBeGreaterThan(100);
});
```

`JSON.stringify` drops the `undefined` keys, which is the C# writer's "case less `decisions` and `message`"; the C# side serialises the case without those two properties, so both read the same ids.

- [ ] **Step 2: Run it**

Run: `cd viewer && npx vitest run tests/calls.agreement.test.ts`
Expected: the decisions and most messages PASS; every failure is a disagreement Task 12 resolves. Record the failing case names in the Task 12 commit message, not in a file.

- [ ] **Step 3: Commit the replay with its failures pinned as `it.fails`**

Each failing case's message test is listed in `const pinned: readonly string[]` at the top of the file and run as `it.fails`, so the suite is green and Task 12 removes each entry as it fixes it.

```bash
git add viewer/tests/calls.agreement.test.ts viewer/tests/callRig.ts
git commit -m "Viewer: calls.agreement.test.ts replays Godot's calls through the browser"
```

### Task 12: The agreement fixes

Every case pinned in Task 11 is fixed in the browser, unless it shows a Godot bug, which is fixed in both. One disagreement is known from reading the code and is fixed here whatever else the replay finds.

**Files (the known one):**
- Modify: `libs/Tabletop.Geometry/Containment.cs` (add `ExitYaw`), `client/src/Core/MoveRuler.cs` (`ExitYawAt` and `Admits` call it)
- Modify: `libs/Tabletop.Geometry.Wasm/Facade.cs`, `libs/Tabletop.Geometry.Wasm/Exports.cs` (add `ExitYaw`)
- Modify: `viewer/src/lib/geometry/types.ts`, `viewer/src/lib/geometry/load.ts` (add `exitYaw`)
- Modify: `viewer/src/lib/play/decisionBox.move.ts` (`relocate`)
- Test: `tests/Tabletop.Geometry.Tests/ContainmentTests.cs`, `tests/Tabletop.Geometry.Wasm.Tests` (the agreement cases), `viewer/tests/decisionBoxMove.test.ts`

**Interfaces:**
- Produces:
  ```csharp
  // Tabletop.Geometry, Containment:
  /// <summary>p.7: a facing this exit position is legal at, or null: fully within `within` of the container's base, on the table, clear of
  /// the impassable volumes; a circular base tries 0° only, any other ExitYaws facings 45° apart.</summary>
  public const int ExitYaws = 8;
  public static Yaw? ExitYaw(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size);
  ```
  ```ts
  // Geometry:
  exitYaw(body: ModelBody, container: PlacedModel, within: number, at: XY, terrain: Terrain, size: TableSize): Yaw | null;
  ```

The known disagreement: Godot answers a disembarking model's facing with `MoveRuler.ExitYawAt` at the drop (part 1 `AskAnswers.FacingOf`), falling back to the container's facing; the browser answers the ghost's own placement yaw (`decisionBox.move.ts` `relocate`), so a turned ghost, or a drop no facing fits, sends a different `Destinations`. The `move/disembark` cases show it.

- [ ] **Step 1: Write the failing tests**

```csharp
// ContainmentTests
[Fact] public void Exit_yaw_of_a_round_base_within_reach_is_zero() { /* 1" circle, container 4" circle at (10,10), within 6, at (14,10): Yaw(0) */ }
[Fact] public void Exit_yaw_beyond_reach_is_null() { /* at (30,10): null */ }
[Fact] public void Exit_yaw_of_an_oval_tries_eight_facings() { /* a 3x1 oval that fits only side-on near the table edge: Yaw(90) */ }
```

```ts
// decisionBoxMove.test.ts (over moveRig with a disembark Ask and the test geometry)
it('a disembark answers the exit yaw at the drop, not the ghost\'s', () => {
  box.dragged(m, { position: { x: 14, y: 10, z: 0 }, yaw: { degrees: 135 } });
  box.enter();
  expect(sentDestinations().moves[0]!.to).toEqual({ kind: 'table', placement: { position: { x: 14, y: 10, z: 0 }, yaw: { degrees: 0 } } });
});
it('a disembark no facing fits answers the container\'s facing', () => { /* drop out of reach: the container's yaw */ });
```

and a geometry agreement case for `ExitYaw` in `tests/Tabletop.Geometry.Wasm.Tests` beside the others (`TABLETOP_WRITE_FIXTURES=1` rewrites `viewer/tests/fixtures/geometry.cases.json`).

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Tabletop.Geometry.Tests --filter "FullyQualifiedName~ContainmentTests"` and `cd viewer && npx vitest run tests/decisionBoxMove.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement**

`Containment.ExitYaw` is `MoveRuler.ExitYawAt` and `AdmitsAt` moved (they use only `Containment.BaseFullyWithin` and `Movement.Check`); `MoveRuler.ExitYaws` and `ExitYawAt` delegate, so Godot is unchanged. The facade's `ExitYaw` takes the terrain as the other facade methods do and passes `WorldVolumes()`. `relocate`, for a model whose location is `inside`: `g?.exitYaw(body, placed(container.slot, containerPlacement), legal.containment!.exitWithin ?? 0, ghost.position, p.board.table.terrain, size) ?? containerPlacement.yaw`.

Then: `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests`, `./scripts/geometry-wasm`.

- [ ] **Step 4: Run them to verify they pass, and unpin**

Run: `dotnet test tests/Tabletop.Geometry.Tests tests/Tabletop.Client.Tests tests/Tabletop.Geometry.Wasm.Tests && cd viewer && npm test`
Expected: PASS; remove the `move/disembark` entries from `pinned` in `calls.agreement.test.ts` and they pass as plain tests.

- [ ] **Step 5: Commit**

```bash
git add libs/Tabletop.Geometry/Containment.cs client/src/Core/MoveRuler.cs libs/Tabletop.Geometry.Wasm viewer/src/lib/geometry viewer/src/lib/play/decisionBox.move.ts tests/Tabletop.Geometry.Tests/ContainmentTests.cs tests/Tabletop.Geometry.Wasm.Tests viewer/tests
git commit -m "Browser: a disembark answers the exit facing at the drop, as Godot does (Containment.ExitYaw)"
```

- [ ] **Step 6: Each remaining pinned case, one commit each**

For each name left in `pinned`: read the case's choice and both messages; find the browser line that builds the differing field. If Godot's message is right, change the browser, add a unit test beside the browser module naming the field, remove the name from `pinned`, run `cd viewer && npm test`, commit `Browser: <what now agrees>`. If the case shows a Godot bug (Godot sends what the server refuses, or what the rulebook rules out), fix the Core builder with a C# test and the browser with a Vitest test, rerun `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter "FullyQualifiedName~CallsViewerFixture"`, and commit both with the regenerated `calls.json`. Task 12 is done when `pinned` is empty and `calls.agreement.test.ts` has no `it.fails`.

### Task 13: The rule in the docs

**Files:**
- Modify: `client/AGENTS.md` (Conventions and pitfalls; the Layout's Core list)
- Modify: `viewer/AGENTS.md` (Layout `tests/`; Conventions and pitfalls)
- Modify: `tests/AGENTS.md` (Layout `Tabletop.Client.Tests`, `Tabletop.Viewer.Tests`; Regenerating checked-in outputs)

- [ ] **Step 1: Write the lines**

`client/AGENTS.md`, Conventions, after the combat log line:

> - Every outbound socket message is built by a Core function (`AskAnswers`, `PiecePick`, `DeployView.Place`, `HandMoves`, `Menus`, `MenuModel`, `CombatLog.UndoHere`, `GameOverView.Answer`, `LobbyEdits`, `SetupEdits`, `LobbyView`, `WaitingView`); a node only calls it. A new outbound message, or a new control that sends one, is not done until `CallsViewerFixtureTests` writes a case for it (`TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CallsViewerFixture`) and `viewer/tests/calls.agreement.test.ts` passes.

and the Core list gains `AskAnswers` (`ExitSite`), `PiecePick`, `HandMoves` (`Call`, `HeldDrop`), `Menus` (`MenuContext`), `LobbyEdits`, `SetupEdits`.

`viewer/AGENTS.md`, `tests/` in Layout: `calls.json` is Godot's calls over the recorded logs and `fixtures/calls/` (ambush, transport, passengers, selfplay, gameover, casting, objectives; written by `tests/Tabletop.Viewer.Tests/CallLogFixtureTests.cs`), written by `tests/Tabletop.Client.Tests/CallsViewerFixtureTests.cs`, which `calls.agreement.test.ts` replays through the decision box and the page builders; regenerate it after the call logs. Conventions:

> - A new outbound message, or a new control that sends one, is not done until it has a case in `tests/fixtures/calls.json` (written from Godot's Core) and `calls.agreement.test.ts` passes; a difference is fixed in the browser, never in the fixture, unless the case shows a Godot bug, which is fixed in both.

`tests/AGENTS.md`: `Tabletop.Client.Tests` also writes `calls.json` (`--filter CallsViewerFixture`); `Tabletop.Viewer.Tests` also writes the call logs (`--filter CallLogFixture`); the regenerate order is call logs, then `calls.json`, then `cd viewer && npm test`.

- [ ] **Step 2: Check nothing still says otherwise**

Run: `grep -rn "SendCommandAsync(new AnswerAsk" client/AGENTS.md docs/design/client` and read each hit; a line that says the decision box builds `new AnswerAsk(...)` itself now names `AskAnswers.Answer`.
Expected: no stale line left.

- [ ] **Step 3: Commit**

```bash
git add client/AGENTS.md viewer/AGENTS.md tests/AGENTS.md docs/design/client
git commit -m "Docs: a new outbound message needs a calls fixture case"
```

Part 4 is done when `calls.agreement.test.ts` passes with nothing pinned. Part 5 (`2026-10-25-same-calls-5-ambush.md`) follows.
