# Browser play D1: the decision box Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** In a playing game the browser answers every Ask the server puts to the player's team, as Godot's decision box does:
Choose (numbered, narrowed to a clicked unit, an option's unit lit on hover), PickPieces (a deploy pick included), Assign
(declare targets), Allocate (casualties), TokenSpend, Confirm, Pass and mute, the sealed reveal, the server's refusal, and the
`game_over` choice with the Game over modal. Moves and deploy placement are D2.

**Architecture:** `viewer/src/lib/play/` holds one TypeScript module per ported Godot core file, members camelCased, bodies
ported line for line, each with the Godot test cases as Vitest tests. `play/decisionBox.ts` is `client/src/Hud/DecisionBox.cs`
and `DecisionBox.Picks.cs` without Godot: a controller that holds the open Ask's handlers (`onEnter`, `onCancel`, `onNumber`,
`onTarget`, `onModel`, `onUnit`, `onStop`) and publishes a `DecisionPanel` the Svelte `DecisionBox` draws. `PlayPage.svelte`
takes `GamePage`'s `playing`/`ended` branch from C2's interim `PlaybackView`. The projection gains Godot's frame stack.

**Tech Stack:** Vite 8, Svelte 5 (runes), TypeScript 5.9, Vitest 5, Node 24.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Goals; Anti-goals; Constraints → the decision kinds;
The builder's calls → the decision box). Prerequisites: `2026-10-24-browser-play-c1-shell.md` (Task 6 `GameConnection`, Task
7 `Board`, `BoardInteraction`, `Overlay`), `2026-10-24-browser-play-c2-setup.md` (Task 1 `GamePage`, `lobby/seats.ts`,
`lobby/lobbyView.ts`; Task 4 `sceneOfTable`). Continued by `-d2-moves.md` (Move, deploy placement, hand moves), `-d3-info.md`
(log, dice, undo, unit card), `-d4-table.md` (header, roster, rings, sight, menus, waiting, end-to-end). Godot sources ported:
`client/src/Core/Names.cs`, `LogText.cs`, `RuleTexts.cs`, `SpellText.cs`, `Seats.cs` (`Acting`, `PlayingAs`),
`AskViewModel.cs`, `HeaderView.cs`, `DecisionView.cs`, `DeployView.cs`, `TargetAssignment.cs`, `CasualtyPick.cs`,
`SealedReveals.cs`, `GameOverView.cs`, `libs/Tabletop.Protocol/WaitingForPlayers.cs`, `client/src/Core/WaitingView.cs`,
`EventApplier.cs` (the stack); `client/src/Hud/DecisionBox.cs`, `DecisionBox.Picks.cs`, `EndMessage.cs`.

## Findings that shape the plan

- **The viewer's projection has no frame stack.** `live/apply.ts` skips `sequence_started`, `step_completed`'s frame,
  `sequence_finished`/`abandoned` and the dice amendments ("The frame stack is not ported"). Godot reads `Projection.Stack` in
  `HeaderView.ActionFrame`, `SheetFocus`, `MoveRuler.UsedInFrame` and `MenuModel.ForFrame`. Task 1 ports it from
  `EventApplier.cs` with its faults; the recording agreement is untouched (`RecordedFrame` carries no stack).
- **Undo is local first in Godot** (`Session.UndoToAsync`: `Applier.ProjectTo(seq)` shows until the `Undone` lands). The
  connection exposes no applier, so Task 1 adds a pure `projectTo(board, game, log, seq)`; D3 builds the prediction on it.
- **C2's `lobby/seats.ts` has `mine`, `teamsOf`, `isCreator` only.** `Seats.Acting` and `Seats.PlayingAs` drive every play
  part (`Session.MyTeamId`); Task 3 adds them there.
- **Godot's table raises three click events** (`TableInput`): `UnitClicked` on every model click (the selection),
  `TargetClicked` for a piece in `Eligible`, `ModelClicked(id, right)`. C1's `BoardInteraction` gives `onSelect(hit, mods)` and
  `onContextMenu(hit, …)`; `PlayPage` maps them the same way (Task 7), so the controller keeps Godot's handler shape.
- **Settings Godot keeps in `settings.json`** that play reads (`MutedWindowKinds`, `RangeRings`, `LogOpen`, `LogFilter`) are
  per-viewer conveniences: `localStorage` `tabletop.play`, every access in try/catch, defaults when it throws (Task 6).
- **Rule texts** come from `GET /v1/properties/{hash}/rules` and grantable effects from `/grants` (`ManagementClient`); C1's
  `Api` has neither. Task 2 adds both; the cast option's tooltip (`SpellText.OfOption`) needs the texts in D1.
- **Every Godot string here is player copy.** Ports keep it byte for byte (`–`, `·`, `→`, `−`, `✕`, curly quotes as written).

## Global Constraints

- The browser judges nothing: every answer is `AnswerAsk { askId, answer, teamId }` through `GameConnection.sendCommand`; a
  refused outcome shows the server's detail.
- One module per Godot file, same members camelCased, same order, no restructuring; a reviewer reads the two side by side.
  Tests are the Godot test cases with their names as sentences.
- `src/lib/play/` imports `src/lib/geometry/` types only; D1 calls no geometry.
- Svelte components bind only; every decision is in a `.ts` module with a test.
- Files under 500 lines. Ids for messages are `GameConnection`'s; nothing here mints a command id.
- Commands: `cd viewer && npm test`, `cd viewer && npm run check`, `cd viewer && npm run build:hosted`.

## Review Focus

- One answer per Ask: `answer` unhooks before it sends, so a second Enter or click sends nothing. Test: `a second Enter after
  an answer sends nothing` (Task 6).
- A refusal of the box's last command shows `The server refused: {detail}` over the same Ask, rebuilt; another Ask clears it.
  Test: `a refusal re-renders the Ask with the server's words and a new Ask clears them` (Task 6).
- A muted window that the server refuses must not auto-pass again: the box falls through to its Pass button. Test: `a refused
  auto-pass renders the Ask with its Pass button` (Task 6).
- Self-play: an answer carries the team the Ask was rendered for, not the acting team when it is sent. Test: `the answer names
  the team the Ask was rendered for` (Task 6).
- Hover lighting (commit f34f24cd): the hovered option's pieces in `GLOW.hovered`, the rest of the Ask's glow dark, restored on
  leave and when the button goes. Test: `hovering an option lights its pieces and darkens the rest of the glow` (Task 6).

---
### Task 1: The frame stack and `projectTo`

**Files:** modify `viewer/src/lib/live/apply.ts`; test `viewer/tests/apply.test.ts` (new cases), `viewer/tests/agreement.test.ts`
(unchanged, must pass).

**Interfaces:**

```ts
// apply.ts — EventApplier's Frame, CompletedStep, FrameOf, Pop, ReplaceDice, ProjectTo
export type CompletedStep = {
  readonly step: string; readonly answer: AskAnswer | undefined; rolls: RollRecord[];
  readonly changes: readonly StateChange[]; readonly values: Readonly<Record<string, number>> | undefined;
  readonly applied: readonly string[] | undefined;
};
export type Frame = {
  readonly id: string; readonly sequence: string; readonly context: SequenceContext;
  readonly outline: readonly OutlineStep[]; readonly steps: CompletedStep[];
};
// Projection gains: readonly stack: Frame[];   (initialProjection: [])
/** EventApplier.ProjectTo: the effective log's events with seq <= `seq`, rebuilt on the board; lastSeq = seq. */
export function projectTo(board: Board, game: GameInfo, log: readonly GameEvent[], seq: number): Projection;
// Applier gains: projectTo(seq: number): Projection   (over its own log)
```

- [ ] Tests first (`apply.test.ts`, the cases of `tests/Tabletop.Protocol.Tests/EventApplierTests.cs` that touch the stack):
  `sequence_started pushes a frame with its context and outline`; `step_completed records step, answer, rolls, changes, values
  and applied on its frame and applies the changes`; `sequence_finished pops the frame and every frame above it` (three
  pushed, finish the middle → one left); `sequence_abandoned pops the same way`; `dice_rerolled replaces the faces at their
  indices in the frame's step` (and `dice_edited`); `a step for an unknown frame throws ProjectionDesyncError naming the frame`;
  `a die index out of range throws naming the roll`; `projectTo rebuilds the effective log up to seq` (skirmish fixture: for
  the seq of the 40th event, `projectTo(...).lastSeq` is that seq and its units' wounds equal a fresh `Applier` over the first
  40 events); `projectTo skips an undone range`.
- [ ] `cd viewer && npx vitest run tests/apply.test.ts` fails.
- [ ] Port the branches of `EventApplier.ApplyTo` (`SequenceStarted` … `DiceEdited`), `FrameOf`, `Pop`, `ReplaceDice` and
  `ProjectTo`/`Rebuild`; drop the "not ported" comment. `rolls` is copied (`[...s.rolls]`) so a replace never edits the event.
- [ ] `npm test` (the agreement test passes unchanged) and `npm run check`.
- [ ] Commit: `Viewer: the projection's frame stack and projectTo, ported from EventApplier`.

### Task 2: Names, log words, rule texts and spell text

**Files:** create `viewer/src/lib/play/names.ts`, `play/logText.ts`, `play/ruleTexts.ts`, `play/spellText.ts`,
`viewer/tests/play.ts` (builders); modify `viewer/src/lib/api/types.ts`, `viewer/src/lib/api/client.ts`; test
`viewer/tests/logText.test.ts`, `viewer/tests/ruleTexts.test.ts`, `viewer/tests/spellText.test.ts`, `viewer/tests/api.test.ts`.

**Interfaces:**

```ts
// names.ts — Names, member for member
export class Names {
  constructor(game: GameInfo, projection: Projection | null);
  participant(id: string | null | undefined): string;  get teams(): readonly Team[];  team(id: string): string;
  unit(id: string): string;  model(id: string): string;  teamOfModel(id: string): string | null;  weapon(id: string): string;
  teamOfUnit(id: string): string | null;  unitOfModel(id: string): string | null;  piece(id: string): string;
  marker(id: string): string;  static short(id: string): string;    // the id without dashes, first eight characters
}
// logText.ts — LogText: describe, roundChange, answer, touched (and the private moved, flag, parts, step, result)
export function describe(e: GameEvent, n: Names): string;
export function roundChange(c: StateChange, n: Names): string;
export function answer(a: AskAnswer, n: Names): string;
export function touched(e: GameEvent): string[];
// ruleTexts.ts — RuleTexts and GrantChoices
export interface RuleTextDto { readonly name: string; readonly kind: string; readonly takesParameter: boolean;
  readonly ruleText: string; readonly played?: boolean }                     // played absent reads true
export interface GrantableEffectDto { readonly entryName: string; readonly effectId: string; readonly duration: string }
export class RuleTexts { static readonly empty: RuleTexts; static from(rows: readonly RuleTextDto[]): RuleTexts;
  readonly loaded: boolean; textOf(name: string): string | null; isAutomated(name: string): boolean; notAutomated(name: string): boolean }
export interface GrantChoice { readonly label: string; readonly entryName: string; readonly effectId: string }
export function grantChoices(effects: readonly GrantableEffectDto[]): GrantChoice[];
/** Session.FetchRuleTexts: rules, then grants; a failure yields null (nothing cached, the next game frame tries again). */
export function fetchRuleTexts(api: Api, hash: string): Promise<{ rules: RuleTexts; grants: GrantChoice[] } | null>;
// spellText.ts — SpellText
export function nameOfOption(optionId: string): string;
export function split(label: string): { name: string; cost: number | null };
export function tooltip(rules: RuleTexts, name: string, cost?: number | null): string | null;
export function ofOption(rules: RuleTexts, optionId: string, label: string): string | null;
// Api gains (client.ts)
ruleTexts(hash: string): Promise<RuleTextDto[]>;               // GET /v1/properties/{hash}/rules
grantableEffects(hash: string): Promise<GrantableEffectDto[]>; // GET /v1/properties/{hash}/grants
```

`tests/play.ts` builds what the play tests share, after `tests/Tabletop.Client.Tests/TableUx.cs` and `Samples`: `gameOf(teams)`
(two participants on teams 1 and 2 unless told), `slotOf({ name, base, weapons, properties, stats })`, `unitOf(...)`,
`boardOf(armies, table?)`, `projectionOf(game, board, events)` (an `Applier`), `ev(seq, payload, actor?)`, `askOf(legal, {
question, teams, allowPass, windowKind, frameId, sealed })`.

- [ ] Tests first. `logText.test.ts` (ViewModelTests' cases): `log lines name actor and subject`; `an answer names the pieces
  it carries`; `game ended names the winner or the tie`; `an answer that broke a named rule is labelled with it`; `control reads
  as taking a marker or making it neutral`; plus `names fall back to the first eight characters of the id`.
  `ruleTexts.test.ts` (RuleTextsTests, all seven) and `each effect is a choice named by its entry and numbered when the entry has
  several` (AddGrantMenuTests); `fetchRuleTexts returns null when the rules request fails and empty grants when only grants
  fail`. `spellText.test.ts` (SpellTextTests, all four). `api.test.ts`: `ruleTexts and grantableEffects get the pinned set's
  rows with the bearer`.
- [ ] `cd viewer && npx vitest run tests/logText.test.ts tests/ruleTexts.test.ts tests/spellText.test.ts tests/api.test.ts` fails.
- [ ] Port the four files; `RuleTexts.From` keeps the first row of a repeated name; `Names.short` matches `ToString("N")[..8]`.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: names, log words, rule texts and spell text, ported from Godot's core`.

### Task 3: Acting team, the Ask that is mine, the header's words and the wait for players

**Files:** modify `viewer/src/lib/lobby/seats.ts`; create `viewer/src/lib/play/askView.ts`, `play/headerView.ts`,
`play/waiting.ts`; test `viewer/tests/seats.test.ts`, `viewer/tests/askView.test.ts`, `viewer/tests/headerView.test.ts`,
`viewer/tests/waiting.test.ts`.

**Interfaces:**

```ts
// seats.ts gains — Seats.Acting, Seats.PlayingAs
export function acting(p: Projection | null, mine: readonly string[], log?: readonly GameEvent[]): string | null;
export function playingAs(game: GameInfo, mine: readonly string[], acting: string | null): string | null;
// askView.ts — AskViewModel
export type AskMode = 'choose' | 'pickPieces' | 'allocate' | 'assign' | 'move' | 'tokenSpend' | 'confirm';
export interface AskView { readonly ask: Ask; readonly mode: AskMode; readonly highlights: readonly string[];
  readonly answeredTeams: readonly string[]; readonly autoPass: boolean }
export function modeOf(legal: LegalSet): AskMode;
export function spendableBy(p: Projection, game: GameInfo, team: string, legal: TokenSpend): Map<string, number>;
export function mine(p: Projection, game: GameInfo, myTeam: string | null, muted: ReadonlySet<string>): AskView | null;
export function waitingFor(p: Projection, game: GameInfo, myTeam: string | null): string | null;
export function confirmLabel(ask: Ask): string;
export function answeredBy(log: readonly GameEvent[], askId: string, myTeam: string | null, me: string | null, n: Names): string | null;
// headerView.ts — HeaderView (the bar itself is D4)
export type OutlineState = 'done' | 'current' | 'next';
export interface HeaderModel { readonly round: number; readonly turn: string; readonly scores: readonly (readonly [string, number])[];
  readonly activeUnit: string | null; readonly action: string | null; readonly outline: readonly { label: string; state: OutlineState }[];
  readonly waiting: string | null; readonly result: string | null; readonly topLine: string }
export function headerOf(p: Projection, game: GameInfo, n: Names): HeaderModel;
export function actionFrame(p: Projection): Frame | null;
export function notActivated(p: Projection, game: GameInfo): Map<string, number>;
export function objectives(p: Projection): { markerId: string; teamId: string | null }[];
export function activationLine(p: Projection, game: GameInfo, n: Names, myTeam: string | null): string | null;
export function outline(frame: Frame): { label: string; state: OutlineState }[];
export function waiting(p: Projection, n: Names, mine?: readonly string[]): string | null;
export function waitingForPlayers(waiting: readonly TeamWaiting[]): string | null;
export const WATCHING: string, REPLAYING: string;
export function outcome(ended: GameEnded, n: Names): string;
// waiting.ts — WaitingForPlayers (libs/Tabletop.Protocol) and WaitingView
export interface TeamWaiting { readonly teamId: string; readonly ordinal: number; readonly hasPlayer: boolean; readonly text: string }
export function waitingTeams(game: GameInfo, board: Board): TeamWaiting[];
export function waitHolds(game: GameInfo, board: Board | null, lastSeq: number): boolean;
export const WAITING_TITLE: string;
export interface WaitingLine { readonly text: string; readonly join: TeamChoice | null; readonly army: ArmySeat | null }
export function waitingLines(game: GameInfo, waiting: readonly TeamWaiting[], participantId: string | null): WaitingLine[];
export function linesChanged(shown: readonly WaitingLine[] | null, now: readonly WaitingLine[]): boolean;
/** Session.IsWatching without the replay arm: an admin watcher, or from playing on a participant with no team while no wait holds. */
export function isWatching(state: ConnectionState): boolean;
```

`TeamChoice` and `ArmySeat` are C2's (`lobby/lobbyView.ts`); a Join choice's edit is `PickTeam(teamId)`.

- [ ] Tests first, Godot's cases: `seats.test.ts` (SeatsTests' first four, TeamAsksTests' three); `askView.test.ts`
  (ViewModelTests: `my ask is the one addressed to my team and others are a wait`, `answered by names a teammate's live answer for
  my team only`, `a muted window auto passes and a simultaneous spend shows who answered without what`; plus `the spend map keeps
  my own models only`, `a move's highlights add the containers it offers`, `end_round confirms as End the round`);
  `headerView.test.ts` (HeaderViewTests' eleven, HeaderAdditionsTests' two); `waiting.test.ts` (SeatsTests' last three, `a team
  with a seat and an army is no wait`, `the wait holds only on an empty log`, `an admin watcher and a teamless participant
  watch, but not while the wait holds`).
- [ ] `cd viewer && npx vitest run tests/seats.test.ts tests/askView.test.ts tests/headerView.test.ts tests/waiting.test.ts` fails.
- [ ] Port; `mine` orders open Asks by id as strings (UUIDv7 ids sort as `Guid` does); `isWatching` reads
  `participantId === '00000000-0000-0000-0000-000000000000'` as the admin watcher.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: acting team, my Ask, the header's words and the wait for players, ported`.

### Task 4: Numbering, narrowing, notes and the deploy pick

**Files:** create `viewer/src/lib/play/decisionView.ts`, `play/deployView.ts`; test `viewer/tests/decisionView.test.ts`,
`viewer/tests/deployView.test.ts`.

**Interfaces:**

```ts
// decisionView.ts — DecisionView
export interface Numbered { readonly key: number; readonly label: string; readonly optionId: string }   // key 0: past nine
export interface DecisionModel { readonly ask: Ask | null; readonly mode: AskMode | null; readonly title: string;
  readonly numbered: readonly Numbered[]; readonly hint: string; readonly autoPass: boolean }
export interface ChoiceRow { readonly option: AskOption | null; readonly note: string | null }
export function decisionOf(p: Projection, game: GameInfo, n: Names, myTeam: string | null, muted: ReadonlySet<string>): DecisionModel;
export function number(choices: readonly AskOption[]): Numbered[];
export function clickAnswers(options: Options): Set<string>;
export function choicesOf(options: Options, p: Projection, unitId: string): AskOption[];
export function rows(shown: readonly AskOption[]): ChoiceRow[];
export function noteOf(p: Projection, game: GameInfo, myTeam: string | null, muted: ReadonlySet<string>, unitId: string): string | null;
export function stopLabel(p: Projection, m: MoveRegion): string | null;
// deployView.ts — DeployView, whole (Layout and Around serve D2)
export interface MidDeploy { readonly unitId: string; readonly question: string; readonly shelvedModelIds: readonly string[] }
export const DEPLOY_QUESTIONS: readonly string[];      // deploy, deploy_scouts, deploy_ambusher, deploy_reinforcement
export function midDeploy(p: Projection, log: readonly GameEvent[], myTeam: string | null): MidDeploy | null;
export function layout(models: readonly ModelInPlay[], at: Point3, size: TableSize): ModelRelocation[];
export function around(models: readonly ModelInPlay[], anchor: Placement, anchorBase: Base, size: TableSize, clearance?: number): ModelRelocation[];
export function pickable(p: Projection, ask: Ask): string[];
export function offTable(p: Projection, eligible: Iterable<string>): string[];
```

- [ ] Tests first: `decisionView.test.ts`, DecisionViewTests' eleven (`options are numbered and the hint names the keys`, `past
  nine an entry has no key`, `a deploy ask numbers the shelved units and a target pick asks for a click`, `a stoppable move offers
  cast when a moving model holds spell tokens`, `each mode has its hint and casualties title the count`, `nothing addressed to me
  gives no ask`, `a muted window is an auto pass`, `only a piece one choice names answers a click`, `a unit narrows the box to its
  choices numbered from one`, `a unit's note shows once after its buttons and an unnoted unit shows none`, `the selected unit's
  note is read off my open choice`). The Move hint is asserted word for word: `Drag to move · Shift: one model · 1–5 while
  holding: rows · Q/E: turn · Enter: commit · Esc: reset`. `deployView.test.ts`: DeployViewTests' eight, DeployAroundTests'
  three, and PassengerMenuTests' `a deploy pick lists only units with a model on the shelf`.
- [ ] `cd viewer && npx vitest run tests/decisionView.test.ts tests/deployView.test.ts` fails.
- [ ] Port both files; `midDeploy` walks `effectiveLog`; `layout` and `around` keep `PerRow = 5`, `Gap = 0.2`, `MaxRings = 64`.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: decision numbering, narrowing and notes; the deploy pick and layouts, ported`.

### Task 5: Targets, casualties, reveals and the game-over choice

**Files:** create `viewer/src/lib/play/targetAssignment.ts`, `play/casualtyPick.ts`, `play/sealedReveals.ts`,
`play/gameOverView.ts`; test `viewer/tests/targetAssignment.test.ts`, `viewer/tests/casualtyPick.test.ts`,
`viewer/tests/sealedReveals.test.ts`, `viewer/tests/gameOverView.test.ts`.

**Interfaces:**

```ts
export class TargetAssignment {            // TargetAssignment
  constructor(legal: Assignment);
  get groups(): readonly AskOption[]; get picked(): ReadonlySet<string>; get assigned(): ReadonlyMap<string, string>;
  pickOnly(groupId: string): void; canTarget(unitId: string): boolean; get candidates(): string[];
  aim(targetUnitId: string): boolean; clear(): void; answer(): Assigned | null;
  static chip(group: AskOption, target?: string | null): string;   // `Label ×n`, `… → target`, `… · holds fire`
}
export function targetsOf(legal: Assignment, itemId: string): readonly string[];   // Assignment.TargetsOf
export class CasualtyPick {                // CasualtyPick
  constructor(legal: Allocation, woundsLeft: ReadonlyMap<string, number>);
  get counts(): ReadonlyMap<string, number>; get chosen(): number; get complete(): boolean; get required(): number;
  get oneEach(): boolean; get title(): string; get hint(): string; get status(): string;
  add(id: string): void; click(id: string): void; remove(id: string): void; clear(): void; auto(): void; answer(): Allocated | null;
}
export class SealedReveals { read(events: readonly GameEvent[], n: Names): void; drain(): string[] }
export type GameOverStage = 'none' | 'choose' | 'waiting' | 'ended';
export interface GameOverModel { readonly stage: GameOverStage; readonly askId: string | null; readonly result: string | null;
  readonly reason: string; readonly choices: readonly AskOption[]; readonly note: string | null }
export const WAITING_NOTE = 'Waiting for the other player';
export function gameOverOf(p: Projection, game: GameInfo, myTeam: string | null): GameOverModel;
```

- [ ] Tests first, Godot's cases: TargetAssignmentTests' ten, CasualtyPickTests' nine, SealedRevealsTests' two,
  GameOverViewTests' three, plus `targetsOf falls back to every target without a per-item map and to none for an unlisted item`.
- [ ] `cd viewer && npx vitest run tests/targetAssignment.test.ts tests/casualtyPick.test.ts tests/sealedReveals.test.ts tests/gameOverView.test.ts`
  fails.
- [ ] Port. Map iteration keeps insertion order, as `Dictionary` does for these adds, so `CasualtyPick.answer` puts the surplus
  on the last model counted.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: target assignment, casualty pick, sealed reveals and the game-over model, ported`.

### Task 6: The decision box controller

**Files:** create `viewer/src/lib/play/settings.ts`, `play/decisionBox.ts`; test `viewer/tests/settings.test.ts`,
`viewer/tests/decisionBox.test.ts`.

**Interfaces:**

```ts
// settings.ts — the play settings ClientSettings holds, per viewer
export type LogFilter = 'all' | 'rolls' | 'mine' | 'unit';
export interface PlaySettings { readonly mutedWindowKinds: readonly string[]; readonly rangeRings: boolean;
  readonly logOpen: boolean; readonly logFilter: LogFilter }
export const DEFAULT_SETTINGS: PlaySettings;            // [], false, false, 'all'
export function loadSettings(storage: StorageLike | null): PlaySettings;   // 'tabletop.play'; unreadable → defaults
export function saveSettings(storage: StorageLike | null, s: PlaySettings): void;   // a throwing storage is ignored
// decisionBox.ts — DecisionBox.cs + DecisionBox.Picks.cs
export type Control =
  | { readonly kind: 'button'; readonly id: string; readonly label: string; readonly disabled: boolean;
      readonly tooltip?: string; readonly pieces?: readonly string[] }        // pieces: lit while the pointer is on it
  | { readonly kind: 'note' | 'label' | 'reason'; readonly text: string }   // note and reason in warning colour
  | { readonly kind: 'chip'; readonly id: string; readonly label: string; readonly pressed: boolean }
  | { readonly kind: 'spin'; readonly id: string; readonly label: string; readonly max: number; readonly value: number };
export interface DecisionPanel { readonly seat: string; readonly title: string; readonly revealed: string | null;
  readonly note: string; readonly controls: readonly Control[]; readonly refusal: string | null; readonly hint: string;
  readonly visible: boolean }
export type Glow = 'selected' | 'hovered' | 'ghost';     // Palette.Selected, Hovered, Ghost
export interface DecisionHost {
  state(): ConnectionState; myTeams(): readonly string[]; acting(): string | null;
  settings(): PlaySettings; updateSettings(change: (s: PlaySettings) => PlaySettings): void;
  rules(): RuleTexts; send(command: Command): Promise<SendOutcome>;
}
/** DecisionBox.Move.cs's half, filled by D2; null renders a move's title and hint with no controls. */
export interface MoveHooks { renderMove(box: DecisionBox, mine: AskView, p: Projection, n: Names): void;
  startDeploy(box: DecisionBox, mid: MidDeploy, n: Names): void; keepGhosts(box: DecisionBox): void;
  refused(box: DecisionBox, token: string, detail: string): void; unhook(box: DecisionBox): void }
export class DecisionBox {
  constructor(host: DecisionHost, moves?: MoveHooks | null);
  get panel(): DecisionPanel; subscribe(listener: (panel: DecisionPanel) => void): () => void;
  render(): void;                                     // Frame.Render: every connection state change
  reveal(events: readonly GameEvent[]): void;         // EventsApplied: SealedReveals.read
  press(controlId: string): void; hover(controlId: string | null): void; spin(controlId: string, value: number): void;
  number(key: number): boolean; enter(): boolean; cancel(): boolean; stop(): boolean;   // keys; false when unused
  target(pieceId: string): void; model(modelId: string, right: boolean): void; unit(unitId: string): void;   // table clicks
  glowOf(modelId: string, unitId: string): Glow | null;      // Glow, Light and the picks' Ghost marks, for the board
  get eligible(): ReadonlySet<string>;                       // TableInput.Eligible
  get marks(): ReadonlyMap<string, string>;                  // casualty marks: '✕' or '✕ n/left'
  get targets(): TargetAssignment | null;                    // declare_targets (D4 draws its lines and sight)
}
```

The controller ports `Render`, `RenderAsk`, `RenderChoose`, `Glow`, `Light`, `RenderSpend`, `Number`, `Resolve`, `Reveal`,
`Send`, `Report`, `Answer`, `Unhook`, `Reset` and `RenderPick`, `RenderTargets`, `RenderCasualties` in their order. `Answer`
unhooks, disables the buttons, takes a fresh token as `_lastCommand`, then awaits `send(AnswerAsk(ask.id, answer, answering))`;
an outcome with `refused` whose token is still the last is Godot's `_onRefused` (`The server refused: {detail}`, the Ask
rebuilt, `moves.refused` first); a thrown `SendError` is `Report` (`Could not send the answer: {message}`). Mute and Unmute go
through `updateSettings`. Wounds left for casualties read `state().maxWounds` (absent: 1) less the model's wounds.

- [ ] Tests first (`decisionBox.test.ts`, a fake host recording sends and settling them as told, projections from `tests/play.ts`):
  `nothing open for my team shows the header's waiting line`; `while deployment waits the title lists each team's wait`;
  `choose numbers its options and a number key answers Chosen`; `a click on a piece one choice names answers it`; `a unit several
  choices name narrows the box, renumbered, and Esc widens it`; `hovering an option lights its pieces and darkens the rest of the
  glow` (`glowOf` hovered for its pieces, null for the other glowing ones, selected again after `hover(null)`); `a cast option
  carries its spell's tooltip`; `token spend offers a box per caster of mine and sends only non-zero spends`; `confirm answers on
  Enter and the end_round button says End the round (Enter)`; `pick pieces toggles a piece and one pick of one answers at once`;
  `a deploy pick lists shelved units as numbered buttons`; `targets aim the picked groups, a chip picks its group alone, Enter
  fires and Esc clears`; `casualties add on click, take one away on right-click, fill on Auto and mark ✕ n/left`; `pass and mute
  each send or store once`; `a muted window auto-passes once and offers Unmute`; `a refused auto-pass renders the Ask with its
  Pass button`; `a refusal re-renders the Ask with the server's words and a new Ask clears them`; `a failed send shows Could not
  send the answer`; `a second Enter after an answer sends nothing`; `the answer names the team the Ask was rendered for`; `a
  teammate's answer reads Name answered for your team`; `a reveal shows until the render after a new Ask`; `Playing as Team 2
  heads the box for an account with two teams`. `settings.test.ts`: `defaults when storage is empty, corrupt or throws`;
  `a saved setting reads back`.
- [ ] `cd viewer && npx vitest run tests/decisionBox.test.ts tests/settings.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the decision box controller — choose, picks, targets, casualties, spend, confirm, pass, mute, refusals`.

### Task 7: The play page, the board's glow, the decision box and the Game over modal

**Files:** create `viewer/src/lib/play/scene.ts`, `play/keys.ts`, `play/PlayPage.svelte`, `play/DecisionBox.svelte`,
`play/GameOverModal.svelte`; modify `viewer/src/lib/board/draw.ts`, `viewer/src/lib/board/palette.ts`,
`viewer/src/lib/game/GamePage.svelte` (the `playing`/`ended` branch); test `viewer/tests/playScene.test.ts`,
`viewer/tests/keys.test.ts`.

**Interfaces:**

```ts
// scene.ts — the board of a game in play: C2's sceneOfTable for terrain, markers and zones, then every model on the table
/** Ghosts (a move's or a deploy's proposed placements) override where a model is drawn; shelf and inside models go to offTable;
 *  markers placed in play are added, an objective coloured by its controller (projection.control). */
export function sceneOfPlay(p: Projection, game: GameInfo, zones: ZonePolygons, name: (piece: TerrainPlacement) => string,
  ghosts?: ReadonlyMap<string, Placement>): Scene;
// keys.ts — Hotkeys.Of, the keys the browser binds
export type Intent = { kind: 'option'; key: number } | { kind: 'confirm' | 'cancel' | 'turnLeft' | 'turnRight' | 'stopHere'
  | 'undo' | 'sight' | 'toggleRangeRings' | 'toggleLog' | 'singleModel' | 'remove' };
export function intentOf(key: string, mods: Modifiers): Intent | null;   // KeyboardEvent.key; Ctrl+Z undo, other Ctrl chords none
/** TableInput.TextFocused: a key typed into an input, textarea, select or contenteditable is no hotkey. */
export function textFocused(target: EventTarget | null): boolean;
// draw.ts: DrawOptions gains glow?: (modelId: string, unitId: string) => Glow | null, and marks?: ReadonlyMap<string, string>
//   (a label over the model's circle). palette.ts gains GLOW: Record<Glow, string> from client/src/Table/Palette.cs
//   (selected #ffffff, hovered #40e6ff, ghost rgba(230,230,255,0.35)).
```

`PlayPage` (props `connection`, `api`, `view` bindable): subscribes to the connection; `myTeams = teamsOf(game, participantId)`,
`acting(projection, myTeams, log)`; loads the geometry for zone polygons and the rule texts (`fetchRuleTexts` once per hash, from
playing on); holds one `DecisionBox` (host over the connection, `localStorage` settings) and calls `render()` on every state and
`reveal()` on every `onEvents` batch. The board (`models: 'circles'`, `sceneOfPlay`, `glow: box.glowOf`, `marks: box.marks`)
maps clicks as `TableInput.OnLeftDown` does: a click on a model in `box.eligible` (the model or its unit) answers, `box.model(id,
false)` when the model is listed, then `box.target(model or unit id)`, and neither selects nor drags (`onDragStart` false); any
other model click selects its unit (`box.unit`); a right click on an eligible model is `box.model(id, true)`. A window
`keydown` (skipped when `textFocused`) feeds `intentOf` to `number`/`enter`/`cancel`/`stop`. `DecisionBox.svelte`
draws the panel (seat, title, revealed, note, controls, refusal in warning colour, hint), sends `press`/`hover`/`spin`, and its
buttons never take focus. `GameOverModal` is `EndMessage.cs`: a `<dialog>` over `gameOverOf` that opens once per `game_over` Ask
(End and Continue answer it through `AnswerAsk`, then `WAITING_NOTE`) and once when the game ends (result, reason, scores; Stay or
Esc closes, Leave goes to `#/games`); the header's Game over button (D4) reopens it.

- [ ] Tests first: `playScene.test.ts`: `models on the table draw at their placement, a ghost overrides it`; `shelf and inside models
  are off the table`; `a controlled objective takes its team's ordinal`. `keys.test.ts` (HotkeysTests for the bound keys):
  `digits are options`; `Enter, Escape, Q, E, C map`; `Ctrl+Z is undo and other Ctrl chords are nothing`; `a key in a text field
  is no hotkey`.
- [ ] `cd viewer && npx vitest run tests/playScene.test.ts tests/keys.test.ts` fails.
- [ ] Implement the modules, the draw options and the three components; `GamePage` renders `PlayPage` from `playing` on.
- [ ] `npm test && npm run check && npm run build:hosted`.
- [ ] Commit: `Viewer: the play page — board glow, the decision box and the Game over modal`.
