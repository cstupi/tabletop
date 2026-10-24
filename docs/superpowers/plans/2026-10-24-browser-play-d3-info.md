# Browser play D3: the combat log, dice, Undo and the unit card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Beside the board a player reads the combat log in Godot's exact words (action headers, rolls with their targets,
results, rerolls and consequences, change lines, undos), the latest dice, presses Undo (warned when it takes dice back) or Undo
to here, and reads a unit card for the selected or hovered unit with its rules' texts, plus the hover line and the enemy strip.

**Architecture:** `client/src/Core/RollEntry.cs`, `CombatLog.cs`, `CombatLog.Words.cs`, `CombatLogCache.cs` and `UndoPlan.cs`
port module for module to `viewer/src/lib/play/`. Their faithfulness is held by Godot's test cases ported to Vitest and by an
agreement fixture: an xUnit test in `tests/Tabletop.Client.Tests` renders Godot's log over three recorded logs into
`viewer/tests/fixtures/combatlog.json`, and Vitest renders the same logs through the TypeScript and compares every line. The
card's models (`UnitSheet`, `UnitCardContent`, `SheetFocus`, `SelectionView`, `HoverView`, `EnemyStrip`) port the same way.
The undo prediction (`Session.UndoToAsync`) sits beside the connection over D1's `projectTo`.

**Tech Stack:** Vite 8, Svelte 5, TypeScript 5.9, Vitest 5, Node 24; .NET 10 xUnit for the fixture writer.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (What play shows → a unit card, the combat log, the latest
dice, Undo). Prerequisites: `-d1-decisions.md` (Tasks 1–3, 7: `projectTo`, `Names`, `logText`, `RuleTexts`, `spellText`,
`headerView.actionFrame`, `PlayPage`, settings). Godot sources ported: `client/src/Core/RollEntry.cs`, `CombatLog.cs`,
`CombatLog.Words.cs`, `CombatLogCache.cs`, `UndoPlan.cs`, `UnitSheet.cs`, `UnitCardContent.cs`, `SheetFocus.cs`,
`SelectionView.cs`, `HoverView.cs`, `EnemyStrip.cs`, `RosterView.ReserveOf`; `Session.UndoToAsync`; `client/src/Hud/UnitCard.cs`,
`CombatLogPanel.cs`, `DiceTray.cs`, `UndoButton.cs`, `client/src/Table/HoverLabels.cs`.

## Findings that shape the plan

- **The combat log is ~530 lines of C# words** (`CombatLog` 151, `.Words` 184, `RollEntry` 195) whose every string is spec copy
  (`docs/superpowers/specs/*combat-log*`). Ports are file for file, `Draft` and `Aftermath` kept as classes, so a diff reads
  side by side; the agreement fixture catches any word that drifts.
- **`tests/Tabletop.Client.Tests` compiles Godot's core** (`<Compile Include="../../client/src/Core/**/*.cs">`) and already loads
  a recorded log (`Fixtures/game-01a126db-charge.json`, `CombatLogFixtureTests`); the viewer's `skirmish` and `terrain` fixtures
  hold whole sim games with welcome and board. The writer reads those three and writes one JSON; Vitest needs nothing else.
- **`CombatLogRows` is Godot's row reuse** (Godot rebuilds controls by hand); Svelte's keyed `{#each}` by `seq` does that job,
  so it is not ported.
- **`HoverView` draws its distance between the two closest base-edge points** (`Distance.ClosestPoints`); plan B exports
  `edgeToEdge` and `nearestPair` but no closest points. The browser shows the distance in the hover line and the enemy strip, and
  draws no line between the bases.
- **Undo prediction:** Godot shows `ProjectTo(seq)` from the press until the `Undone` with that `toSeq` arrives or the send
  fails; the log fades the rows past it (`PendingUndoTo`). `undo.ts` keeps that state for `PlayPage`, which shows the predicted
  projection in its place.

## Global Constraints

- Words are Godot's, byte for byte; nothing in the log or the card names a rule the event does not carry.
- One module per Godot file, members camelCased; tests are Godot's cases.
- The fixture is written only with `TABLETOP_WRITE_FIXTURES=1`; a plain `dotnet test` fails when it is stale, as the viewer's
  fixtures do.
- A watcher sees the log and the card and no Undo.
- Files under 500 lines.
- Commands: `cd viewer && npm test`, `npm run check`, `npm run build:hosted`; `dotnet test tests/Tabletop.Client.Tests`;
  `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CombatLogViewerFixture`.

## Review Focus

- Every line of three real logs agrees with Godot's. Test: `combatlog fixture <name> renders as Godot does` (Task 3).
- Undo steps back one more action per press while the first is pending, and a refused undo restores the projection. Tests:
  `a pending undo plans over the log with its marker`, `a refused or failed undo drops the prediction` (Task 4).
- The dice warning lists each lost roll once, in the log's collapsed words. Test: `a roll and its reroll both in the range are
  listed once` (Task 4).
- The card rebuilds only when what it shows changed (`UnitCardContent` equality), so an open tooltip survives other frames.
  Test: `the same sheet built again is the same content` (Task 5).

---
### Task 1: Roll entries and their words

**Files:** create `viewer/src/lib/play/rollEntry.ts`; test `viewer/tests/rollEntry.test.ts`.

**Interfaces:**

```ts
// rollEntry.ts — RollEntry.cs: LogEntry, LineEntry, DieView, RollAmendment, RollEntry, RollWords
export type LogEntry = LineEntry | RollEntry | ActivationBlock;          // ActivationBlock from combatLog.ts (Task 2)
export interface LineEntry { readonly type: 'line'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean;
  readonly text: string }
export interface DieView { readonly raw: number; readonly modified: number; readonly success: boolean; readonly rawJudged: boolean;
  readonly judged: boolean; readonly triggered: readonly string[] | null }
export const dieText: (d: DieView) => string;
export interface RollAmendment { readonly seq: number; readonly kind: string; readonly replaced: readonly number[];
  readonly undone: boolean; readonly with: readonly number[] | null }
export const amendmentLine: (a: RollAmendment, face: (n: number) => string) => string;
export interface RollEntry { readonly type: 'roll'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean;
  readonly frameId: string; readonly record: RollRecord; readonly context: SequenceContext; readonly label: string;
  readonly target: string; readonly dice: readonly DieView[]; readonly amendments: readonly RollAmendment[];
  readonly applied: readonly string[]; readonly details: readonly string[]; readonly sequence: string | null;
  readonly consequence: string | null; readonly dieLabels: readonly string[] | null }
// RollEntry's computed members as functions over an entry: judged, successes, title, result, chain, outcome, collapsed(face), faces(face)
export function judged(r: RollEntry): boolean;   export function successes(r: RollEntry): number;
export function title(r: RollEntry): string;     export function result(r: RollEntry): string;
export function chain(r: RollEntry): string;     export function outcome(r: RollEntry): string;
export function collapsed(r: RollEntry, face: (n: number) => string): string;
export function faces(r: RollEntry, face: (n: number) => string): string;
export function isTest(r: RollRecord, sequence: string | null): boolean;
export function capital(s: string): string;
/** CombatLog.Pip: a face as its die glyph (⚀–⚅), else the number; combatLog.ts re-exports it. */
export function pip(face: number): string;
export function rollEntryOf(e: GameEvent, undone: boolean, step: StepCompleted, r: RollRecord, c: SequenceContext,
  sequence: string | null, amendments: readonly { event: GameEvent; undone: boolean }[], n: Names): RollEntry;
export const RollWords: { label(roll: string): string; perWound(roll: string, sequence: string | null): boolean;
  target(r: RollRecord): string; modifier(m: RollModifier): string; details(r: RollRecord): string[];
  result(roll: string, dice: readonly DieView[], sequence?: string | null): string; plural(n: number, word: string): string };
```

C# records with computed properties become plain objects plus functions over them; `with { Consequence, DieLabels }` becomes a
spread. `Signed(n)` is `n > 0 ? '+n' : n < 0 ? '-n' : '0'`, as `ToString("+#;-#;0")`.

- [ ] Tests first: RollLineTests' nine (`a raw face roll reads unmodified`, `a roll every face passes reads no target`, `a roll in a
  deal-wounds frame counts wounds ignored and standing`, `a single die test reads passed or failed then its consequence`, `a
  passed casting roll reads cast`, `dangerous terrain fails on a one`, `two rules share one pair of brackets`, `a roll-off reads
  each die after its team and the winner after the faces`, `roll labels`), built through `rollEntryOf` and `pip` as
  `RollLineTests.Entry` and `Line` do; CombatLogTests' roll cases go through the build (Task 2).
- [ ] `cd viewer && npx vitest run tests/rollEntry.test.ts` fails.
- [ ] Port `RollEntry.cs` top to bottom.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: roll entries and roll words, ported from the combat log`.

### Task 2: The combat log build and its cache

**Files:** create `viewer/src/lib/play/combatLog.ts` (`CombatLog.cs`), `play/combatLogWords.ts` (`CombatLog.Words.cs`:
`changeLines`, `change`, `deployed`, `Aftermath`, `Draft`), `play/combatLogState.ts` (`CombatLogCache.cs`); test
`viewer/tests/combatLog.test.ts`, `viewer/tests/combatLogBlock.test.ts`, `viewer/tests/combatLogState.test.ts`.

**Interfaces:**

```ts
// combatLog.ts
export interface ActivationBlock { readonly type: 'block'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean;
  readonly header: string; readonly actor: string | null; readonly targets: readonly string[]; readonly entries: readonly LogEntry[];
  readonly chainParts: readonly string[]; readonly nested: boolean; readonly spell: string | null; readonly spellCost: number | null }
export const blockChain: (b: ActivationBlock) => string;          // ChainParts joined with ' → '
export { pip } from './rollEntry';
export function build(log: readonly GameEvent[], n: Names): LogEntry[];
export function filter(entries: readonly LogEntry[], f: LogFilter, mine: (unitId: string) => boolean, unitId: string | null): LogEntry[];
export function latest(entries: readonly LogEntry[]): RollEntry | null;
export function flatten(entries: Iterable<LogEntry>): LogEntry[];
// combatLogState.ts
export class CombatLogState {
  readonly names: Names; get entries(): readonly LogEntry[]; get latest(): RollEntry | null; get undo(): UndoPlan | null;   // UndoPlan: Task 4
}
export class CombatLogCache {
  /** The same state until the log instance, its length or last seq, the pending undo, the game or the projection instance changes. */
  of(log: readonly GameEvent[], pendingUndoTo: number | null, game: GameInfo, projection: Projection | null): CombatLogState;
}
```

`build` keeps `Build`'s locals (`live`, `frames`, `rolled`, `amendments` as a `Map<rollId, {event, dead}[]>`, `after`, `top`,
`drafts`, `open`, `round`, `started`, `undos`), its `closeFrom` and its switch in the same case order. `Draft.header` rounds the
farthest move with `Math.round(x * 2) / 2` (distances are never negative, where it equals `MidpointRounding.AwayFromZero`) and
prints it as `0.#` does.

- [ ] Tests first, Godot's cases through `build`, with `buildOn(board, ...payloads)` as `CombatLogTests.BuildOn`:
  `combatLog.test.ts` CombatLogTests' twenty-four (the nine roll cases Task 1 left, then `questions answers closes empty steps and
  frame bookkeeping are not drawn`, `a header names the round team unit action targets and the farthest move`, `a unit sent to the
  shelf unwounded after a failed morale test fled`, `an undo reads undone to the header it took back and a menu edit is by hand`,
  `an action frame groups its entries under one header with a chain summary`, `an undone activation closes struck through and an
  undo inside one keeps it open`, `filters keep rolls my units or the carded unit`, `a model wounded then removed in one step reads
  removed not wounded`, `an undo into a finished activation reopens it until its live finish`, `a block is found by the targets of
  its nested frames`, `an undone block closes at its own finish or abandonment`, `the engine's action record draws no line and a
  hand record does`, `rerolls and edits stay on the roll's line`, `a rerolled die counts the triggers its new face set off`, `a
  single die test reads passed or failed and a raw face roll says unmodified`); `combatLogBlock.test.ts` CombatLogBlockTests'
  thirteen; `combatLogState.test.ts` CombatLogCacheTests' four (`a pending undo plans over the log with its marker` waits for
  Task 4: write it now, `it.todo` until then).
- [ ] `cd viewer && npx vitest run tests/combatLog.test.ts tests/combatLogBlock.test.ts tests/combatLogState.test.ts` fails.
- [ ] Port the three files; `CombatLogState.undo` returns null until Task 4 wires `undoPlanOf`.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the combat log build — headers, rolls, change lines, undos — and its per-state cache, ported`.

### Task 3: The combat log agreement fixture

**Files:** create `tests/Tabletop.Client.Tests/CombatLogViewerFixtureTests.cs`, `viewer/tests/fixtures/combatlog.json`
(written), `viewer/tests/combatLog.agreement.test.ts`.

**Interfaces:**

```ts
// combatlog.json, written by CombatLogViewerFixtureTests
interface Case { name: 'skirmish' | 'terrain' | 'charge'; game: GameInfo; board: Board | null; events: GameEvent[];
  rows: Row[]; latest: string | null; undo: { toSeq: number; tooltip: string; lostRolls: string[] } | null }
interface Row { depth: number; kind: 'block' | 'roll' | 'line'; seq: number; undone: boolean;
  text: string;                       // Header, Collapsed(Pip) or Text
  title?: string; result?: string; outcome?: string; chain?: string; details?: string[] }   // rolls; chain also on blocks
```

The writer builds `CombatLog.Build(log, names)` with `Names(game, EventApplier(board, game) over the log)` for `skirmish` and
`terrain` (read from `viewer/tests/fixtures/<name>.{welcome,board,events}.json`), and `Names(CombatLogFixtureTests.Game, null)`
for `charge` (`Fixtures/game-01a126db-charge.json`), flattens depth first, and adds `CombatLog.Latest` (collapsed) and
`UndoPlan.Of`. It writes under `TABLETOP_WRITE_FIXTURES=1` and otherwise fails when the file differs, naming the case.

- [ ] Write `CombatLogViewerFixtureTests.Combatlog_fixture_is_current` (finds the repo root by walking up to `Tabletop.sln`) and
  `combatLog.agreement.test.ts`: `describe.each` over the cases, `it('combatlog fixture <name> renders as Godot does')` comparing
  the TypeScript rows (same flattening) with `toEqual`, then `latest` and `undo`.
- [ ] `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CombatLogViewerFixture`; `dotnet test
  tests/Tabletop.Client.Tests` passes; `cd viewer && npx vitest run tests/combatLog.agreement.test.ts` passes for `rows` and
  `latest` (`undo` after Task 4: `it.todo` until then). A difference is fixed in the TypeScript port, never in the fixture.
- [ ] Commit: `Viewer: the combat log agrees with Godot's over three recorded logs`.

### Task 4: Undo — the plan, the dice warning and the prediction

**Files:** create `viewer/src/lib/play/undo.ts`; modify `viewer/src/lib/play/combatLogState.ts` (`undo`); test
`viewer/tests/undo.test.ts`, `viewer/tests/combatLogState.test.ts` (the todo), `viewer/tests/combatLog.agreement.test.ts` (the
todo).

**Interfaces:**

```ts
// undo.ts — UndoPlan and Session.UndoToAsync's prediction
export interface UndoPlan { readonly toSeq: number; readonly action: GameEvent; readonly tooltip: string; readonly lostRolls: readonly string[] }
export function undoPlanOf(log: readonly GameEvent[], n: Names, entries?: readonly LogEntry[]): UndoPlan | null;
export const NOTHING_TO_UNDO = 'Nothing to undo';
export class UndoPrediction {
  /** The projection to show: ProjectTo(seq) while an undo is pending, else the connection's. */
  projection(state: ConnectionState): Projection | null;
  get pendingUndoTo(): number | null;
  /** Session.UndoToAsync: predict, send Undo(seq); a refusal or a failed send drops the prediction and rethrows the failure. */
  undoTo(state: ConnectionState, seq: number, send: (c: Command) => Promise<SendOutcome>): Promise<void>;
  /** Each events batch: an undone whose toSeq is the pending one ends the prediction (ClearPrediction). */
  events(batch: readonly GameEvent[]): void;
  subscribe(listener: () => void): () => void;
}
```

`CombatLogState.undo` is `CombatLogCache`'s: `undoPlanOf(log, names, entries)` with nothing pending, else over the log plus a
synthetic `undone` marker at `lastSeq + 1` to the pending seq, so each press steps back one more.

- [ ] Tests first: UndoPlanTests' eleven (`nothing undoable is null`, `the last action is undone whoever took it`, `a first action
  undoes to zero`, `engine follow-ups after an action are inside the range`, `each press steps back one more action`, `an answer
  and the close it caused are one action`, `a range with no rolls loses none`, `a roll in the range is listed in the log's collapsed
  words`, `a reroll or a dice edit in the range counts`, `a roll and its reroll both in the range are listed once`, `an undone roll
  before the target is not lost again`); `the prediction shows projectTo until the matching undone lands`; `a refused or failed
  undo drops the prediction`; `a watcher's state never predicts` (no board, no prediction); the state's `a pending undo plans over
  the log with its marker` and the fixture's `undo` assertions.
- [ ] `cd viewer && npx vitest run tests/undo.test.ts tests/combatLogState.test.ts tests/combatLog.agreement.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: undo — the last action, the dice it takes back, and the local prediction`.

### Task 5: The unit card's models, the hover line and the enemy strip

**Files:** create `viewer/src/lib/play/unitSheet.ts`, `play/unitCard.ts`, `play/sheetFocus.ts`, `play/selectionView.ts`,
`play/hoverView.ts`, `play/enemyStrip.ts`, `play/rosterReserve.ts`; test `viewer/tests/unitSheet.test.ts`,
`viewer/tests/unitCard.test.ts`, `viewer/tests/sheetFocus.test.ts`, `viewer/tests/selectionView.test.ts`,
`viewer/tests/hoverView.test.ts`, `viewer/tests/enemyStrip.test.ts`.

**Interfaces:**

```ts
// unitSheet.ts — UnitSheet (SheetRule, ChipKind, SheetChip, SheetWeapon, SheetModel, UnitSheetModel as interfaces)
export const HERO_LABEL = 'Hero', AP_LABEL = 'AP';
export function unitSheetOf(p: Projection, n: Names, unitId: string, rules: RuleTexts, maxWounds: ReadonlyMap<string, number>): UnitSheetModel;
// unitCard.ts — UnitCardContent and ModelGroup
export interface ModelGroup { readonly label: string; readonly loadout: string; readonly hero: boolean; readonly alive: number;
  readonly size: number; readonly woundsLeft: number; readonly woundsMax: number; readonly standing: readonly string[] }
export const groupTitle: (g: ModelGroup) => string, groupWounds: (g: ModelGroup) => string | null,
  groupLine: (g: ModelGroup) => string, nextModel: (g: ModelGroup, selected: string | null) => string | null;
export interface UnitCardContent { readonly sheet: UnitSheetModel; readonly team: number; readonly note: string | null;
  readonly groups: readonly ModelGroup[]; readonly woundsLeft: number; readonly woundsMax: number }
export function unitCardOf(sheet: UnitSheetModel, team: number, note: string | null): UnitCardContent;
export function sameCard(a: UnitCardContent | null, b: UnitCardContent | null): boolean;   // Equals, list by list
// sheetFocus.ts, selectionView.ts
export function sheetFocusOf(p: Projection, game: GameInfo, myTeam: string | null, selectedPiece: string | null): string | null;
export function teamOf(p: Projection, game: GameInfo, unitId: string): Team | null;
export interface Selection { readonly pieceId: string; readonly modelIds: readonly string[] }
export function selectedModels(p: Projection, s: Selection | null): string[];
export function describeSelection(n: Names, p: Projection, s: Selection | null): string | null;
// hoverView.ts — HoverView without From/To (no closest points in plan B)
export interface HoverInfo { readonly unitId: string; readonly name: string; readonly teamOrdinal: number; readonly distance: number | null;
  readonly wounds: readonly { modelId: string; left: number; max: number }[] }
export function hoverOf(g: Geometry, p: Projection, game: GameInfo, n: Names, hovered: string, selected: string | null,
  maxWounds: ReadonlyMap<string, number>): HoverInfo | null;           // distance: min edgeToEdge over model pairs
// enemyStrip.ts
export function stripShows(p: Projection, game: GameInfo, myTeam: string | null, cardUnit: string, hoveredUnit: string): boolean;
export function enemyStripOf(mine: UnitSheetModel, hovered: UnitSheetModel, distance: number | null): EnemyStripModel;   // RangeCheck, EnemyStripModel as interfaces
// rosterReserve.ts — RosterView.ReserveOf, shared by the card's chip and D4's roster
export function reserveOf(p: Projection, u: UnitInPlay): string | null;
```

- [ ] Tests first, Godot's cases: UnitSheetTests' ten, ReserveLabelTests' three, UnitCardContentTests' six, SheetFocusTests' six,
  SelectionViewTests' three, HoverViewTests' six (distance on a fake `Geometry` whose `edgeToEdge` measures circles), EnemyStripTests'
  three. `TrailingNumber` is `/\s+\d+(\.\d+)*$/`.
- [ ] `cd viewer && npx vitest run tests/unitSheet.test.ts tests/unitCard.test.ts tests/sheetFocus.test.ts tests/selectionView.test.ts tests/hoverView.test.ts tests/enemyStrip.test.ts`
  fails.
- [ ] Port.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the unit sheet, card content, focus, selection, hover line and enemy strip, ported`.

### Task 6: The unit card, the hover label and the enemy strip on the page

**Files:** create `viewer/src/lib/play/cardText.ts`, `play/UnitCard.svelte`, `play/EnemyStrip.svelte`; modify
`viewer/src/lib/play/PlayPage.svelte` (selection, hover, the card slot, the hover label overlay); test
`viewer/tests/cardText.test.ts`.

**Interfaces:**

```ts
// cardText.ts — the copy UnitCard.cs, HoverLabels.cs and the strip build by hand
export const cardHeadline: (c: UnitCardContent) => string;      // `{name} · {alive}/{size} models · Wounds {left}/{max}`
export const badges: (s: UnitSheetModel) => [string, string];   // `Quality {q ?? '-'}`, `Defense {d ?? '-'}`
export const weaponName: (w: SheetWeapon) => string;            // `{name} ×{count}` past one
export const WEAPON_HEADINGS: readonly string[];                // Weapon, Range, Attacks, AP, Rules
export const ruleChip: (r: SheetRule) => string;                // `{label} (not automated)` when tagged
export const ruleTooltip: (r: SheetRule) => string;             // its text, else `Not automated: no rule text.` when tagged, else ''
export const stripLine: (m: EnemyStripModel) => string;         // `Defense {d} · {alive} alive · {distance:0.0}"`
export const rangeLine: (r: RangeCheck) => { text: string; tone: 'success' | 'warning' | 'plain' };   // `{weapon} {range} within|beyond`
export const hoverLabel: (h: HoverInfo, eligible: boolean) => string;   // `{name} · Click to target`, `{name} · {d:0.0}"`, `{name}`
```

`PlayPage` holds the `Selection` (a model click selects its unit, Shift the model, an empty click clears; drawn through
`selected`), the hovered unit (`onHover`; leaving the canvas clears it), and the card's unit `sheetFocusOf(projection, game,
acting, selection?.pieceId)`. The card rebuilds only when `sameCard` says it changed. `UnitCard.svelte` draws the headline, the
badges, the chips (`Activated: …`, conditions, spell tokens, grants, reserve), the decision note (`noteOf`), the group rows
(title clipped with `groupLine` as its tooltip, wounds beside it; a click selects `nextModel`), the weapons grid with the AP
tooltip and rule chips, and the unit's rule chips with their texts as tooltips. The enemy strip sits above the card when
`stripShows`. The hover label is an overlay over the hovered unit's first model: `hoverLabel` in its team colour and each
multi-wound model's `left/max`.

- [ ] Tests first: `cardText.test.ts`: `the headline counts models and wounds`; `a weapon carried twice reads ×2`; `a tagged rule
  reads not automated and its tooltip falls back`; `the strip reads defense, alive and the distance to a tenth`; `a range within
  reads success and beyond warning`; `the hover label offers a click on an eligible unit, else the distance`.
- [ ] `cd viewer && npx vitest run tests/cardText.test.ts` fails.
- [ ] Implement the module, the components and the page wiring.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the unit card, the hover label and the enemy strip in play`.

### Task 7: The combat log panel, the dice tray and Undo

**Files:** create `viewer/src/lib/play/logRows.ts`, `play/CombatLogPanel.svelte`, `play/DiceTray.svelte`,
`play/UndoButton.svelte`; modify `viewer/src/lib/play/PlayPage.svelte`; test `viewer/tests/logRows.test.ts`.

**Interfaces:**

```ts
// logRows.ts — the row text CombatLogPanel.cs and DiceTray.cs draw
export const FILTER_LABELS: readonly string[];          // All, Rolls, My units, This unit (LogFilter's order)
export function rowText(e: LogEntry, open: boolean): string;     // `▾`/`▸` + header (+ `  ·  {chain}` folded), a roll collapsed, a line
export function faded(e: LogEntry, pendingUndoTo: number | null): boolean;   // undone, or past a pending undo
export function strike(s: string): string;               // each character + U+0336, as Strike
export function undoHere(e: LogEntry, watching: boolean): boolean;   // a live non-roll line or block, not an undo marker
export function trayLines(r: RollEntry | null): { title: string; faces: readonly DieView[]; result: string | null;
  success: boolean; tooltip: string } | null;
```

`PlayPage` keeps one `CombatLogCache` and reads `state = cache.of(log, prediction.pendingUndoTo, game, projection)`; the panel
(toggled by G and its button, `settings.logOpen`) shows `filter(state.entries, settings.logFilter, mine, cardUnit)`, rows keyed
by seq, blocks and rolls folded until clicked, a roll's expansion holding its details, dice and rule chips, `Undo to here` per
`undoHere` (`prediction.undoTo(e.seq)`, no question, as Godot's). The tray shows `state.latest`: title, faces as pips, result
in success or dim colour, the collapsed line as its tooltip. `UndoButton` (`Undo (Ctrl+Z)`, hidden for a watcher, disabled with
`Nothing to undo`, tooltip `state.undo.tooltip`) asks `Undo dice rolls?` with the lost rolls one per line, Undo or Cancel, when
`lostRolls` is not empty, else sends at once; Ctrl+Z presses it. A failed undo shows `Undo failed: {message}` in the page's alert.

- [ ] Tests first: `logRows.test.ts`: `a folded block shows its chain and an open one does not`; `a roll row is its collapsed line`;
  `rows past a pending undo fade`; `strike puts a combining stroke after each character`; `undo to here skips rolls, undone rows,
  undo markers and watchers`; `the tray titles the latest roll and carries its collapsed line`.
- [ ] `cd viewer && npx vitest run tests/logRows.test.ts` fails.
- [ ] Implement the module, the three components and the wiring; the board and every part read `prediction.projection(state)`.
- [ ] `npm test && npm run check && npm run build:hosted`.
- [ ] Commit: `Viewer: the combat log panel, the dice tray and Undo in play`.
