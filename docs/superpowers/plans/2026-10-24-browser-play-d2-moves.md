# Browser play D2: moves, deploys and hand moves Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the 2D board a player answers a move Ask and places a deploy pick by dragging circles that land solid as Godot's
do, sees each moving model's allowance as a circle and a ruler, sees contact and violation marks before committing, is held by
the gate under strict, commits on Enter, gets a refused commit back where it was dropped with the server's reason, and moves
models and markers by hand outside an Ask under the game's enforcement.

**Architecture:** Godot's move half of the decision box (`client/src/Hud/DecisionBox.Move.cs`) ports to
`play/decisionBox.move.ts`, the `MoveHooks` D1 left open; the drag (`TableInput`'s model drag over `DragBodies.Models`) ports to
`play/modelDrag.ts` over plan B's `Geometry.landModels`; the measures (`ContactCheck`, `Violations`, `MoveRuler`, `CommitHold`,
`GhostStash`, `Formation`, `PendingDrops`, `MarkerLocks`) port one module each. `PlayPage` drives the board through C1's
`BoardInteraction` and draws rings, rulers, landings and marks as `Overlay`s.

**Tech Stack:** Vite 8, Svelte 5, TypeScript 5.9, Vitest 5, Node 24; plan B's `viewer/src/lib/geometry/` (WebAssembly).

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Coordinator's calls → Drags are solid, Heights, Moves in
the browser; What play shows → contact and violation marks). Prerequisites: `-d1-decisions.md` (Tasks 1–7), plan B Task 5
(`Geometry`, `wallsOf`, `placed`), C1 Task 7. Godot sources ported: `client/src/Core/ContactCheck.cs` (its words; the geometry is
B's `Contacts.Of`), `Violations.cs`, `MoveRuler.cs` (`Read`, `Gate`, `UsedInFrame`, `IsDisembark`, `IsShelfPlacement`),
`CommitHold.cs`, `GhostStash.cs`, `Formation.cs`, `DragBodies.cs` (`Stationary`, `Grabs`, `Models` lifted and free),
`PendingDrops.cs`, `MarkerLocks.cs`; `client/src/Hud/DecisionBox.Move.cs`; `client/src/Table/TableInput.cs` (`MayDrag`,
`OnLeftUp`), `RulerNode.cs` (the readout).

## Findings that shape the plan

- **No reach outline, by the spec.** `MoveRegions` (`Outline`, `Clamp`, `Route`, `Allowance`, `Minimum`) is not ported and B
  exports no `Movement.Region`. So a target is not clamped to its reach, the ruler is straight (start centre to landing, GF p.7's
  displacement, `MoveRuler.Read(start, current, …)`), the allowance is the Ask's (`MoveRegion.allowance`, already what the
  segment has left), the minimum is the Ask's `minDistance`, and the difficult cap is the server's to judge.
- **The disembark region is not ported either** (`MoveRuler.ExitRegion`, `ExitYawAt` sweep `Movement.Check` and
  `Containment.BaseFullyWithin`, which B does not export). The coordinator's brief: shelf placements *and* disembarks start in
  `DeployView.around` rings about the anchor or container; each answers with its ghost's own facing, and the server judges p.15.
- **Godot turns held models at 120°/s while Q/E is down.** The browser turns per keydown as C2's setup does: ±15°, Shift ±30°,
  counter-clockwise positive, key repeat included.
- **Contacts are B's `Geometry.contacts`** (the moved `Contacts.Of`): D2 builds its `ContactBody` rows and `ContactTerms` from
  the projection and the Ask as `ContactCheck.Move`/`Placement` do, without `unit` (no cap), and keeps `ContactCheck.Words`.
- **`sendCommand` settles with its own `Refused`,** so a command's refusal reaches its sender directly. `CommitHold` keeps its
  shape with a local token per send in place of the command id: every send the box makes calls `sent(token)`.
- **Heights:** every drop lands lifted (`landModels`, or `restModel` without a level under Alt): no level keys (spec).

## Global Constraints

- The browser judges nothing: the gate disables a commit only under `strict`, as Godot's; `warn` and `silent` send and the
  server judges. Every move goes as `AnswerAsk(Destinations)` or `MoveModels`/`MoveMarker`.
- One module per Godot file, members camelCased, bodies ported line for line; Godot's copy byte for byte.
- `src/lib/play/` takes a `Geometry` as a parameter; tests use a fake that records calls and, where named, B's
  `loadTestGeometry()`.
- Files under 500 lines.
- Commands: `./scripts/geometry-wasm`, `cd viewer && npm test`, `npm run check`, `npm run build:hosted`.

## Review Focus

- A refused commit comes back where it was dropped with the server's reason on the models it moved, until one of them is dragged.
  Test: `a refused commit returns its ghosts marked with the server's detail` (Task 4).
- A reconnect mid-move or mid-deploy keeps the ghosts for the same Ask or unit only. Test: `ghosts kept across a reconnect come
  back for the same Ask and not for another` (Task 4).
- Strict: the commit and the stop wait while a ghost is past its allowance or marked; a hand drag never starts. Tests: `under
  strict a mark disables Commit and Stop` (Task 4), `under strict a hand drag does not start` (Task 6).
- A hand move's circles stay at the drop until its event or refusal (`PendingDrops`), never snapping back first. Test: `a
  dropped model is drawn at its drop until the move's event` (Task 6).
- A drag while another player's frame moves models: the landing is recomputed against the projection current at each move and
  at the drop. Test: `the drop lands against the projection current at the drop` (Task 3).

---
### Task 1: Contacts, violations and the move ruler

**Files:** create `viewer/src/lib/play/contactCheck.ts`, `play/violations.ts`, `play/moveRuler.ts`; test
`viewer/tests/contactCheck.test.ts`, `viewer/tests/contactCheck.wasm.test.ts`, `viewer/tests/violations.test.ts`,
`viewer/tests/moveRuler.test.ts`.

**Interfaces:**

```ts
// contactCheck.ts — ContactCheck over Geometry.contacts
/** The projection's models as contact bodies: `at` for a model on the table. */
export function bodiesOf(p: Projection): ContactBody[];
export function move(g: Geometry, p: Projection, legal: MoveRegion, proposed: ReadonlyMap<string, Placement>, size: TableSize): Map<string, Contact>;
export function placement(g: Geometry, p: Projection, proposed: ReadonlyMap<string, Placement>): Map<string, Contact>;
export function words(c: Contact, separation: number | undefined, unitName: (id: string) => string): string;
export function allWords(found: ReadonlyMap<string, Contact>, separation: number | undefined, unitName: (id: string) => string): Map<string, string>;
// violations.ts — Violations
export class Violations {
  get marked(): ReadonlyMap<string, string>; subscribe(listener: () => void): () => void;   // Changed
  measure(over: Iterable<string>, found: ReadonlyMap<string, string>): void; refused(models: Iterable<string>, reason: string): void;
  moved(models: Iterable<string>): void; clear(): void; blocks(enforcement: Enforcement): boolean;
  labels(order: Iterable<string>): { modelId: string; reason: string }[];
}
// moveRuler.ts — MoveRuler, the parts the spec keeps
export interface RulerReadout { readonly distance: number; readonly remaining: number | null; readonly overAllowance: boolean;
  readonly underMinimum: boolean; readonly outOfRange: boolean }
export interface MoveGate { readonly allowed: boolean; readonly reason: string | null }
export function read(start: XY, current: XY, allowance: number | null, minimum?: number | null): RulerReadout;
export function gate(readouts: readonly RulerReadout[], enforcement: Enforcement, marked?: ReadonlyMap<string, string>): MoveGate;
export function usedInFrame(frame: Frame, modelId: string): number;
export function isDisembark(legal: MoveRegion): boolean;
export function isShelfPlacement(legal: MoveRegion, p: Projection): boolean;
/** RulerNode's label: `6.0"  (1.5" left)`, or `6.0"` without an allowance. */
export function readoutText(r: RulerReadout): string;
```

`move` passes `{ separation, passesModels, landing: !ignoresTerrain, move: { allowance, impassable: impassableVolumeIds, difficult:
difficultVolumeIds, difficultCap }, size }`; `placement` passes `{ passesModels: true, landing: false }`.

- [ ] Tests first. `contactCheck.test.ts` (a fake `Geometry`): `move builds the bodies and the Ask's terms`; `placement asks for
  overlaps only`; `words say what each contact is` (ContactCheckTests' `Words_say_what_each_contact_is`: `overlaps a base`, `path
  crosses a model`, `ends in impassable terrain`, `within 1" of Orcs`). `contactCheck.wasm.test.ts` (B's `loadTestGeometry`):
  ContactCheckTests' `a landing on another model's base overlaps`, `a landing within the separation of another unit's model is
  too close`, `a model answered where it stands has not moved`, `a hand placement is held to overlap only`. `violations.test.ts`:
  ViolationsTests' eight. `moveRuler.test.ts`: MoveRulerTests' four `Gate` cases, `the ruler reads displacement and remaining
  allowance` and `a minimum move reads short moves red` (ViewModelTests), `used in frame sums the frame's table-to-table moves`,
  `a region anchored on a model over shelf models is a placement, not a disembark` (DeployAroundTests), `readoutText`.
- [ ] `./scripts/geometry-wasm && cd viewer && npx vitest run tests/contactCheck.test.ts tests/contactCheck.wasm.test.ts tests/violations.test.ts tests/moveRuler.test.ts`
  fails.
- [ ] Port; `Violations.update` raises only when the marks change.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: contacts, violations and the move ruler, ported`.

### Task 2: Held commits, the ghost stash, formations, pending drops and marker locks

**Files:** create `viewer/src/lib/play/commitHold.ts`, `play/ghostStash.ts`, `play/formation.ts`, `play/pendingDrops.ts`,
`play/markerLocks.ts`; test `viewer/tests/commitHold.test.ts`, `viewer/tests/ghostStash.test.ts`,
`viewer/tests/formation.test.ts`, `viewer/tests/pendingDrops.test.ts`, `viewer/tests/markerLocks.test.ts`.

**Interfaces:**

```ts
export interface HeldCommit { readonly key: string; readonly at: ReadonlyMap<string, Point3>; readonly yaws: ReadonlyMap<string, Yaw>;
  readonly moved: readonly string[] }
export class CommitHold { remember(key: string, moves: readonly ModelRelocation[]): void; sent(token: string): void;
  refused(token: string): HeldCommit | null }
export interface KeptGhost { readonly position: Point3; readonly yaw: Yaw | null }
export class GhostStash { keep(key: string, positions: ReadonlyMap<string, Point3>, yaws: ReadonlyMap<string, Yaw>): void;
  take(key: string): Map<string, KeptGhost> | null }
export const GAP = 0.5;                                   // Formation.Gap
export function arrange(g: Geometry, bases: readonly Base[], rows: number, centre: Point3, facing: Yaw): Point3[];
export class PendingDrops { holds(modelId: string): boolean; add(token: string, moves: readonly ModelRelocation[]): void;
  consume(moved: ModelRelocation): boolean; refuse(token: string): string[] }
export class MarkerLocks {                                // MarkerLocks and TerrainLock
  isLocked(status: GameStatus | null, p: Projection | null, id: string): boolean;
  mayDrag(status: GameStatus | null, p: Projection | null, id: string): boolean;
  itemFor(status: GameStatus | null, p: Projection | null, id: string): { label: 'Unlock' | 'Lock'; apply(): void } | null;
  subscribe(listener: () => void): () => void;
}
export const terrainMayDrag = (status: GameStatus | null): boolean => status === 'setup';
```

`arrange` takes the `Geometry` for `Formation.Behind`'s `Distance.EdgeToEdge` (`g.edgeToEdge` over `placed` bodies of height 1).
`MarkerLocks.itemFor` returns the item and applies it itself (Godot's `ItemFor`/`Chosen` pair); D4's menu shows it last.

- [ ] Tests first: CommitHoldTests' four (the command id read as the token), GhostStashTests' one plus `ghosts kept for one key
  are not given to another`, FormationTests' six (the fake-free ones on `loadTestGeometry()`), `a drop is held until its move's
  event and a refusal releases it` and `consume is true only when the move landed where it was dropped` (PendingDrops),
  MarkerLocksTests' seven but `The_marker_menu_carries_the_lock_item_last` (D4).
- [ ] `./scripts/geometry-wasm && cd viewer && npx vitest run tests/commitHold.test.ts tests/ghostStash.test.ts tests/formation.test.ts tests/pendingDrops.test.ts tests/markerLocks.test.ts`
  fails.
- [ ] Port; `PendingDrops.consume` compares positions exactly, as Godot's `==` does.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: held commits, the ghost stash, formations, pending drops and marker locks, ported`.

### Task 3: The model drag

**Files:** create `viewer/src/lib/play/modelDrag.ts`; test `viewer/tests/modelDrag.test.ts` (a fake `Geometry`),
`viewer/tests/modelDrag.wasm.test.ts`.

**Interfaces:**

```ts
// modelDrag.ts — TableInput's model drag (OnLeftDown's pick-up, Place, Arrange, Turn, OnLeftUp) over DragBodies
export function stationary(p: Projection, held: ReadonlySet<string>, ghosts?: ReadonlyMap<string, Placement>): PlacedModel[];
export function grabs(models: readonly string[], proposing: ReadonlySet<string>, drawn: (id: string) => Placement | null,
  projected: (id: string) => Placement): Map<string, Placement>;
export interface Held {
  readonly ids: readonly string[];                       // drag order
  readonly grabs: ReadonlyMap<string, Placement>;        // where each stood when picked up (a ghost where it is drawn)
  readonly origins: ReadonlyMap<string, Placement>;      // the projection's placements: rulers and allowance (none off the table)
  readonly yaws: ReadonlyMap<string, Yaw>; readonly rows: number;   // rows 0: no formation
  readonly ends: ReadonlyMap<string, HeldEnd>;           // where each is drawn and lands now
}
export class ModelDrag {
  constructor(geometry: Geometry);
  get held(): Held | null;
  /** The clicked model alone with Shift, else every model of its unit the board draws (on the table, or ghosts). */
  start(p: Projection, modelId: string, at: XY, shift: boolean, proposing: ReadonlySet<string>, ghosts: ReadonlyMap<string, Placement>): Held;
  /** Each held model to its grab plus the cursor's travel (or its formation spot), landed by landModels against stationary(p,
   *  held, ghosts), walls (wallsOf(terrain, region?.impassableVolumeIds)) and terrain; with Alt, straight there and restModel. */
  move(p: Projection, at: XY, alt: boolean, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null;
  /** Q/E: each held model about its own centre, or with rows the formation; ±15°, Shift ±30°. */
  turn(p: Projection, degrees: number, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null;
  /** 1–5 while holding: Formation.arrange about the cursor, facing yaw 0 turned by Q/E. */
  arrange(p: Projection, rows: number, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null;
  /** The landings with their yaws; null when nothing moved (DragBodies' `moved` test). */
  end(): Map<string, Placement> | null;
  cancel(): void;
}
```

A held model the projection no longer has, or that left the table outside a proposal, cancels the drag at the next `move`.

- [ ] Tests first (fake `Geometry` returning scripted `HeldEnd`s, recording calls): `stationary keeps every unheld model on the
  table and a ghost where it is drawn`; `grabs take a proposed ghost where it is drawn`; `shift picks one model, else the unit`;
  `move sends held models in drag order with their grab offsets`; `walls are the Ask's impassable ids inside a move, else every
  Impassable volume`; `alt places straight at the cursor resting on the highest surface`; `turn adds 15 degrees to each held
  model, 30 with shift, and re-lands`; `arrange lays out rows about the cursor`; `a still drop ends with null`; `the drop lands
  against the projection current at the drop` (move with projection A, a model moves in B, end after a move with B → the last
  `landModels` saw B's model); `a held model that leaves the projection cancels the drag`. WASM: `two models dropped on one spot
  land apart` (`landModels` through the real geometry: the two landings' `edgeToEdge` is at least 0).
- [ ] `./scripts/geometry-wasm && cd viewer && npx vitest run tests/modelDrag.test.ts tests/modelDrag.wasm.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the model drag — lifted solid landings, Alt free, Q/E turns, formations`.

### Task 4: The move Ask

**Files:** create `viewer/src/lib/play/decisionBox.move.ts`; modify `viewer/src/lib/play/decisionBox.ts` (the surface the hooks
use); test `viewer/tests/decisionBoxMove.test.ts`.

**Interfaces:**

```ts
// decisionBox.ts gains, for MoveHooks only (DecisionBox.cs's fields the Move partial touches):
//   title / hint / note setters; add(control: Control): void; replace(id: string, control: Control): void;
//   onEnter / onCancel / onStop / onTarget handler slots; answer(ask: Ask, answer: AskAnswer): void;
//   sendCommand(command: Command, failed: (message: string) => string): string   // a token; refusals reach hooks.refused
//   proposing: Set<string>; ghosts: Map<string, Placement>   (TableInput.Proposing and the proposed nodes' placements)
//   placementPoint: ((at: Point3) => void) | null;  placeAt(at: Point3): boolean   (TableInput.PlacementPoint)
//   dragged(modelId: string, at: Placement): void   (OnDragged: a held proposing model dropped; Violations.moved)
// decisionBox.move.ts
export interface MoveDeps { geometry(): Geometry | null; violations: Violations; enforcement(): Enforcement; size(): TableSize }
export function moveHooks(deps: MoveDeps): MoveHooks & {
  /** The rings the board draws: each listed model's start and the Ask's allowance (ShowRegions' ids). */
  allowance(): { readonly centre: XY; readonly radius: number }[];
  /** One readout per held proposing model from its origin, for the rulers. */
  readouts(held: Held | null): { modelId: string; from: XY; to: XY; readout: RulerReadout }[];
};
```

`renderMove` is `RenderMove` with the spec's cuts: a disembark or shelf placement starts its ghosts in
`around(models, container placement, container base, size, legal.separation)` and notes `Drag them into place, then commit.` (shelf)
or `Disembark: place every model fully within {within:0.0}" of {piece}. Drag them out, then commit.`; a move from the table notes
`Move up to {remaining:0.0}" (used {used:0.0}") · Alt: place freely.` or, with a `minDistance`, `Move {min:0.0}" to
{remaining:0.0}" (used {used:0.0}") · Alt: place freely.`, `used` from `usedInFrame(ask's frame, lead)`; `OfferEmbark` appends
` Or click a container in reach to embark: {names}.` and a click on a listed container answers `Inside` for every model. Commit
(`Commit move (Enter)`) and the stop (`{stopLabel} (C)`) send `Destinations(moves, continues)` with each model's ghost facing
(`YawOf`: a table or shelf model its ghost's yaw, a disembarking one its ghost's yaw too). `regate` measures `contactCheck.move`
on every drag and render, reads one `read(origin, ghost, allowance, minDistance)` per model on the table, sets `gate(...)`, and
disables Commit and Stop when it is not allowed, the reason in a `reason` control. `refused` turns `CommitHold.refused(token)`
into the stash and the verdict; the next render puts the ghosts back (`RestoreMove`) and marks the moved models
(`ApplyVerdict`). `keepGhosts` stashes on a reconnect.

- [ ] Tests first (a fake host and geometry; projections from `tests/play.ts`): `a move from the table proposes each model where it
  stands and notes the allowance and what was used`; `a minimum distance notes the range`; `a shelf placement rings the anchor and
  notes the prompt`; `a disembark rings the container and names it`; `a dragged ghost regates with contacts and readouts`; `under
  strict a mark disables Commit and Stop`; `under warn the reason shows and Commit sends`; `commit sends Destinations with each
  ghost's facing`; `the stop sends continues true`; `a click on an enterable container answers Inside for every model`; `Esc puts
  the ghosts back and clears the marks`; `a refused commit returns its ghosts marked with the server's detail`; `the marks lapse
  when a refused model is dragged`; `ghosts kept across a reconnect come back for the same Ask and not for another`; `a container
  gone from the table notes it and offers nothing`.
- [ ] `cd viewer && npx vitest run tests/decisionBoxMove.test.ts` fails.
- [ ] Implement; `DecisionBox.render` calls `moves.renderMove` for `move` and `moves.keepGhosts` while `reconnecting`, as
  `RenderAsk` does.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the move Ask — ghosts, allowance, contacts, the gate, commit, stop, embark and refused commits`.

### Task 5: Deploy placement

**Files:** modify `viewer/src/lib/play/decisionBox.move.ts` (`startDeploy` and its helpers); test
`viewer/tests/decisionBoxDeploy.test.ts`.

**Interfaces:** `MoveHooks.startDeploy` is `StartDeploy`, `DropDeploy`, `ShowDeploy` and `PlaceDeploy`: the title `Place the
models of {unit}`, the hint `Click the table where the unit goes`, a `placementPoint` that lays the unit's shelf models out with
`layout(models, at, size)` as ghosts, then the hint `Drag to place · Shift: one model · 1–5 while holding: rows · Enter: place ·
Esc: clear` and `Place (Enter)`; `regate` measures `contactCheck.placement` and gates under the game's enforcement; Place
remembers the commit and sends `MoveModels` from the shelf with each ghost's facing (`Could not place the models: {message}` on a
failed send); Esc resets and re-arms the click. A stash for the unit restores the ghosts of models still on the shelf.

- [ ] Tests first: `a mid-deploy pick asks for a click and the click lays the unit out`; `a click where nothing fits keeps the
  pick armed`; `a ghost on a base is named and strict waits`; `Place sends MoveModels from the shelf with each ghost's facing`;
  `a refused placement comes back marked`; `Esc clears the ghosts and asks for a click again`; `a reconnect brings back the ghosts
  of models still on the shelf`; `either player of the team places the pick` (TeamAsksTests).
- [ ] `cd viewer && npx vitest run tests/decisionBoxDeploy.test.ts` fails.
- [ ] Implement; `DecisionBox.render`'s no-Ask branch calls `moves.startDeploy` for the first `midDeploy` of my teams, acting
  team first, as `RenderAsk` does.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: deploy placement — a click lays the unit out, ghosts drag solid, Place sends from the shelf`.

### Task 6: Hand moves outside an Ask

**Files:** create `viewer/src/lib/play/handMove.ts`; test `viewer/tests/handMove.test.ts`.

**Interfaces:**

```ts
// handMove.ts — TableInput.MayDrag and OnLeftUp's free moves
export type DragTarget = { kind: 'models'; ids: readonly string[] } | { kind: 'marker'; id: string } | { kind: 'terrain'; id: string };
/** A watcher drags nothing; models need every one proposing or a game that is not strict; a locked objective never drags;
 *  terrain only in setup. */
export function mayDrag(target: DragTarget, watching: boolean, proposing: ReadonlySet<string>, enforcement: Enforcement,
  status: GameStatus | null, p: Projection | null, locks: MarkerLocks): boolean;
/** A hand move's relocations: from each grab to its landing and yaw (OnLeftUp). */
export function handMoves(grabs: ReadonlyMap<string, Placement>, landings: ReadonlyMap<string, Placement>): ModelRelocation[];
/** While in hand: ContactCheck.Placement's words for the held models (cleared when the drag ends; the server judges the drop). */
export function handMarks(g: Geometry, p: Projection, held: ReadonlyMap<string, Placement>, n: Names): Map<string, string>;
export function markerMove(id: string, at: Point3, yaw: Yaw): MoveMarker;
```

`PlayPage` adds the drop to `PendingDrops` before `sendCommand(MoveModels)`; `sceneOfPlay` draws a held model at its drop until
`consume` (the move's event) or `refuse` (its refusal or a failed send). A marker drop sends `MoveMarker`; it snaps back until its
event lands, as Godot redraws.

- [ ] Tests first: `under strict a hand drag does not start`; `proposing models drag under strict`; `a watcher drags nothing`;
  `a locked objective does not drag until unlocked`; `terrain drags only in setup` (MarkerLocksTests' last); `hand moves go from
  the grab to the landing`; `hand marks name overlaps while in hand`; `a dropped model is drawn at its drop until the move's event`
  (a `PendingDrops` plus `sceneOfPlay` case).
- [ ] `cd viewer && npx vitest run tests/handMove.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: hand moves outside an Ask under the game's enforcement; markers and locks`.

### Task 7: Rings, rulers, landings and marks on the board; the drag wired

**Files:** create `viewer/src/lib/play/moveOverlays.ts`; modify `viewer/src/lib/board/draw.ts` (`warned`),
`viewer/src/lib/play/PlayPage.svelte`, `viewer/src/lib/play/DecisionBox.svelte` (the `reason` control); test
`viewer/tests/moveOverlays.test.ts`.

**Interfaces:**

```ts
// moveOverlays.ts — what RulerNode, LandingMarks, MoveRegionNode (as a circle) and ViolationMarks draw, as data, then as Overlays
export interface RulerLine { readonly from: XY; readonly to: XY; readonly text: string; readonly outOfRange: boolean }
export function rulerLines(readouts: readonly { from: XY; to: XY; readout: RulerReadout }[]): RulerLine[];
/** A ring where each held model lands while it is drawn elsewhere (lifted over another model or terrain). */
export function landingRings(held: Held, radius: (modelId: string) => number): { centre: XY; radius: number }[];
/** One label per reason over its first model (Violations.labels), held models first. */
export function violationLabels(v: Violations, order: readonly string[], centreOf: (id: string) => XY | null): { at: XY; text: string }[];
export const allowanceOverlay: (rings: readonly { centre: XY; radius: number }[], colour: string) => Overlay;
export const rulerOverlay: (lines: readonly RulerLine[]) => Overlay;              // red when out of range, else white
export const landingOverlay: (rings: readonly { centre: XY; radius: number }[]) => Overlay;
export const labelOverlay: (labels: readonly { at: XY; text: string }[], colour: string) => Overlay;
// draw.ts: DrawOptions gains warned?: ReadonlySet<string> (a model's circle in the warning colour, PieceNode.SetWarning)
```

`PlayPage` builds its box as `new DecisionBox(host, moveHooks(deps))` and wires `BoardInteraction`: `onDragStart` on a model
not in `box.eligible` checks `mayDrag` and starts `ModelDrag`
(`box.proposing`, `box.ghosts`), on a marker checks `mayDrag` and holds it on the table plane; `onDragMove` moves it (hand
marks while not proposing, `box.dragged` for each landing of a proposing model); `onDragEnd` drops (proposing: the ghosts stay
and `box.dragged` regates; else a hand move or `MoveMarker`); `onDragCancel` puts the held ones back; `onSelect` on empty table
is `box.placeAt(at)` or clears the selection. While a drag is held the window keys go to it first: Q/E `turn`, 1–5 `arrange`,
Escape cancel; Enter is held off mid-drag, as the box's `_Input` does. Overlays, in order: the allowance rings (team colour),
the rulers, landing rings, violation labels (warning colour). The scene's ghosts are `box.ghosts`, the drag's drawn ends and the
pending drops.

- [ ] Tests first: `rulerLines read RulerNode's text and turn red out of range` (`6.0"  (1.5" left)`; `7.5"  (0.0" left)` red);
  `a landing ring shows only while drawn and landing differ`; `violation labels name each reason once over its first model,
  held first`.
- [ ] `cd viewer && npx vitest run tests/moveOverlays.test.ts` fails.
- [ ] Implement the module, `warned`, and the wiring.
- [ ] `npm test && npm run check && npm run build:hosted`.
- [ ] Commit: `Viewer: moves and deploys on the board — allowance rings, rulers, landing rings, violation marks`.
