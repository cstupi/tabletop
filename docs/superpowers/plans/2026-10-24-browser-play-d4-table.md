# Browser play D4: header, roster, rings, sight, hand actions, waiting, and a game end to end Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Around the board a player reads the header (round, turn, scores, the activation and its steps, who the game waits
on), the roster, the roll-off result, range rings for the selected unit and the sight check; uses Godot's menus' hand actions
(set aside, transports, grants, wounds, conditions, objective control, score, tokens) and objective locks; sees and acts on the
wait for players; and a test plays a short game from lobby through deployment and a whole round with the browser modules
against the compose stack.

**Architecture:** As D1–D3: Godot core files port one module each to `viewer/src/lib/play/` (`RosterView`, `RollOffBanners`,
`RangeRings`, `SightKey`, `SightSummary`, `MenuModel`), with Godot's tests; `SightLines`' query logic becomes a DOM-free
`sightCheck.ts`; Svelte parts bind. The end-to-end test drives two `GameConnection`s through the same `DecisionBox`
controllers, move hooks and undo prediction the page uses.

**Tech Stack:** Vite 8, Svelte 5, TypeScript 5.9, Vitest 5, Node 24; plan B's `viewer/src/lib/geometry/`; the compose stack for
Task 7.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Goals → every step of a game; What play shows → range
rings, the sight check, the menus' hand actions; Not ported → turn banners beyond a plain line). Prerequisites:
`-d1-decisions.md`, `-d2-moves.md`, `-d3-info.md`; C1 Task 6 (`querySight`); C2 Task 8 (the `test:e2e` script). Godot sources
ported: `client/src/Core/RosterView.cs`, `RollOffBanners.cs`, `RangeRings.cs` (the ring per printed range; the outline is B's
`rangeOutline`), `SightKey.cs`, `SightSummary.cs`, `MenuModel.cs` (play items), `WaitingView.cs` (bound here, ported in D1);
`client/src/Hud/HeaderBar.cs`, `Roster.cs`, `TurnBanner.cs` (the roll-off only); `client/src/Table/SightLines.cs`,
`RangeRingNode.cs`; `client/src/Panels/PieceMenu.cs`, `WaitingPanel.cs`; `client/src/Screens/GameScreen.Play.cs` (`ItemsFor`,
`OpenTeamMenu`, `Token`).

## Findings that shape the plan

- **Where Godot opens each menu** (`GameScreen.Play.ItemsFor`, `OpenTeamMenu`, `Roster.UnitMenuRequested`, `TableInput`): a
  right-click on a model gives `ForBoarding` then `ForModel` and `ForFrame` (Shift: `ForUnit` with the grants); on a marker
  `ForMarker` with the lock item; a team name in the header `ForTeam` then `OffTable`; a roster row `ForRosterUnit`; Alt+right
  on the table `Drop a token here`. A right-click on a model the open Ask lists is the casualty pick's, not a menu (D1).
- **Menu inputs** are `None`, `Integer`, `Text`, `PieceId` (the next piece clicked), `TablePoint` (the next table click),
  `Choice` (a list). `MarkerLocks`' item is local and sends nothing (D2's `itemFor`).
- **Sight** (`SightLines`): the observer is the shooter of my open Assign Ask (its frame's actor) or, while L is held, the
  selected unit; the target the hovered unit; each assigned target is asked again only when its `SightKey` changed. C1's
  `querySight` resolves with the answer, so only the latest hovered query is shown.
- **`RangeRings.Of` is per printed range over B's `rangeOutline`** (the union moved into `Tabletop.Geometry` by B Task 1);
  `RangeRingCache` keys on the unit's models' slots and placements.
- **No army in a fresh dev account:** the end-to-end test imports two lists through `POST /v1/dev/armies/import` (dev only), the
  export bodies the lifecycle uses (`sim/Lifecycle/Armies/z65fgu0l29i4lnlu.json`, Human Defense Force, and
  `1wj1ysgxpuuz9bc7.json`, Orc Marauders: three units each), and deletes them (`DELETE /v1/armies/{id}`) and the game after.

## Global Constraints

- Every hand action is one ordinary command through `sendCommand`; the server refuses what the enforcement setting refuses,
  and the refusal shows Godot's text (`refusalText`). No dice controls (owner ruling: dice are never edited).
- One module per Godot file, members camelCased; Godot's copy byte for byte; Godot's tests as Vitest cases.
- A watcher sees the header, roster, rings and sight, and gets no menu, no Join, no army picker.
- The end-to-end suite skips unless `TABLETOP_E2E=1` (`describe.skipIf`), so `npm test` stays offline.
- Files under 500 lines.
- Commands: `cd viewer && npm test`, `npm run check`, `npm run build:hosted`; `docker compose up -d`, `./scripts/geometry-wasm`,
  `cd viewer && npm run test:e2e`.

## Review Focus

- A menu item on a model of another team or a watcher: Godot offers the model menu to any participant (hand edits are judged by
  the server), the roster menu only for my team, and nothing to a watcher. Test: `a watcher gets no menu and the roster menu is
  my team's only` (Task 5).
- Sight queries do not pile up: an assigned target is asked again only after its key changed. Test: `cache asks a new pair once,
  then only after a change, and drops unassigned pairs` (Task 3).
- The waiting panel keeps an open army picker across unrelated frames. Test: `the waiting panel rebuilds only when its lines
  change` (D1 Task 3, bound in Task 6).
- The end-to-end game reaches round 2 with every unit of both teams deployed and activated in round 1, both clients agreeing on
  the last seq. Test: `a game goes from lobby through deployment and round 1 from the browser modules` (Task 7).

---
### Task 1: The header, the roster and the roll-off banner

**Files:** create `viewer/src/lib/play/rosterView.ts`, `play/rollOffBanners.ts`, `play/headerText.ts`, `play/HeaderBar.svelte`,
`play/Roster.svelte`, `play/Banner.svelte`; modify `viewer/src/lib/play/PlayPage.svelte`; test `viewer/tests/rosterView.test.ts`,
`viewer/tests/rollOffBanners.test.ts`, `viewer/tests/headerText.test.ts`.

**Interfaces:**

```ts
// rosterView.ts — RosterView (ReserveOf is D3's rosterReserve.ts)
export interface RosterRow { readonly unitId: string; readonly name: string; readonly activated: boolean; readonly left: number;
  readonly max: number; readonly offTable: boolean; readonly conditions: readonly string[]; readonly inside: string | null;
  readonly reserve: string | null }
export interface RosterTeam { readonly teamId: string; readonly name: string; readonly ordinal: number; readonly rows: readonly RosterRow[] }
export function rosterOf(p: Projection, game: GameInfo, n: Names, myTeam: string | null, maxWounds: ReadonlyMap<string, number>): RosterTeam[];
export function pips(left: number, max: number): string;   // ● and ○
export function words(name: string): string;               // first letter capitalised
export const woundsText: (r: RosterRow) => string;        // pips up to 12 wounds, else `left/max` (Roster.cs)
// rollOffBanners.ts
export interface Banner { readonly text: string; readonly seconds: number; readonly detail: string | null }
export const SECONDS = 4;
export function rollOffBannerOf(batch: readonly GameEvent[], n: Names, catchingUp: boolean): Banner | null;
// headerText.ts — the lines HeaderBar.cs builds
export const scoreText: (name: string, score: number) => string;        // `  {name} {score}`
export const notActivatedText: (count: number) => string;               // ` · {n} not activated`
export const outlineText: (item: { label: string; state: OutlineState }) => string;   // `  ✓ {label}` when done
/** HeaderBar's third line: the wait for players, else the watcher's line, else the open Asks' waiting line. */
export function waitingLine(p: Projection, n: Names, waiting: readonly TeamWaiting[], watching: boolean, mine: readonly string[]): string | null;
```

The banner has no setting in the browser (Godot's `TurnBanner` toggle): it shows whenever a live batch carries the ranking.
`HeaderBar.svelte` draws `headerOf`'s top line, each team's score in its colour (right-click: the team menu, Task 5) with its
not-activated count, the objective chips coloured by controller (`objectives`), the result (a button that reopens the Game over
modal), the activation line (`activationLine`) with its outline strip, and `waitingLine`. `Roster.svelte` (R toggles it) lists
`rosterOf` with the acting team first: activated mark, `woundsText`, conditions as chips, `in {Transport}`, the reserve label,
off-table rows dimmed; a click selects the unit; a right-click opens the roster menu (Task 5). `Banner.svelte` shows `text` and
`detail` for `seconds`.

- [ ] Tests first: `rosterView.test.ts`: RosterViewTests' one, PassengerMenuTests' `the roster marks a passenger and dims it while
  its Transport is off the table`, `wounds read as pips up to twelve, else as a count`; `rollOffBanners.test.ts`:
  RollOffBannersTests' four (the switched-off arm becomes `nothing while catching up, after an undo or without a ranking`);
  `headerText.test.ts`: `the wait for players comes first, then the watcher's line, then the open Asks`, `a done step is ticked`.
- [ ] `cd viewer && npx vitest run tests/rosterView.test.ts tests/rollOffBanners.test.ts tests/headerText.test.ts` fails.
- [ ] Implement the modules, the three components, and the page wiring (`onEvents` feeds the banner, silent while `catchingUp`).
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the header, the roster and the roll-off banner in play`.

### Task 2: Range rings for the selected unit

**Files:** create `viewer/src/lib/play/rangeRings.ts`; modify `viewer/src/lib/play/PlayPage.svelte` (V, the overlay); test
`viewer/tests/rangeRings.test.ts`, `viewer/tests/rangeRings.wasm.test.ts`.

**Interfaces:**

```ts
// rangeRings.ts — RangeRings and RangeRingCache
export interface RangeRing { readonly inches: number; readonly label: string; readonly segments: readonly Segment[] }
export function inches(printed: string | null | undefined): number | null;   // `24"` → 24; '-', '0', none → null (melee)
export function rangeRingsOf(g: Geometry, p: Projection, unitId: string): RangeRing[];   // per distinct printed range, ascending
export class RangeRingCache { of(g: Geometry, p: Projection, unitId: string): readonly RangeRing[] }   // recomputed on a key change
export const ringsOverlay: (rings: readonly RangeRing[], colour: string) => Overlay;   // each outline and its label
```

Rings show for the card's unit when `settings.rangeRings` is on (V toggles and saves it), in the unit's team colour.

- [ ] Tests first: `rangeRings.test.ts` (fake geometry): `inches reads the printed number and treats melee as none`; `one ring per
  distinct printed range around the models carrying it` (RangeRingsTests' first, asserting the `rangeOutline` calls); `the cache
  recomputes only when the unit or its placements change`. WASM: `a lone 1" base at range 6 is one closed ring 6.5" out`.
- [ ] `./scripts/geometry-wasm && cd viewer && npx vitest run tests/rangeRings.test.ts tests/rangeRings.wasm.test.ts` fails.
- [ ] Implement and wire.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: range rings for the selected unit, through Geometry.rangeOutline`.

### Task 3: The sight check

**Files:** create `viewer/src/lib/play/sightKey.ts`, `play/sightSummary.ts`, `play/sightCheck.ts`; modify
`viewer/src/lib/play/PlayPage.svelte` (L held, the overlay, dimmed models); test `viewer/tests/sightKey.test.ts`,
`viewer/tests/sightSummary.test.ts`, `viewer/tests/sightCheck.test.ts`.

**Interfaces:**

```ts
// sightKey.ts — SightKey and SightCache
export class SightKey { static of(p: Projection, from: string, to: string): SightKey; equals(o: SightKey | null): boolean }
export class SightCache { needsAsk(target: string, key: SightKey): boolean; asked(target: string, key: SightKey): void;
  forget(target: string): void; keep(targets: Iterable<string>): void }
// sightSummary.ts — SightSummary
export function summaryText(answer: SightAnswer, target: string, shooting: boolean): string;
export function dimmed(p: Projection, hovered: SightAnswer | null, groups: readonly AskOption[], assigned: ReadonlyMap<string, string>,
  answers: ReadonlyMap<string, SightAnswer>): Set<string>;
// sightCheck.ts — SightLines' Shooter, Ask, AskAssigned, OnAnswer and Draw, without Godot
export interface SightView { readonly lines: readonly { from: XY; to: XY; tone: 'clear' | 'screened' | 'blocked' }[];
  readonly blockers: readonly string[]; readonly summary: { at: XY; text: string } | null; readonly dimmed: ReadonlySet<string> }
export const SIGHT_COLOURS: Record<'clear' | 'screened' | 'blocked', string>;   // SightLines' (0.2,0.9,0.3), (1,0.75,0.1), (0.95,0.15,0.15)
export class SightCheck {
  constructor(query: (observer: string, target: string) => Promise<SightAnswer>);
  /** Re-reads observer and target (the shooter of my open Assign Ask, else the selected unit while L is held; the hovered unit),
   *  asks when they changed, and asks each assigned target whose key changed; a failed query is forgotten. */
  update(p: Projection, game: GameInfo, box: DecisionBox, hovered: string | null, selected: string | null, sightHeld: boolean): void;
  view(p: Projection, game: GameInfo, n: Names, shooting: boolean): SightView;
  subscribe(listener: () => void): () => void;
}
```

Only the newest hovered query's answer is drawn (a counter, since `querySight` resolves by its own id); assigned answers stand
until their key changes. The overlay draws each line between the two models' centres in its tone, outlines blockers (model
circles, or the volume's outline) in the blocked colour at 35 %, and writes the summary over the target; `dimmed` models draw at
reduced opacity (a `DrawOptions.dimmed` set added to `draw.ts`).

- [ ] Tests first: `sightKey.test.ts`: SightKeyTests' seven; `sightSummary.test.ts`: SightSummaryTests' two; `sightCheck.test.ts`
  (a fake query): `under a target pick the shooter observes the hovered unit`; `with L held the selected unit observes`; `nothing is
  asked of a unit about itself or with no observer`; `only the newest hovered answer is drawn`; `an assigned target is asked again
  only after its key changed`; `a failed query is asked again next time`; `the view colours lines by clear, screened and blocked`.
- [ ] `cd viewer && npx vitest run tests/sightKey.test.ts tests/sightSummary.test.ts tests/sightCheck.test.ts` fails.
- [ ] Implement, `dimmed` in `draw.ts`, and the page wiring (L keydown and keyup).
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the sight check — the server's lines, the summary and who cannot shoot`.

### Task 4: The menus' hand actions

**Files:** create `viewer/src/lib/play/menuModel.ts`; test `viewer/tests/menuModel.test.ts`.

**Interfaces:**

```ts
// menuModel.ts — MenuModel's play items (setup's are C2's setup/edits.ts)
export type MenuInput = 'none' | 'integer' | 'text' | 'pieceId' | 'tablePoint' | 'choice';
export interface MenuItem { readonly label: string; readonly input: MenuInput; readonly choices: readonly string[];
  /** The command the input builds; null when it does not parse. A local item (a marker lock) builds nothing and applies itself. */
  build(input: string | null): Command | null; readonly local?: () => void }
export function forUnit(p: Projection, unitId: string, actionNames: readonly string[], grantable?: readonly GrantChoice[]): MenuItem[];
export function forModel(p: Projection, modelId: string, actionNames: readonly string[]): MenuItem[];
export function offTable(p: Projection, teamId: string, n: Names): MenuItem[];
export const SET_ASIDE = 'set-aside';
export function forRosterUnit(game: GameInfo, p: Projection, unitId: string, myTeam: string | null,
  deployInside?: ReadonlyMap<string, readonly string[]>): MenuItem[];
export function forBoarding(p: Projection, ask: Ask | null, modelId: string): MenuItem[];
export function forTeam(game: GameInfo, p: Projection, teamId: string): MenuItem[];
export function forMarker(game: GameInfo, p: Projection, markerId: string, locks?: MarkerLocks): MenuItem[];
export function forFrame(p: Projection): MenuItem[];
export function knownActionNames(p: Projection, log: readonly GameEvent[]): string[];
/** GameScreen.Play.Token: Alt+right-click on the table. */
export function token(at: Point3): MenuItem;
```

A `tablePoint` input is `"x,y,z"` with invariant numbers, as `MenuModel.Point` parses it. New ids (`AddGrant`'s grant,
`token`'s marker) are `crypto.randomUUID()`.

- [ ] Tests first, Godot's cases: ViewModelTests' `menu items build the commands the spec lists`, `the team menu lists off table
  models of that team only`, `an objective from setup or play can be given to a team or made neutral and a token cannot`;
  RosterMenuTests' two; PassengerMenuTests' `a shelved unit is offered each Transport the server named`, `a unit inside a Transport
  on the shelf may be taken out`, `board transport answers my move Ask with every model inside`; AddGrantMenuTests' `the unit menu
  adds a grant of the chosen effect to the unit with one use`; MarkerLocksTests' `the marker menu carries the lock item last`;
  `a moved setup objective is still not removable`; `the token item drops a labelled chip where clicked`.
- [ ] `cd viewer && npx vitest run tests/menuModel.test.ts` fails.
- [ ] Port.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the menus' hand actions, ported from MenuModel`.

### Task 5: The menus on the page

**Files:** create `viewer/src/lib/play/menus.ts`, `play/PieceMenu.svelte`; modify `viewer/src/lib/play/PlayPage.svelte`,
`play/HeaderBar.svelte`, `play/Roster.svelte`; test `viewer/tests/menus.test.ts`.

**Interfaces:**

```ts
// menus.ts — where GameScreen.Play opens each menu, and what an item's input needs before it sends
export interface MenuContext { readonly game: GameInfo; readonly p: Projection; readonly log: readonly GameEvent[];
  readonly watching: boolean; readonly myTeam: string | null; readonly mine: AskView | null; readonly grantable: readonly GrantChoice[];
  readonly deployInside: ReadonlyMap<string, readonly string[]>; readonly locks: MarkerLocks }
/** ItemsFor: a model (boarding, then the model's items and the frame's; Shift the unit's), else a marker; [] for a watcher. */
export function pieceMenu(ctx: MenuContext, id: string, shift: boolean): MenuItem[];
export function teamMenu(ctx: MenuContext, teamId: string, n: Names): MenuItem[];    // ForTeam then OffTable
export function rosterMenu(ctx: MenuContext, unitId: string): MenuItem[];
export function tableMenu(ctx: MenuContext, at: Point3): MenuItem[];                 // the token, Alt+right-click
/** What choosing an item asks for next: send now, a dialog (integer, text, choice), or the next piece or table click. */
export type MenuStep = { kind: 'send'; command: Command } | { kind: 'local' } | { kind: 'dialog'; input: 'integer' | 'text' | 'choice';
  choices: readonly string[] } | { kind: 'pick'; what: 'piece' | 'table' } | { kind: 'invalid' };
export function choose(item: MenuItem, input: string | null): MenuStep;
```

`PieceMenu.svelte` is `PieceMenu.cs` at the pointer: the items, Esc or a click outside closes it; an integer or text item asks in
a small dialog (focused, Enter confirms, text stops at 64 characters); a choice item lists its choices; a `pieceId` or
`tablePoint` item takes the next board click (`BoardInteraction.onSelect`) before anything else. `PlayPage` opens it from a
right-click on a model or marker not taken by the casualty pick, from Alt+right-click on the table, from a header team name and a
roster row; a sent command's refusal shows its `refusalText` in the page's alert line.

- [ ] Tests first: `a watcher gets no menu and the roster menu is my team's only`; `a model's menu leads with boarding and ends with
  the frame's abandon`; `shift gives the unit's menu with its grants`; `a marker's menu ends with the lock item`; `choose sends a
  None item at once, asks for an Integer and refuses a bad one`; `a table point item waits for a table click`; `a lock item
  applies locally and sends nothing`.
- [ ] `cd viewer && npx vitest run tests/menus.test.ts` fails.
- [ ] Implement the module, the component and the wiring.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the piece, team, roster and table menus in play`.

### Task 6: Waiting for players and the teams I play

**Files:** create `viewer/src/lib/play/WaitingPanel.svelte`; modify `viewer/src/lib/play/PlayPage.svelte`; test
`viewer/tests/waiting.test.ts` (one case).

`WaitingPanel` is `WaitingPanel.cs` over D1's `waitingLines`: `WAITING_TITLE`, one line per team, Join (the `TeamChoice`'s
`PickTeam` edit through `sendEdit`) on a team no one holds, and on a team of mine with no army an army picker over
`api.listArmies()` (`Pick an army`, then `pickArmy(seat, id)` from C2). `PlayPage` shows it while `waitHolds` and keeps the
same component across frames, re-rendering its rows only when `linesChanged`; the decision box and header already read the
wait (D1, Task 1). "Playing as Team N" (`playingAs`) heads the decision box (D1) and the Game over modal, and the header for an
account holding several teams; everything that follows the acting team (box, card, roster order, rings) reads `acting(...)`.

- [ ] Test first: `a Join line becomes an army line once the board holds my seat, and an unchanged frame keeps the lines`
  (`linesChanged` false for equal lines built from two different `GameInfo` objects).
- [ ] `cd viewer && npx vitest run tests/waiting.test.ts`: it passes on D1's port; it guards the panel's reuse (a failure means
  `linesChanged` compares by reference and is fixed there).
- [ ] Implement the panel and the wiring; `Playing as Team N` in the modal and the header.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: waiting for players — join an empty team, pick an army; Playing as Team N`.

### Task 7: A game end to end against the stack, and the docs

**Files:** create `viewer/tests/e2e/play.e2e.test.ts`, `viewer/tests/e2e/driver.ts`; modify `viewer/AGENTS.md`,
`docs/design/viewer/design.md`, `client/AGENTS.md` (the fixture's rule).

**Interfaces:**

```ts
// driver.ts — one seat played through the page's own modules
export interface Seat { readonly name: string; readonly conn: GameConnection; readonly api: Api; readonly box: DecisionBox;
  readonly undo: UndoPrediction }
/** One step for the seat if its box shows an Ask or a deploy placement: choose (an option labelled Hold when there is one, else
 *  key 1), move (commit where the ghosts stand), deploy pick (key 1) and placement (placeAt the centre of the team's zone, then
 *  Enter), pick pieces (the first eligible, then Enter), assign (the first candidate, then Enter; Pass when none), allocate
 *  (Auto, then Enter), token spend and confirm (Enter). Resolves after the send settles; false when there was nothing to do. */
export function step(seat: Seat, g: Geometry): Promise<boolean>;
export function zoneCentre(g: Geometry, table: TableState, ordinal: number): Point3;   // the first polygon's centroid
```

The test signs in two dev accounts (`POST http://127.0.0.1:5100/v1/dev/sessions`), imports one list each through `POST
/v1/dev/armies/import` (`{ source: 'e2e-browser', list }`, the files in `sim/Lifecycle/Armies/`), and plays:

- [ ] Write `play.e2e.test.ts`: `a game goes from lobby through deployment and round 1 from the browser modules` — A
  `createGame` (first rule set, alternating, warn), B `joinGame`; both `connectGame` on `ws://127.0.0.1:5200/ws`; teams and
  armies as C2's test does, plus `PickArmy` each; A `StartSetup`, loads `builtinLayouts()[0]` with `loadEdit`, `EndSetup`; both
  reach `playing` with no wait (`waitHolds` false). Then, in a bounded loop (600 steps), each seat whose box has work takes a
  `step`; after A's first deploy placement lands, A presses Undo (`undoPlanOf` → `undo.undoTo(plan.toSeq)`) → both logs carry the
  `undone`, A's unit is mid-deploy again (`midDeploy` names it), and the loop goes on. The loop ends when the projection's round
  is 2. Assert: every model of both armies went from the shelf to the table before `round_started` 1; every unit with a model on
  the table when round 1 ended has an `action_recorded` between `round_started` 1 and 2; both connections' `projection.lastSeq`
  agree; the combat log
  (`build`) holds an `R1 · Team 1 · …` and an `R1 · Team 2 · …` header, `headerOf(...).round` is 2, and no `onRefused` fired
  except one deliberate case: B answers A's open Ask's id → `refused` with Godot's text. `afterAll` deletes the game (A) and both
  armies (`DELETE /v1/armies/{id}`).
- [ ] Run `docker compose up -d`, `./scripts/geometry-wasm`, then `cd viewer && npm run test:e2e`: passes. A failure is fixed in
  the module at fault (or reported as an engine defect), never by loosening an assertion.
- [ ] Docs. `viewer/AGENTS.md`: Layout gains `src/lib/play/` (the decision box and its move hooks, the drag, the measures, the
  log, the card, undo, header, roster, rings, sight, menus, waiting; `PlayPage`); How it works: play beside setup in `GamePage`,
  every answer an `AnswerAsk` and every hand action a command, the board's interaction in play, the undo prediction, settings in
  `localStorage` `tabletop.play`; Conventions: a change to Godot's `CombatLog`, `RollEntry` or `UndoPlan` regenerates
  `viewer/tests/fixtures/combatlog.json` (`TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter
  CombatLogViewerFixture`) and is ported to `src/lib/play/` until `combatLog.agreement.test.ts` passes; a change to any other
  ported core file is ported with its test. `client/AGENTS.md` Conventions: the same rule from Godot's side.
  `docs/design/viewer/design.md`: the play structure, current state only. Grep `viewer/`, `docs/design/` for `PlaybackView over
  the connection`, `until plan D lands` and fix each.
- [ ] `cd viewer && npm test && npm run check && npm run build:hosted`; `dotnet test tests/Tabletop.Client.Tests`.
- [ ] Commit: `Viewer: a game from lobby through round 1 validated from the browser modules; docs`.
