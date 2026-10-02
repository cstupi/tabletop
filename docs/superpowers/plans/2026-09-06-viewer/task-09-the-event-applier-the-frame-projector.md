# Task 9: The event applier, the frame projector and the log lines, ported to TypeScript

**Files:**
- Create: `viewer/src/lib/live/apply.ts`, `viewer/src/lib/live/logLines.ts`, `viewer/src/lib/live/project.ts`
- Test: `viewer/tests/apply.test.ts`

**Interfaces:**
- Consumes: from `wire.generated.ts` — `Board`, `GameInfo`, `GameEvent`, `EventPayload`, `StateChange`, `Ask`, `AskAnswer`, `LegalSet`, `Location`, `Unit`, `ModelSlot`, `Marker`, `Grant`, `RollRecord`, `TerrainPlacement`, `Volume`; from `recording.generated.ts` — `RecordedBoard`, `RecordedFrame` and every `Recorded*` member; `DECLARATIONS` (Task 4).
- Produces (`apply.ts`): `type UnitInPlay`, `type ModelInPlay`, `type SchedulerPosition`, `type OpenAsk`, `type Projection` as declared below; `class ProjectionDesyncError extends Error` with `readonly kind: string` and `readonly unknownId: string`, the names C#'s `ProjectionDesyncException` uses; `function initialProjection(board: Board, game: GameInfo): Projection`; `function applyChange(p: Projection, change: StateChange): void`; `function applyTo(p: Projection, event: GameEvent): void`; `function effectiveLog(log: readonly GameEvent[]): readonly GameEvent[]`; `class Applier` with `constructor(board: Board, game: GameInfo)`, `readonly log`, `get projection()`, `apply(event: GameEvent): void`, `applyAll(events: Iterable<GameEvent>): void`.
- Produces (`logLines.ts`): `function lineOf(event: GameEvent, after: Projection, ordinals: ReadonlyMap<string, number>): string`; `function shortId(id: string): string`.
- Produces (`project.ts`): `function recordedBoardOf(board: Board, game: GameInfo): RecordedBoard`; `function declarationsOf(wire: string): readonly string[]`; `class FrameProjector` with `constructor(board: Board, game: GameInfo)`, `readonly board: RecordedBoard`, `add(event: GameEvent): RecordedFrame`, `get frames(): readonly RecordedFrame[]`, `get projection(): Projection`.
- These three files are the port `Tabletop.Protocol.EventApplier` (contracts plan Task 14), `Tabletop.Sim.Recording.LogLines` and `Tabletop.Sim.Recording.FrameProjector` (simulation plan Task 5) already are in C#. **They are a port, not a design**: every branch below matches a `case` in the C#, and Task 10 proves it by replaying a recording through both. Nothing here decides anything; the applier "copies. It never decides legality, never derives a location, never clears a store on its own."

Spec: spec → *Two feeds* — "the socket carries events, so the viewer must apply them, so `Tabletop.Protocol`'s message types and the event applier exist a second time in TypeScript. **Accepted, with the drift generated away where possible**."

**An unknown id throws `ProjectionDesyncError`, matching the C# site for site.** `EventApplier` throws `ProjectionDesyncException`, carrying the event kind and the unknown id, wherever it would otherwise index a dictionary by an id it does not hold. The port must fault on the same input or the agreement test is comparing two different appliers. Written with `!`, the TypeScript would not: `p.models.get(id)!.wounds = w` throws an untyped `TypeError`, which is at least loud, but `{ ...p.markers.get(id)!, position }` on a missing marker spreads `undefined` and **succeeds**, writing a marker with no `id` into the store — a silent divergence at exactly the case the C# exception exists for, a marker placed during setup and then moved. So every read-through-a-lookup site goes through `unitIn`, `modelIn`, `markerIn` or `askIn`, which throw `ProjectionDesyncError(kind, unknownId)`; no `!` on a `Map.get` survives in `apply.ts`. Those four helpers cover twelve of the sixteen lookups the contracts plan lists as fallible — four on `ModelsMut`, six on `UnitsMut`, one on `MarkersMut`, one on `OpenAsksMut`. The other four are the frame-stack ones, `FrameOf` under `step_completed` and `modifier_added` and the roll lookup inside `ReplaceDice`, and the port has no stack to look in: those kinds are no-ops here, so the C# can fault on an unknown `frameId` where this cannot. That is the same divergence the unported stack already causes and is not a new one. A comment in `applyTo` names every stack-site fault the port leaves unported: `Pop` on `sequence_finished` and `sequence_abandoned`, `FrameOf`, and `ReplaceDice`'s roll and die-index lookups. The four sites where the C# writes through an indexer or calls `Remove` — `score_set`, `marker_placed`, `marker_removed` and `ask_closed` — fault on neither side, and the code says so, because a port that threw where the C# returns quietly would be just as wrong. Task 10 pins the pair.

**What the viewer does with a desync** is Task 11's: the live feed reports it and stops, rather than skipping the change and carrying on. The recording feed cannot raise one, because a recording carries frames rather than events and never reaches `applyTo`; only the agreement test and the live socket run this code.

**The resolution stack is not ported.** C#'s `Projection.Stack` holds `Frame`s: `sequence_started` pushes one, `sequence_finished` and `sequence_abandoned` pop one, and `dice_rerolled`, `dice_edited` and `modifier_added` amend a completed step's rolls. `RecordedFrame` carries no stack, and its `rolls` come from the `step_completed` payload rather than from the stack, so the C# recording does not show a reroll in `rolls` either. Six event kinds therefore change nothing a frame records, and porting the stack would add code no field reads. `applyTo` handles those six kinds as no-ops with that reason on them, and `apply.test.ts` pins it.

- [ ] **Step 1: Write `viewer/tests/apply.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { Applier, ProjectionDesyncError, effectiveLog, initialProjection } from '../src/lib/live/apply';
import { lineOf } from '../src/lib/live/logLines';
import { FrameProjector, declarationsOf } from '../src/lib/live/project';
import type { Board, GameEvent, GameInfo } from '../src/lib/protocol/wire.generated';

const T1 = '0198b7ac-0000-7000-8000-000000000001';
const T2 = '0198b7ac-0000-7000-8000-000000000002';
const P1 = '0198b7ac-0000-7000-8000-000000000011';
const U1 = '0198b7ac-0000-7000-8000-000000000021';
const M1 = '0198b7ac-0000-7000-8000-000000000031';
const K1 = '0198b7ac-0000-7000-8000-000000000041';
const K9 = '0198b7ac-0000-7000-8000-000000000049';
const A1 = '0198b7ac-0000-7000-8000-000000000051';
const F1 = '0198b7ac-0000-7000-8000-000000000061';
const R1 = '0198b7ac-0000-7000-8000-000000000071';
const R2 = '0198b7ac-0000-7000-8000-000000000072';

const game = {
  id: '0198b7ac-0000-7000-8000-0000000000ff', name: 'Friday', status: 'playing', createdBy: P1,
  gameRules: 'grimdark-future', propertiesHash: 'sha256:x', schedulerShape: 'alternating-activation',
  enforcement: 'strict', waitsBetweenSteps: false,
  teams: [{ id: T1, ordinal: 1 }, { id: T2, ordinal: 2 }],
  participants: [{ id: P1, accountId: P1, name: 'Clay', teamId: T1 }],
} as unknown as GameInfo;

const board = {
  table: {
    id: '0198b7ac-0000-7000-8000-0000000000fe', size: { width: 72, length: 48 },
    texture: { builtin: 'grass' },
    terrain: [{
      id: '0198b7ac-0000-7000-8000-0000000000fd', mesh: { builtin: 'ruin' },
      position: { x: 20, y: 10, z: 0 }, yaw: { degrees: 90 }, scale: 2,
      volumes: [{ id: '0198b7ac-0000-7000-8000-0000000000fc', volume: {
        kind: 'solid', footprint: { kind: 'rect', a: 3, b: 1 }, offset: { x: 1, y: 0 },
        yaw: { degrees: 0 }, bottom: 0, top: 4, declarations: 'impassable, blocks_line_of_sight' } }],
    }],
    markers: [{ id: K1, kind: 'objective', position: { x: 36, y: 24, z: 0 }, yaw: { degrees: 0 }, diameter: 2, label: 'A', visual: { builtin: 'chip' } }],
    zones: [{ id: '0198b7ac-0000-7000-8000-0000000000fb', shape: 'frontline/1', teamOrdinal: 1 }],
  },
  armies: [{ participantId: P1, snapshot: { id: '0198b7ac-0000-7000-8000-0000000000fa', name: 'Vanguard', gameSystem: 'grimdark-future',
    units: [{ id: U1, ordinal: 1, name: 'Rifles', stats: {}, properties: [], models: [
      { id: M1, ordinal: 1, name: 'Rifleman', stats: {}, properties: [], base: { shape: 'circle', a: 1, b: 1 }, height: 1.2, weapons: [] },
    ] }] } }],
} as unknown as Board;

const ev = (seq: number, payload: unknown): GameEvent => ({ seq, payload } as unknown as GameEvent);
const ordinals = new Map([[T1, 1], [T2, 2]]);

describe('the ported applier', () => {
  it('shelves every model and zeroes every score', () => {
    const p = initialProjection(board, game);
    expect(p.models.get(M1)!.location.kind).toBe('shelf');
    expect(p.units.get(U1)!.participantId).toBe(P1);
    expect(p.scores.get(T1)).toBe(0);
    expect(p.scheduler.round).toBe(0);
    expect(p.lastSeq).toBe(0);
  });

  it('copies what every state change says, and nothing more', () => {
    const a = new Applier(board, game);
    a.applyAll([
      ev(1, { kind: 'models_moved', moves: [{ modelId: M1, from: { kind: 'shelf' }, to: { kind: 'table', placement: { position: { x: 6, y: 6, z: 0 }, yaw: { degrees: 45 } } } }] }),
      ev(2, { kind: 'wounds_set', modelId: M1, wounds: 2 }),
      ev(3, { kind: 'condition_set', subject: { kind: 'unit', id: U1 }, condition: 'shaken', present: true }),
      ev(4, { kind: 'action_recorded', unitId: U1, action: 'Advance' }),
      ev(5, { kind: 'grant_added', unitId: U1, grant: { id: R1, effectId: R1, entryName: 'Regeneration', casterUnitId: U1, remainingUses: 1 } }),
      ev(6, { kind: 'spell_tokens_set', modelId: M1, tokens: 3 }),
      ev(7, { kind: 'wounds_this_activation_set', unitId: U1, wounds: 2 }),
      ev(8, { kind: 'score_set', teamId: T1, score: 4 }),
      // A setup marker is not projection state — `initialProjection` seeds no markers, exactly as C#'s
      // `Projection.Initial` does not — so a marker has to be placed by an event before it can be moved.
      // `marker_moved` naming a setup marker is a desync, pinned in its own test.
      ev(9, { kind: 'marker_placed', marker: { id: K1, kind: 'objective', position: { x: 1, y: 1, z: 0 },
        yaw: { degrees: 0 }, diameter: 2, visual: { builtin: 'chip' } } }),
      ev(10, { kind: 'marker_moved', markerId: K1, position: { x: 5, y: 5, z: 0 }, yaw: { degrees: 90 } }),
    ]);
    const p = a.projection;
    expect(p.models.get(M1)!.location).toEqual({ kind: 'table', placement: { position: { x: 6, y: 6, z: 0 }, yaw: { degrees: 45 } } });
    expect(p.models.get(M1)!.wounds).toBe(2);
    expect(p.models.get(M1)!.spellTokens).toBe(3);
    expect([...p.units.get(U1)!.conditions]).toEqual(['shaken']);
    expect([...p.units.get(U1)!.actions]).toEqual(['Advance']);
    expect(p.units.get(U1)!.grants.map((g) => g.entryName)).toEqual(['Regeneration']);
    expect(p.units.get(U1)!.woundsThisActivation).toBe(2);
    expect(p.scores.get(T1)).toBe(4);
    expect(p.markers.get(K1)!.position.x).toBe(5);
    expect(p.lastSeq).toBe(10);
  });

  it('follows the four scheduler events, and a pass stands until the next round', () => {
    const a = new Applier(board, game);
    a.applyAll([
      ev(1, { kind: 'round_started', round: 1, order: [T1, T2], changes: [{ kind: 'action_recorded', unitId: U1, action: 'x' }] }),
      ev(2, { kind: 'phase_started', phase: 'rounds' }),
      ev(3, { kind: 'turn_started', teamId: T1 }),
      ev(4, { kind: 'turn_ended', teamId: T1, passed: true, changes: [{ kind: 'action_record_cleared', unitId: U1 }] }),
    ]);
    expect(a.projection.scheduler).toMatchObject({ round: 1, phase: 'rounds', activeTeamId: undefined });
    expect([...a.projection.scheduler.passed]).toEqual([T1]);
    expect([...a.projection.units.get(U1)!.actions]).toEqual([]);
    a.apply(ev(5, { kind: 'round_started', round: 2, order: [T2, T1], changes: [] }));
    expect([...a.projection.scheduler.passed]).toEqual([]);
  });

  it('opens, answers and closes an Ask', () => {
    const a = new Applier(board, game);
    const ask = { id: A1, question: 'activate', prompt: 'Activate', teams: [T1], legal: { kind: 'options', choices: [{ id: 'x', label: 'X', pieces: [] }] }, allowPass: true, sealed: false };
    a.applyAll([ev(1, { kind: 'ask_opened', ask }), ev(2, { kind: 'ask_answered', askId: A1, teamId: T1, outsideLegalSet: false })]);
    expect([...a.projection.openAsks.get(A1)!.answers.keys()]).toEqual([T1]);
    a.apply(ev(3, { kind: 'ask_closed', askId: A1, answers: [] }));
    expect(a.projection.openAsks.size).toBe(0);
  });

  it('treats a reroll, an edit and a modifier as changing nothing a frame records', () => {
    const a = new Applier(board, game);
    a.applyAll([
      ev(1, { kind: 'sequence_started', frameId: F1, sequence: 'attack-sequence', context: { actorUnitId: U1, targetUnitIds: [], weaponIds: [] } }),
      ev(2, { kind: 'step_completed', frameId: F1, step: 'roll-to-hit', rolls: [], changes: [{ kind: 'wounds_set', modelId: M1, wounds: 1 }] }),
      ev(3, { kind: 'modifier_added', frameId: F1, roll: 'hit', modifier: { source: 'Clay', amount: 1 } }),
      ev(4, { kind: 'dice_rerolled', frameId: F1, rollId: R1, replaced: [] }),
      ev(5, { kind: 'dice_edited', frameId: F1, rollId: R1, replaced: [] }),
      ev(6, { kind: 'sequence_finished', frameId: F1 }),
    ]);
    expect(a.projection.models.get(M1)!.wounds).toBe(1);
    expect(a.projection.lastSeq).toBe(6);
  });

  it('drops an undone range by rebuilding, and keeps the log append-only', () => {
    const a = new Applier(board, game);
    a.applyAll([
      ev(1, { kind: 'score_set', teamId: T1, score: 1 }),
      ev(2, { kind: 'score_set', teamId: T1, score: 2 }),
      ev(3, { kind: 'score_set', teamId: T1, score: 3 }),
      ev(4, { kind: 'undone', toSeq: 1 }),
      ev(5, { kind: 'score_set', teamId: T2, score: 9 }),
    ]);
    expect(a.projection.scores.get(T1)).toBe(1);
    expect(a.projection.scores.get(T2)).toBe(9);
    expect(a.log).toHaveLength(5);
    expect(effectiveLog(a.log).map((e) => e.seq)).toEqual([1, 5]);
  });

  it('throws ProjectionDesyncError on an id it does not hold, and leaves the store untouched', () => {
    const a = new Applier(board, game);
    // The canonical desync: a marker placed during setup, then moved. `{ ...undefined }` would
    // have written a marker with no id and carried on, so this is the test that the port faults instead.
    const move = ev(1, { kind: 'marker_moved', markerId: K9, position: { x: 1, y: 1, z: 0 }, yaw: { degrees: 0 } });
    expect(() => a.apply(move)).toThrow(ProjectionDesyncError);
    try { a.apply(move); } catch (error) {
      expect(error).toMatchObject({ kind: 'marker_moved', unknownId: K9 });   // C#'s Kind and UnknownId
    }
    expect(a.projection.markers.size).toBe(0);

    // Every read-through-a-lookup site faults the same way, whether the id is a model, a unit or an Ask.
    expect(() => new Applier(board, game).apply(ev(1, { kind: 'wounds_set', modelId: K9, wounds: 1 })))
      .toThrow(ProjectionDesyncError);
    expect(() => new Applier(board, game).apply(ev(1, { kind: 'action_recorded', unitId: K9, action: 'advance' })))
      .toThrow(ProjectionDesyncError);
    expect(() => new Applier(board, game).apply(ev(1, { kind: 'ask_answered', askId: K9, teamId: T1, answer: { kind: 'confirmed' }, outsideLegalSet: false })))
      .toThrow(ProjectionDesyncError);

    // A store the C# writes with an indexer or Remove cannot desync, so neither side faults.
    expect(() => new Applier(board, game).apply(ev(1, { kind: 'marker_removed', markerId: K9 }))).not.toThrow();
    expect(() => new Applier(board, game).apply(ev(1, { kind: 'ask_closed', askId: K9 }))).not.toThrow();
  });
});

describe('the ported log lines', () => {
  it('names teams by ordinal, units and models by name, and every pool of a roll by its threshold and modified faces', () => {
    const a = new Applier(board, game);
    const line = (e: GameEvent) => { a.apply(e); return lineOf(e, a.projection, ordinals); };
    expect(line(ev(1, { kind: 'round_started', round: 1, order: [T1], changes: [] }))).toBe('Round 1 started');
    expect(line(ev(2, { kind: 'turn_started', teamId: T1 }))).toBe("Team 1's turn");
    expect(line(ev(3, { kind: 'turn_ended', teamId: T1, passed: true, changes: [] }))).toBe('Team 1 passed');
    expect(line(ev(4, { kind: 'phase_started', phase: 'rounds' }))).toBe('Phase: rounds');
    expect(line(ev(5, { kind: 'sequence_started', frameId: F1, sequence: 'attack-sequence', context: { actorUnitId: U1, targetUnitIds: [], weaponIds: [] } })))
      .toBe('attack-sequence started for Rifles');
    expect(line(ev(6, { kind: 'step_completed', frameId: F1, step: 'roll-to-hit', rolls: [
      { id: R1, roll: 'hit', threshold: 3, modifiers: [], dice: [{ raw: 4, modified: 4, success: true }] },
      { id: R2, roll: 'hit', threshold: 5, modifiers: [], dice: [{ raw: 6, modified: 6, success: true }, { raw: 2, modified: 3, success: false }] },
    ], changes: [] }))).toBe('roll-to-hit: hit 3+ 4!; hit 5+ 6!,3');
    expect(line(ev(7, { kind: 'wounds_set', modelId: M1, wounds: 2 }))).toBe('Rifleman at 2 wound(s)');
    expect(line(ev(8, { kind: 'action_recorded', unitId: U1, action: 'Advance' }))).toBe('Rifles did Advance');
    expect(line(ev(9, { kind: 'modifier_added', frameId: F1, roll: 'hit', modifier: { source: 'Clay', amount: -1 } })))
      .toBe('Clay gives -1 to hit');
    expect(line(ev(10, { kind: 'score_set', teamId: T2, score: 3 }))).toBe('Team 2 scores 3');
    expect(line(ev(11, { kind: 'undone', toSeq: 3 }))).toBe('undone to 3');
  });

  it('falls back to a short id for a team, unit or model it cannot name', () => {
    const a = new Applier(board, game);
    const e = ev(1, { kind: 'score_set', teamId: '0198b7ac-9999-7000-8000-000000000009', score: 1 });
    expect(() => a.apply(e)).not.toThrow();
    expect(lineOf(e, a.projection, new Map())).toBe('0198b7ac scores 1');
  });
});

describe('the ported board projection', () => {
  it('splits the flags wire form into the recording order', () => {
    expect(declarationsOf('impassable, blocks_line_of_sight')).toEqual(['impassable', 'blocks_line_of_sight']);
    expect(declarationsOf('cover, impassable')).toEqual(['impassable', 'cover']);
    expect(declarationsOf('none')).toEqual([]);
    expect(declarationsOf('')).toEqual([]);
  });

  it('flattens the wire board into a recorded board', () => {
    const b = new FrameProjector(board, game).board;
    expect(b).toMatchObject({ width: 72, length: 48, texture: 'grass' });
    expect(b.terrain[0]).toMatchObject({ mesh: 'ruin', x: 20, y: 10, z: 0, yaw: 90, scale: 2 });
    expect(b.terrain[0]!.volumes[0]).toMatchObject({ kind: 'solid', footprint: 'rect', a: 3, b: 1, offsetX: 1, yaw: 0, top: 4, declarations: ['impassable', 'blocks_line_of_sight'] });
    expect(b.markers[0]).toEqual({ id: K1, kind: 'objective', diameter: 2, label: 'A' });
    // No ZoneStyles in the browser, so the live feed's zones carry no rings; normaliseBoard drops them.
    expect(b.zones[0]).toMatchObject({ shape: 'frontline/1', teamOrdinal: 1, polygons: [] });
    expect(b.models[0]).toEqual({ id: M1, unitId: U1, unitName: 'Rifles', name: 'Rifleman', ordinal: 1, teamId: T1, teamOrdinal: 1, baseShape: 'circle', baseA: 1, baseB: 1, height: 1.2 });
  });

  it('records one whole-state frame per event, indexed from zero', () => {
    const projector = new FrameProjector(board, game);
    const first = projector.add(ev(1, { kind: 'phase_started', phase: 'deployment' }));
    expect(first).toMatchObject({ index: 0, seq: 1, kind: 'phase_started', phase: 'deployment', round: 0, line: 'Phase: deployment' });
    expect(first.models).toHaveLength(1);
    expect(first.models[0]).toEqual({ id: M1, where: 'shelf', wounds: 0, spellTokens: 0, conditions: [] });
    expect(first.units[0]).toEqual({ id: U1, actions: [], conditions: [], grants: [], woundsThisActivation: 0 });
    const second = projector.add(ev(2, { kind: 'step_completed', frameId: F1, step: 's', rolls: [
      { id: R1, roll: 'hit', threshold: 4, modifiers: [{ source: 'AP', amount: -1 }], dice: [{ raw: 3, modified: 4, success: true }, { raw: 1, modified: 2, success: false }] },
    ], changes: [] }));
    expect(second.index).toBe(1);
    expect(second.rolls[0]).toEqual({ id: R1, roll: 'hit', threshold: 4, net: 1, raw: [3, 1], modified: [4, 2], success: [true, false], modifiers: [{ source: 'AP', amount: -1 }] });
    expect(projector.frames).toHaveLength(2);
  });

  it('carries the score, the passed teams and a marker dropped during play, all as whole state', () => {
    const projector = new FrameProjector(board, game);
    const opening = projector.add(ev(1, { kind: 'round_started', round: 1, order: [T1, T2], changes: [] }));
    expect(opening.scores).toEqual({ [T1]: 0, [T2]: 0 });   // canonical Guid keys, no key policy set
    expect(opening.passed).toEqual([]);
    expect(opening.markers).toEqual([]);                    // the board's setup markers are not frame state

    projector.add(ev(2, { kind: 'score_set', teamId: T2, score: 4 }));
    const dropped = projector.add(ev(3, { kind: 'step_completed', frameId: F1, step: 'drop', rolls: [], changes: [
      { kind: 'marker_placed', marker: { id: K9, kind: 'token', position: { x: 4, y: 4, z: 0 },
        yaw: { degrees: 0 }, diameter: 1, visual: { builtin: 'chip' } } },
    ] }));
    // The definition rides along per frame, so the viewer needs no join against the board block.
    expect(dropped.markers).toEqual([{ id: K9, kind: 'token', diameter: 1, x: 4, y: 4, z: 0, yaw: 0 }]);
    expect(dropped.scores).toEqual({ [T1]: 0, [T2]: 4 });

    const passed = projector.add(ev(4, { kind: 'turn_ended', teamId: T1, passed: true, changes: [] }));
    expect(passed.passed).toEqual([T1]);
    expect(passed.scores).toEqual({ [T1]: 0, [T2]: 4 });    // whole state, not a delta
  });
});
```

- [ ] **Step 2: Run; expect the imports to fail**

Run: `cd viewer && npx vitest run tests/apply.test.ts`
Expected: `Failed to resolve import "../src/lib/live/apply"`.

---
- [ ] **Step 3: Write `apply.ts`**

```ts
// viewer/src/lib/live/apply.ts
// A port of Tabletop.Protocol.EventApplier (contracts plan Task 14), branch for branch.
// It copies. It never decides legality, never derives a location, never clears a store on its own.
import type {
  Board, GameEvent, GameInfo, Grant, Location, Marker, ModelSlot, StateChange, Unit,
} from '../protocol/wire.generated';
import type { Ask, AskAnswer } from '../protocol/wire.generated';

export type UnitInPlay = {
  readonly id: string; readonly participantId: string; readonly definition: Unit;
  readonly actions: Set<string>; readonly conditions: Set<string>;
  grants: Grant[]; woundsThisActivation: number;
};
export type ModelInPlay = {
  readonly id: string; readonly unitId: string; readonly slot: ModelSlot;
  location: Location; wounds: number; readonly conditions: Set<string>; spellTokens: number;
};
export type SchedulerPosition = {
  round: number; order: string[]; activeTeamId: string | undefined;
  phase: string | undefined; readonly passed: Set<string>;
};
export type OpenAsk = { readonly ask: Ask; readonly answers: Map<string, AskAnswer | undefined> };

/**
 * The port of `Tabletop.Protocol.ProjectionDesyncException`: an event named
 * an id this projection does not hold, so this copy of the game has diverged from the server's. Thrown at
 * exactly the sites where the C# applier indexes a dictionary, and carrying the same two facts.
 */
export class ProjectionDesyncError extends Error {
  constructor(readonly kind: string, readonly unknownId: string) {
    super(`${kind} names ${unknownId}, which this projection does not hold`);
    this.name = 'ProjectionDesyncError';
  }
}

export type Projection = {
  readonly board: Board;
  readonly units: Map<string, UnitInPlay>;
  readonly models: Map<string, ModelInPlay>;
  readonly markers: Map<string, Marker>;
  readonly scores: Map<string, number>;
  readonly scheduler: SchedulerPosition;
  readonly openAsks: Map<string, OpenAsk>;
  lastSeq: number;
};

export function initialProjection(board: Board, game: GameInfo): Projection {
  const p: Projection = {
    board,
    units: new Map(), models: new Map(), markers: new Map(), scores: new Map(),
    scheduler: { round: 0, order: [], activeTeamId: undefined, phase: undefined, passed: new Set() },
    openAsks: new Map(), lastSeq: 0,
  };
  for (const team of game.teams) p.scores.set(team.id, 0);
  // Insertion order matters: it is the order every frame lists models and units in, and C#'s
  // Projection.Initial walks armies, then units, then model slots in exactly this order.
  for (const army of board.armies)
    for (const unit of army.snapshot.units) {
      p.units.set(unit.id, {
        id: unit.id, participantId: army.participantId, definition: unit,
        actions: new Set(), conditions: new Set(), grants: [], woundsThisActivation: 0,
      });
      for (const slot of unit.models)
        p.models.set(slot.id, {
          id: slot.id, unitId: unit.id, slot, location: { kind: 'shelf' },
          wounds: 0, conditions: new Set(), spellTokens: 0,
        });
    }
  return p;
}

/** The four lookups that can miss. Each is a place the C# applier indexes a dictionary. */
const unitIn = (p: Projection, kind: string, id: string): UnitInPlay => {
  const unit = p.units.get(id);
  if (unit === undefined) throw new ProjectionDesyncError(kind, id);
  return unit;
};
const modelIn = (p: Projection, kind: string, id: string): ModelInPlay => {
  const model = p.models.get(id);
  if (model === undefined) throw new ProjectionDesyncError(kind, id);
  return model;
};
const markerIn = (p: Projection, kind: string, id: string): Marker => {
  const marker = p.markers.get(id);
  if (marker === undefined) throw new ProjectionDesyncError(kind, id);
  return marker;
};
const askIn = (p: Projection, kind: string, id: string): OpenAsk => {
  const ask = p.openAsks.get(id);
  if (ask === undefined) throw new ProjectionDesyncError(kind, id);
  return ask;
};

export function applyChange(p: Projection, change: StateChange): void {
  switch (change.kind) {
    case 'models_moved': for (const move of change.moves) modelIn(p, change.kind, move.modelId).location = move.to; return;
    case 'wounds_set': modelIn(p, change.kind, change.modelId).wounds = change.wounds; return;
    case 'condition_set': {
      const set = change.subject.kind === 'unit'
        ? unitIn(p, change.kind, change.subject.id).conditions
        : modelIn(p, change.kind, change.subject.id).conditions;
      if (change.present) set.add(change.condition); else set.delete(change.condition);
      return;
    }
    case 'action_recorded': unitIn(p, change.kind, change.unitId).actions.add(change.action); return;
    case 'action_record_cleared': unitIn(p, change.kind, change.unitId).actions.clear(); return;
    case 'grant_added': unitIn(p, change.kind, change.unitId).grants.push(change.grant); return;
    case 'grant_removed': {
      const unit = unitIn(p, change.kind, change.unitId);
      unit.grants = unit.grants.filter((g) => g.id !== change.grantId);
      return;
    }
    case 'spell_tokens_set': modelIn(p, change.kind, change.modelId).spellTokens = change.tokens; return;
    case 'wounds_this_activation_set': unitIn(p, change.kind, change.unitId).woundsThisActivation = change.wounds; return;
    // A store that C# writes with an indexer or Remove rather than reading through one: score_set,
    // marker_placed and marker_removed cannot desync, and neither side faults on them.
    case 'score_set': p.scores.set(change.teamId, change.score); return;
    case 'marker_placed': p.markers.set(change.marker.id, change.marker); return;
    case 'marker_moved': {
      const marker = markerIn(p, change.kind, change.markerId);
      p.markers.set(change.markerId, { ...marker, position: change.position, yaw: change.yaw });
      return;
    }
    case 'marker_removed': p.markers.delete(change.markerId); return;
    default: {
      // A kind added to the generated union fails `npm run check` here.
      const unreachable: never = change;
      throw new Error(`Unknown state change ${(unreachable as { kind: string }).kind}`);
    }
  }
}

// A Record, not a list: a kind added to the generated StateChange union is a missing key and fails the check.
const STATE_CHANGE_KINDS: Record<StateChange['kind'], true> = {
  models_moved: true, wounds_set: true, condition_set: true, action_recorded: true, action_record_cleared: true,
  grant_added: true, grant_removed: true, spell_tokens_set: true, wounds_this_activation_set: true,
  score_set: true, marker_placed: true, marker_moved: true, marker_removed: true,
};
const isStateChange = (payload: { kind: string }): payload is StateChange =>
  Object.hasOwn(STATE_CHANGE_KINDS, payload.kind);

export function applyTo(p: Projection, event: GameEvent): void {
  const payload = event.payload;
  if (isStateChange(payload)) applyChange(p, payload);
  else switch (payload.kind) {
    case 'round_started':
      p.scheduler.round = payload.round;
      p.scheduler.order = [...payload.order];
      p.scheduler.activeTeamId = undefined;
      p.scheduler.passed.clear();
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'turn_started': p.scheduler.activeTeamId = payload.teamId; break;
    case 'turn_ended':
      if (payload.passed) p.scheduler.passed.add(payload.teamId);
      if (p.scheduler.activeTeamId === payload.teamId) p.scheduler.activeTeamId = undefined;
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'phase_started': p.scheduler.phase = payload.phase; break;
    case 'step_completed': for (const change of payload.changes) applyChange(p, change); break;
    case 'ask_opened': p.openAsks.set(payload.ask.id, { ask: payload.ask, answers: new Map() }); break;
    case 'ask_answered': askIn(p, payload.kind, payload.askId).answers.set(payload.teamId, payload.answer); break;
    case 'ask_closed': p.openAsks.delete(payload.askId); break;
    // The frame stack is not ported: RecordedFrame carries no stack, and its rolls come from the
    // step_completed payload, so these six change nothing any field of a frame records.
    // Unported stack-site faults: Pop on sequence_finished/sequence_abandoned, FrameOf,
    // ReplaceDice's roll and die-index lookups.
    case 'sequence_started': case 'sequence_finished': case 'sequence_abandoned':
    case 'dice_rerolled': case 'dice_edited': case 'modifier_added': break;
    case 'undone': throw new Error('undone is applied by rebuilding; use Applier.apply');
    default: {
      const unreachable: never = payload;
      throw new Error(`Unknown payload ${(unreachable as { kind: string }).kind}`);
    }
  }
  p.lastSeq = event.seq;
}

/** The log with every undone range removed. Undone markers never appear in the result. */
export function effectiveLog(log: readonly GameEvent[]): readonly GameEvent[] {
  const live: GameEvent[] = [];
  for (const event of log) {
    if (event.payload.kind === 'undone') {
      const to = event.payload.toSeq;
      for (let i = live.length - 1; i >= 0; i--) if (live[i]!.seq > to) live.splice(i, 1);
    } else live.push(event);
  }
  return live;
}

export class Applier {
  readonly #board: Board;
  readonly #game: GameInfo;
  readonly #log: GameEvent[] = [];
  #projection: Projection;

  constructor(board: Board, game: GameInfo) {
    this.#board = board;
    this.#game = game;
    this.#projection = initialProjection(board, game);
  }

  get log(): readonly GameEvent[] { return this.#log; }
  get projection(): Projection { return this.#projection; }

  apply(event: GameEvent): void {
    this.#log.push(event);
    if (event.payload.kind === 'undone') {
      const rebuilt = initialProjection(this.#board, this.#game);
      for (const live of effectiveLog(this.#log)) applyTo(rebuilt, live);
      rebuilt.lastSeq = event.seq;
      this.#projection = rebuilt;
    } else applyTo(this.#projection, event);
  }

  applyAll(events: Iterable<GameEvent>): void { for (const event of events) this.apply(event); }
}
```

- [ ] **Step 4: Write `logLines.ts`**

```ts
// viewer/src/lib/live/logLines.ts
// A port of Tabletop.Sim.Recording.LogLines (simulation plan Task 5), case for case. Rules-free by
// construction: every name it prints is a string the event already carries.
import type { AskAnswer, GameEvent, RollRecord, Subject } from '../protocol/wire.generated';
import type { Projection } from './apply';

export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(0, 8);
}

const team = (ordinals: ReadonlyMap<string, number>, id: string): string =>
  ordinals.has(id) ? `Team ${ordinals.get(id)}` : shortId(id);
const unit = (p: Projection, id: string): string => p.units.get(id)?.definition.name ?? shortId(id);
const model = (p: Projection, id: string): string => p.models.get(id)?.slot.name ?? shortId(id);
const subject = (p: Projection, s: Subject): string => (s.kind === 'unit' ? unit(p, s.id) : model(p, s.id));
const actor = (p: Projection, id: string | undefined): string => (id === undefined ? '' : ` for ${unit(p, id)}`);
/** C#'s "+#;-#;0" custom numeric format. */
const signed = (n: number): string => (n > 0 ? `+${n}` : n < 0 ? `${n}` : '0');

// Every roll on the step, with its threshold: a ruleset splits one roll wherever a step's dice do not share one, and OPR's
// roll-to-hit takes one pool per Quality value the attacking models bring (p.4, and a joined hero brings its own). The
// threshold is the only thing that tells two pools of the same roll apart.
const dice = (rolls: readonly RollRecord[]): string =>
  rolls.length === 0
    ? ''
    : `: ${rolls
        .map(
          (r) =>
            `${r.roll}${r.threshold === undefined ? '' : ` ${r.threshold}+`} ` +
            `${r.dice.map((d) => (d.success ? `${d.modified}!` : `${d.modified}`)).join(',')}`,
        )
        .join('; ')}`;

function answerOf(answer: AskAnswer | undefined): string {
  if (answer === undefined) return 'nothing';
  switch (answer.kind) {
    case 'chosen': return answer.optionId;
    case 'selected_pieces': return `${answer.pieceIds.length} piece(s)`;
    case 'allocated': return `${Object.values(answer.counts).reduce((a, b) => a + b, 0)} allocated`;
    case 'assigned': return `${Object.keys(answer.targets).length} assignment(s)`;
    case 'destinations': return `${answer.moves.length} destination(s)`;
    case 'tokens_spent': return `${Object.values(answer.counts).reduce((a, b) => a + b, 0)} token(s) spent`;
    case 'confirmed': return 'yes';
    case 'passed': return 'a pass';
    default: { const unreachable: never = answer; return (unreachable as { kind: string }).kind; }
  }
}

export function lineOf(event: GameEvent, after: Projection, ordinals: ReadonlyMap<string, number>): string {
  const e = event.payload;
  switch (e.kind) {
    case 'round_started': return `Round ${e.round} started`;
    case 'turn_started': return `${team(ordinals, e.teamId)}'s turn`;
    case 'turn_ended': return e.passed ? `${team(ordinals, e.teamId)} passed` : `${team(ordinals, e.teamId)}'s turn ended`;
    case 'phase_started': return `Phase: ${e.phase}`;
    case 'sequence_started': return `${e.sequence} started${actor(after, e.context.actorUnitId)}`;
    case 'step_completed': return `${e.step}${dice(e.rolls)}`;
    case 'sequence_finished': return 'sequence finished';
    case 'sequence_abandoned': return 'sequence abandoned';
    case 'ask_opened': return `Asked ${e.ask.teams.map((t) => team(ordinals, t)).join(', ')}: ${e.ask.prompt}`;
    case 'ask_answered':
      return `${team(ordinals, e.teamId)} answered ${answerOf(e.answer)}${e.outsideLegalSet ? ' (outside the legal set)' : ''}`;
    case 'ask_closed': return 'ask closed';
    case 'dice_rerolled': return `rerolled ${e.replaced.length} dice`;
    case 'dice_edited': return `edited ${e.replaced.length} dice`;
    case 'modifier_added': return `${e.modifier.source} gives ${signed(e.modifier.amount)} to ${e.roll}`;
    case 'undone': return `undone to ${e.toSeq}`;
    case 'models_moved': return `${e.moves.length} model(s) moved`;
    case 'wounds_set': return `${model(after, e.modelId)} at ${e.wounds} wound(s)`;
    case 'condition_set': return `${subject(after, e.subject)} ${e.present ? 'gains' : 'loses'} ${e.condition}`;
    case 'action_recorded': return `${unit(after, e.unitId)} did ${e.action}`;
    case 'action_record_cleared': return `${unit(after, e.unitId)}'s action record cleared`;
    case 'grant_added': return `${unit(after, e.unitId)} gains ${e.grant.entryName}`;
    case 'grant_removed': return 'a grant was removed';
    case 'spell_tokens_set': return `${model(after, e.modelId)} holds ${e.tokens} token(s)`;
    case 'wounds_this_activation_set': return `${unit(after, e.unitId)} has taken ${e.wounds} wound(s) this activation`;
    case 'score_set': return `${team(ordinals, e.teamId)} scores ${e.score}`;
    case 'marker_placed': return `marker placed${e.marker.label === undefined ? '' : `: ${e.marker.label}`}`;
    case 'marker_moved': return 'marker moved';
    case 'marker_removed': return 'marker removed';
    default: { const unreachable: never = e; return (unreachable as { kind: string }).kind; }
  }
}
```

- [ ] **Step 5: Write `project.ts`**

```ts
// viewer/src/lib/live/project.ts
// A port of Tabletop.Sim.Recording.FrameProjector (simulation plan Task 5): applies the events the socket
// delivered and writes down what the table looked like afterwards, in the recording's own shape.
import { DECLARATIONS } from '../board/palette';
import type {
  Ask, Board, GameEvent, GameInfo, ModelSlot, RollRecord, TerrainPlacement, VisualRef, Volume,
} from '../protocol/wire.generated';
import type {
  RecordedAsk, RecordedAskOption, RecordedBoard, RecordedFrame, RecordedModel, RecordedModelState,
  RecordedRoll, RecordedTerrain, RecordedUnitState, RecordedVolume,
} from '../protocol/recording.generated';
import { Applier, type ModelInPlay, type OpenAsk, type Projection, type UnitInPlay } from './apply';
import { lineOf } from './logLines';

/** The [Flags] wire form ("none", or "cover, impassable") in the canonical declaration order C# emits. */
export function declarationsOf(wire: string): readonly string[] {
  const present = new Set(wire.split(',').map((s) => s.trim()).filter((s) => s.length > 0 && s !== 'none'));
  return DECLARATIONS.filter((d) => present.has(d));
}

const visualName = (visual: VisualRef): string => visual.builtin ?? visual.assetId!;

function volumeOf(id: string, v: Volume): RecordedVolume {
  return {
    id, kind: v.kind, footprint: v.footprint.kind, a: v.footprint.a, b: v.footprint.b,
    offsetX: v.offset.x, offsetY: v.offset.y, yaw: v.yaw.degrees, bottom: v.bottom, top: v.top,
    declarations: declarationsOf(v.declarations),
    ...(v.notes === undefined ? {} : { notes: v.notes }),
  };
}

function terrainOf(piece: TerrainPlacement): RecordedTerrain {
  return {
    id: piece.id, mesh: visualName(piece.mesh), x: piece.position.x, y: piece.position.y, z: piece.position.z,
    yaw: piece.yaw.degrees, scale: piece.scale, volumes: piece.volumes.map((v) => volumeOf(v.id, v.volume)),
  };
}

export function recordedBoardOf(board: Board, game: GameInfo): RecordedBoard {
  const table = board.table;
  const teamOf = new Map(game.participants.filter((p) => p.teamId !== undefined).map((p) => [p.id, p.teamId!]));
  const ordinalOf = new Map(game.teams.map((t) => [t.id, t.ordinal]));
  const models: RecordedModel[] = board.armies.flatMap((army) =>
    army.snapshot.units.flatMap((unit) =>
      unit.models.map((slot: ModelSlot) => {
        const teamId = teamOf.get(army.participantId)!;
        return {
          id: slot.id, unitId: unit.id, unitName: unit.name, name: slot.name, ordinal: slot.ordinal,
          teamId, teamOrdinal: ordinalOf.get(teamId)!,
          baseShape: slot.base.shape, baseA: slot.base.a, baseB: slot.base.b, height: slot.height,
        };
      }),
    ),
  );
  return {
    width: table.size.width, length: table.size.length, texture: visualName(table.texture),
    terrain: table.terrain.map(terrainOf),
    markers: table.markers.map((m) => ({
      id: m.id, kind: m.kind, diameter: m.diameter, ...(m.label === undefined ? {} : { label: m.label }),
    })),
    // The live feed resolves no zone: ZoneStyles is C# and is not ported (Task 4). An empty ring list
    // is what an unresolvable shape id produces C#-side too, and draws no outline rather than a wrong
    // one. Task 10's normaliseBoard drops `polygons` before comparing, and says why.
    zones: table.zones.map((z) => ({ id: z.id, shape: z.shape, teamOrdinal: z.teamOrdinal, polygons: [] })),
    models,
  };
}

const modelState = (m: ModelInPlay): RecordedModelState => {
  const common = { id: m.id, wounds: m.wounds, spellTokens: m.spellTokens, conditions: [...m.conditions].sort() };
  if (m.location.kind === 'table') {
    const { position, yaw } = m.location.placement;
    return { ...common, where: 'table', x: position.x, y: position.y, z: position.z, yaw: yaw.degrees };
  }
  if (m.location.kind === 'inside')
    return { ...common, where: 'inside', containerId: m.location.containerId };
  return { ...common, where: 'shelf' };
};

const unitState = (u: UnitInPlay): RecordedUnitState => ({
  id: u.id, actions: [...u.actions].sort(), conditions: [...u.conditions].sort(),
  grants: u.grants.map((g) => g.entryName), woundsThisActivation: u.woundsThisActivation,
});

const optionOf = (o: { id: string; label: string; pieces: readonly string[] }): RecordedAskOption =>
  ({ id: o.id, label: o.label, pieces: o.pieces });

function askOf(open: OpenAsk): RecordedAsk {
  const ask: Ask = open.ask;
  const legal = ask.legal;
  let kind = legal.kind as string;
  let options: readonly RecordedAskOption[] = [];
  let pieces: readonly string[] = [];
  let allowance: number | undefined;
  switch (legal.kind) {
    case 'options': options = legal.choices.map(optionOf); break;
    case 'pieces': pieces = legal.eligible; break;
    case 'allocation': pieces = legal.targets; break;
    case 'assignment': options = legal.items.map(optionOf); pieces = legal.targets; break;
    case 'move_region': pieces = legal.modelIds; allowance = legal.allowance; break;
    // One maximum per eligible model (p.13): the models go in `pieces` and their sum in `allowance`, which is what
    // `FrameProjector.AskOf` records on the C# side. The per-model figures are not carried; each frame already has
    // every model's `spellTokens`.
    case 'token_spend':
      pieces = Object.keys(legal.maxByModel);
      allowance = Object.values(legal.maxByModel).reduce((a, b) => a + b, 0);
      break;
    case 'confirmation': break;
    default: { const unreachable: never = legal; kind = (unreachable as { kind: string }).kind; }
  }
  return {
    id: ask.id, question: ask.question, prompt: ask.prompt, teams: ask.teams,
    allowPass: ask.allowPass, sealed: ask.sealed,
    ...(ask.windowKind === undefined ? {} : { windowKind: ask.windowKind }),
    ...(ask.frameId === undefined ? {} : { frameId: ask.frameId }),
    legalKind: kind, answered: [...open.answers.keys()], options, pieces,
    ...(allowance === undefined ? {} : { allowance }),
  };
}

const rollOf = (r: RollRecord): RecordedRoll => ({
  id: r.id, roll: r.roll, ...(r.threshold === undefined ? {} : { threshold: r.threshold }),
  net: r.dice.length === 0 ? 0 : r.dice[0]!.modified - r.dice[0]!.raw,
  raw: r.dice.map((d) => d.raw), modified: r.dice.map((d) => d.modified), success: r.dice.map((d) => d.success),
  modifiers: r.modifiers.map((m) => ({ source: m.source, amount: m.amount })),
});

export class FrameProjector {
  readonly #applier: Applier;
  readonly #ordinals: ReadonlyMap<string, number>;
  readonly #frames: RecordedFrame[] = [];
  readonly board: RecordedBoard;

  constructor(board: Board, game: GameInfo) {
    this.#applier = new Applier(board, game);
    this.#ordinals = new Map(game.teams.map((t) => [t.id, t.ordinal]));
    this.board = recordedBoardOf(board, game);
  }

  get frames(): readonly RecordedFrame[] { return this.#frames; }
  get projection(): Projection { return this.#applier.projection; }

  add(event: GameEvent): RecordedFrame {
    this.#applier.apply(event);
    const p = this.#applier.projection;
    const frame = {
      index: this.#frames.length, seq: event.seq, kind: event.payload.kind,
      ...(event.actor === undefined ? {} : { actor: event.actor }),
      ...(event.warning === undefined ? {} : { warning: event.warning }),
      round: p.scheduler.round,
      ...(p.scheduler.phase === undefined ? {} : { phase: p.scheduler.phase }),
      ...(p.scheduler.activeTeamId === undefined ? {} : { activeTeamId: p.scheduler.activeTeamId }),
      line: lineOf(event, p, this.#ordinals),
      models: [...p.models.values()].map(modelState),
      units: [...p.units.values()].map(unitState),
      markers: [...p.markers.values()].map((m) => ({
        id: m.id, kind: m.kind, diameter: m.diameter, ...(m.label === undefined ? {} : { label: m.label }),
        x: m.position.x, y: m.position.y, z: m.position.z, yaw: m.yaw.degrees,
      })),
      openAsks: [...p.openAsks.values()].map(askOf),
      rolls: event.payload.kind === 'step_completed' ? event.payload.rolls.map(rollOf) : [],
      // Copied, not aliased: every frame is kept for the whole run and the applier keeps mutating both.
      scores: Object.fromEntries(p.scores),
      passed: [...p.scheduler.passed],
    };
    this.#frames.push(frame);
    return frame;
  }
}
```

`[...set].sort()` is JavaScript's code-unit order and C#'s `.Order()` is `Comparer<string>.Default`, which is culture-sensitive; the two agree for the ASCII identifiers conditions and actions actually use, and Task 10's comparison sorts both sides anyway, so a difference cannot produce a false failure. The same holds for `Map` insertion order versus `Dictionary` enumeration order after a marker or Ask is removed and another added.

- [ ] **Step 6: Run the tests; expect 13 more passed**

Run: `cd viewer && npm test && npm run check`

Thirteen: the desync test is the thirteenth.

- [ ] **Step 7: Commit**

```bash
git add viewer/src/lib/live viewer/tests/apply.test.ts
git commit -m "Port the event applier, the frame projector and the log lines to TypeScript"
```
