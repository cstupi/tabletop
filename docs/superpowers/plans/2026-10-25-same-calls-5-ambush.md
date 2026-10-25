# Same Calls, Part 5: Ambush in the Browser, End to End — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A stack test sets a unit aside from the roster menu, plays to the round boundary and places the Ambusher through the decision box: refused within 9" of an enemy, accepted over 9"; then the same with one account holding both teams.

**Architecture:** `viewer/tests/e2e/ambush.e2e.test.ts` over the running stack, through the page's own modules: `harness.ts` (sessions, army import, `playingGame`, `playerOf`, `until`), `driver.ts` (`step`), `menus.ts` (`rosterMenu`, `choose`) and the decision box with D2's move hooks. The army is lifecycle book `z65fgu0l29i4lnlu` (Human Defense Force) with Ambush added to its Veterans, as `sim/Lifecycle/LifecycleArmy.cs` adds it to book 0's Beast Riders.

**Tech Stack:** TypeScript, Vitest, the compose stack (`docker compose up -d`, both servers in dev mode), the test WebAssembly geometry.

**Spec:** `docs/superpowers/specs/2026-10-25-same-calls-design.md`

**Build order:** last (Task 14). It needs none of parts 1–4, but a failure it finds is fixed against the agreed builders.

## Global Constraints

- "Ambush in the browser, end to end: a stack test (`viewer/tests/e2e`, `TABLETOP_E2E=1`) that sets a unit aside from the roster menu, plays to the round boundary, and places the Ambusher through the decision box: a placement within 9" of an enemy refused (`ambush_distance`) and one over 9" accepted; and the same with one account holding both teams."
- Rulebook p.13 Ambush: "At the start of any round after the first, may be deployed anywhere over 9” away from enemy units." The server's breach is `ambush_distance` (`GfNames.AmbushDistanceRule`); under strict it is a `Refused` with reason `illegal` and the detail `{model} would be placed within 9" of an enemy unit: Ambushers deploy over 9" away from enemy units (p.13)` (`GfRules.AmbushBreach`). The `Refused` carries the detail, not the rule's name, so the test reads the detail.
- The suites sign in fixed accounts (`viewer-e2e-<label>-<seat>`), sweep before they start and delete every game and army in `finally`.
- Skipped without `TABLETOP_E2E=1`; run with `cd viewer && npm run test:e2e` after `./scripts/geometry-wasm`.

## Review Focus

- The roster's Set aside is offered only for the acting team's units (`forRosterUnit(game, p, unitId, myTeam)`); with one account holding both teams the test opens it while team 1 acts, as a player would, and asserts it is offered then.
- A deploy pick that still lists the set-aside unit: the test never picks it before the round boundary, and asserts no `deploy` Ask's answer names it.
- The refused placement leaves the Ambusher's models on the shelf and the box over the same unit with the server's words; the accepted one puts every model on the table in round 2 or later.
- One account, two teams: the Ambusher pick and placement say "Playing as Team 1", and the logged `AskAnswered` names team 1.
- Cleanup on failure: every path through the test reaches `cleanup()` (the `try`/`finally` shape `play.e2e.test.ts` uses).

---

### Task 14: `ambush.e2e.test.ts`

**Files:**
- Modify: `viewer/tests/e2e/harness.ts` (`importArmy` takes an Ambush unit; `playingGame` passes it for seat A; add `oneAccountGame`)
- Create: `viewer/tests/e2e/ambush.e2e.test.ts`
- Modify: `viewer/AGENTS.md` (Commands → `npm run test:e2e`: name the new suite)

**Interfaces:**
- Consumes: `session`, `sweep`, `importArmy`, `waitFor`, `playerOf`, `until`, `accountName`, `BOOKS`, `SOCKET`, `MANAGEMENT` (`harness.ts`); `step`, `Seat` (`driver.ts`); `rosterMenu`, `choose` (`play/menus.ts`); `acting`, `teamsOf` (`lobby/seats.ts`); `mine` (`play/askView.ts`); `MarkerLocks`; `loadTestGeometry`; `UndoPrediction`.
- Produces:
  ```ts
  // harness.ts
  export const AMBUSHER = 'Veterans';
  /** The book's list; `ambush` names a unit that also carries Ambush (p.13), added as the list names its rules. */
  export async function importArmy(jwt: string, book: string, ambush?: string): Promise<string>;
  export async function playingGame(label: string, enforcement?: 'strict' | 'warn' | 'silent',
      options?: { layout?: boolean; ambush?: string }): Promise<PlayingGame>;     // `ambush` goes on seat A's army
  /** One account takes both teams, an army each (team 1's carrying `ambush`), and ends setup: one connection, playing. */
  export async function oneAccountGame(label: string, options?: { ambush?: string }):
      Promise<{ a: GameConnection; t1: Team; t2: Team; jwt: string; api: Api; cleanup: () => Promise<void> }>;
  ```
  `importArmy` with `ambush` reads the list, appends `{ name: 'Ambush', label: 'Ambush' }` to the `rules` of the first unit of that name (throwing if none), and posts it. `oneAccountGame` follows `playingGame`'s steps on one connection: `create_team` until two teams, `pick_team` team 1 then team 2 (the second keeps the first: `lobbyView.ts` `teamChoices`, "You keep your other team and play both sides"), `pick_army` with each team's id, `start_setup`, `end_setup`, then waits for `playing`.

The test file's helpers (local to it):

```ts
/** The roster's Set aside for `unit`, sent as the page sends it; resolves once the projection carries the condition. */
async function setAside(conn: GameConnection, unit: string): Promise<void>;
/** One step, but a deploy pick never takes `avoid`: it presses the first other `pick:` control and Enter. */
async function stepAvoiding(seat: Seat, g: Geometry, avoid: string): Promise<boolean>;
/** Spots on the table, nearest first to `from`, for the Ambusher's placement click. */
function spots(size: TableSize, from: XY): XY[];
/** Every ghost's base over `reach` inches, edge to edge, from every enemy model on the table (Geometry.edgeToEdge). */
function clearOfEnemies(g: Geometry, p: Projection, ghosts: ReadonlyMap<string, Placement>, enemyTeam: string, reach: number): boolean;
```

`setAside` builds a `MenuContext` from the connection's state (`myTeam` = `acting(p, teamsOf(game, participantId), log)`), takes `rosterMenu(ctx, unit)`, expects an item labelled `Set aside`, and sends `choose(item, null)`'s command; it expects `refused` null and waits until `p.units.get(unit)!.conditions` includes `set-aside`.

- [ ] **Step 1: Write the test, two accounts**

```ts
describe.skipIf(process.env.TABLETOP_E2E !== '1')('Ambush in the browser', () => {
  it('sets a unit aside, then places it at the round boundary: refused within 9", accepted over 9"', async () => {
    const g = await loadTestGeometry();
    const game = await playingGame('ambush', 'strict', { ambush: AMBUSHER });
    const { a, b, t1, t2 } = game;
    try {
      const unit = unitNamed(a, t1.id, AMBUSHER);                 // the board's unit of A's army with that name
      expect(propertyNames(a, unit)).toContain('Ambush');          // the import kept the rule
      await setAside(a, unit);

      const seatA = seatOf('A', a, game.apiA, g), seatB = seatOf('B', b, game.apiB, g);
      const boundary = () => openAsk(a, 'deploy_ambusher', t1.id);
      for (let i = 0; i < 600 && boundary() === undefined; i++) {
        const acted = (await stepAvoiding(seatA, g, unit)) || (await step(seatB, g));
        if (!acted) await sleep(100);
      }
      expect(boundary()).toBeDefined();
      expect(a.state.projection!.scheduler.round).toBeGreaterThanOrEqual(2);
      expect(deployAnswersNaming(a, unit, 'deploy')).toEqual([]);  // no deploy pick named it before the boundary

      // The pick: the Ambusher is a numbered entry; its key picks it, and the box waits for its placement.
      await pickUnit(seatA.box, unit);
      await until('the Ambusher mid-deploy', () => seatA.box.deployUnit === unit);

      // Within 9": next to an enemy model. The server refuses; the models stay on the shelf and the box shows its words.
      const enemy = firstOnTable(a, t2.id);
      seatA.box.placeAt({ x: enemy.x + 3, y: enemy.y, z: 0 });
      seatA.box.enter();
      await until('the refusal', () => seatA.box.panel.refusal !== null);
      expect(seatA.box.panel.refusal).toMatch(/would be placed within 9" of an enemy unit: Ambushers deploy over 9" away from enemy units \(p\.13\)/);
      expect(modelsOf(a, unit).every((m) => m.location.kind === 'shelf')).toBe(true);
      expect(seatA.box.deployUnit).toBe(unit);

      // Over 9": the first spot whose every ghost clears every enemy base by more than 9".
      seatA.box.cancel();
      for (const s of spots(a.state.projection!.board.table.size, { x: enemy.x, y: enemy.y }).reverse()) {
        seatA.box.placeAt({ ...s, z: 0 });
        if (seatA.box.ghosts.size > 0 && clearOfEnemies(g, a.state.projection!, seatA.box.ghosts, t2.id, 9.5)) break;
        seatA.box.cancel();
      }
      seatA.box.enter();
      await until('the Ambusher on the table', () => modelsOf(a, unit).every((m) => m.location.kind === 'table'));
      expect(seatA.box.panel.refusal).toBeNull();
    } finally {
      await game.cleanup();
    }
  }, 240_000);
```

`unitNamed`, `propertyNames`, `seatOf` (as `play.e2e.test.ts`'s `mk`), `openAsk(conn, question, team)`, `deployAnswersNaming(conn, unit, question)` (the `AskAnswered` of that question whose `SelectedPieces` hold the unit), `pickUnit(box, unit)` (press `pick:<unit>`; when the pick is not one of one, Enter), `firstOnTable(conn, team)`, `modelsOf(conn, unit)` and `sleep` are small local helpers over the connection's state.

- [ ] **Step 2: The same with one account holding both teams**

```ts
  it('one account holding both teams places its Ambusher as team 1', async () => {
    const g = await loadTestGeometry();
    const game = await oneAccountGame('ambush-solo', { ambush: AMBUSHER });
    const { a, t1, t2 } = game;
    try {
      const unit = unitNamed(a, t1.id, AMBUSHER);
      const seat = seatOf('solo', a, game.api, g);
      const actingTeam = () => acting(a.state.projection, teamsOf(a.state.game!, a.state.participantId), a.state.log);
      // The roster offers Set aside for team 1's unit while team 1 acts.
      for (let i = 0; i < 200 && actingTeam() !== t1.id; i++) if (!(await stepAvoiding(seat, g, unit))) await sleep(100);
      expect(actingTeam()).toBe(t1.id);
      await setAside(a, unit);

      const boundary = () => openAsk(a, 'deploy_ambusher', t1.id);
      for (let i = 0; i < 900 && boundary() === undefined; i++) if (!(await stepAvoiding(seat, g, unit))) await sleep(100);
      expect(boundary()).toBeDefined();
      expect(seat.box.panel.seat).toBe('Playing as Team 1');

      const askId = boundary()!.id;
      await pickUnit(seat.box, unit);
      await until('the Ambusher mid-deploy', () => seat.box.deployUnit === unit);
      expect(answeredBy(a, askId)).toBe(t1.id);                  // the logged AskAnswered names team 1
      await placeRefusedThenAccepted(seat, a, g, unit, t2.id);
    } finally {
      await game.cleanup();
    }
  }, 300_000);
});
```

Write the placement half once as `placeRefusedThenAccepted(seat, conn, g, unit, enemyTeam)` and call it from both tests; `answeredBy(conn, askId)` reads the team of the `ask_answered` event of that Ask; the Ask is closed after the pick, so its id is taken before.

- [ ] **Step 3: Run it**

Run: `docker compose up -d && ./scripts/geometry-wasm && cd viewer && TABLETOP_E2E=1 npx vitest run tests/e2e/ambush.e2e.test.ts`
Expected: both tests PASS. A failure is a finding: a server refusal of the over-9" placement, a Set aside not offered, an Ambusher never asked, or an answer for the wrong team is fixed where it lies (server rule, browser builder, or both clients when the calls fixture shows Godot does the same), with a test, before this task is done.

- [ ] **Step 4: Run the whole e2e suite and the unit suite**

Run: `cd viewer && npm run test:e2e && npm test`
Expected: PASS; no game, army or layout left by the suites (each sweeps in `finally`).

- [ ] **Step 5: Commit**

```bash
git add viewer/tests/e2e/harness.ts viewer/tests/e2e/ambush.e2e.test.ts viewer/AGENTS.md
git commit -m "Viewer e2e: Ambush through the browser, two accounts and one account holding both teams"
```
