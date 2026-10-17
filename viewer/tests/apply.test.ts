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
      // A marker placed in play, then moved. A setup marker's first move is pinned in its own test.
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

  it('treats a reroll and an edit as changing nothing a frame records', () => {
    const a = new Applier(board, game);
    a.applyAll([
      ev(1, { kind: 'sequence_started', frameId: F1, sequence: 'attack-sequence', context: { actorUnitId: U1, targetUnitIds: [], weaponIds: [] } }),
      ev(2, { kind: 'step_completed', frameId: F1, step: 'roll-to-hit', rolls: [], changes: [{ kind: 'wounds_set', modelId: M1, wounds: 1 }] }),
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
    // The canonical desync: moving a marker neither the board nor the log holds. `{ ...undefined }` would
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
    expect(line(ev(10, { kind: 'score_set', teamId: T2, score: 3 }))).toBe('Team 2 scores 3');
    expect(line(ev(11, { kind: 'undone', toSeq: 3 }))).toBe('undone to 3');
  });

  it('labels an answer that broke a rule, and one outside the legal set, as the recording does', () => {
    const p = initialProjection(board, game);
    const answered = (extra: { outsideLegalSet: boolean; breach?: string }) =>
      lineOf(ev(1, { kind: 'ask_answered', askId: A1, teamId: T1, answer: { kind: 'confirmed' }, ...extra }), p, ordinals);
    expect(answered({ outsideLegalSet: false, breach: 'move_overlap' })).toBe('Team 1 answered yes (breaks move_overlap)');
    expect(answered({ outsideLegalSet: true })).toBe('Team 1 answered yes (outside the legal set)');
    expect(answered({ outsideLegalSet: false })).toBe('Team 1 answered yes');
  });

  it('falls back to a short id for a team, unit or model it cannot name', () => {
    const a = new Applier(board, game);
    const e = ev(1, { kind: 'score_set', teamId: '0198b7ac-9999-7000-8000-000000000009', score: 1 });
    expect(() => a.apply(e)).not.toThrow();
    expect(lineOf(e, a.projection, new Map())).toBe('0198b7ac scores 1');
  });
});

describe('game_ended', () => {
  it('sets ended, and a round start clears it', () => {
    const a = new Applier(board, game);
    a.apply(ev(1, { kind: 'game_ended', reason: 'over', winners: [T1] }));
    expect(a.projection.ended).toEqual({ kind: 'game_ended', reason: 'over', winners: [T1] });
    a.apply(ev(2, { kind: 'round_started', round: 5, order: [T1, T2], changes: [] }));
    expect(a.projection.ended).toBeUndefined();
  });

  it('reads as the recording reads it', () => {
    const p = initialProjection(board, game);
    const ordinals = new Map([[T1, 1], [T2, 2]]);
    const line = (winners: string[]) => lineOf(ev(9, { kind: 'game_ended', reason: 'over', winners }), p, ordinals);
    expect(line([T1])).toBe('Game ended: over; Team 1 won');
    expect(line([T1, T2])).toBe('Game ended: over; tied between Team 1, Team 2');
    expect(line([])).toBe('Game ended: over');
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
    expect(opening.markers.map((m) => m.id)).toEqual([K1]);  // the board's setup markers lead every frame

    projector.add(ev(2, { kind: 'score_set', teamId: T2, score: 4 }));
    const dropped = projector.add(ev(3, { kind: 'step_completed', frameId: F1, step: 'drop', rolls: [], changes: [
      { kind: 'marker_placed', marker: { id: K9, kind: 'token', position: { x: 4, y: 4, z: 0 },
        yaw: { degrees: 0 }, diameter: 1, visual: { builtin: 'chip' } } },
    ] }));
    // The definition rides along per frame, so the viewer needs no join against the board block.
    expect(dropped.markers).toEqual([
      { id: K1, kind: 'objective', diameter: 2, label: 'A', x: 36, y: 24, z: 0, yaw: 0 },
      { id: K9, kind: 'token', diameter: 1, x: 4, y: 4, z: 0, yaw: 0 },
    ]);
    expect(dropped.scores).toEqual({ [T1]: 0, [T2]: 4 });

    const passed = projector.add(ev(4, { kind: 'turn_ended', teamId: T1, passed: true, changes: [] }));
    expect(passed.passed).toEqual([T1]);
    expect(passed.scores).toEqual({ [T1]: 0, [T2]: 4 });    // whole state, not a delta
  });

  describe('objective control', () => {
    it('follows its changes, alone or inside a round end, and leaves with the marker', () => {
      const a = new Applier(board, game);
      a.apply(ev(1, { kind: 'round_ended', round: 1, changes: [{ kind: 'objective_control_set', markerId: K1, teamId: T1 }] }));
      expect(a.projection.control.get(K1)).toBe(T1);
      a.apply(ev(2, { kind: 'objective_control_set', markerId: K1 }));
      expect(a.projection.control.has(K1)).toBe(false);
      a.apply(ev(3, { kind: 'objective_control_set', markerId: K9, teamId: T2 }));
      a.apply(ev(4, { kind: 'marker_removed', markerId: K9 }));
      expect(a.projection.control.size).toBe(0);
    });

    it('reads as the recording reads it', () => {
      const p = initialProjection(board, game);
      const ordinals = new Map([[T1, 1], [T2, 2]]);
      const line = (payload: unknown) => lineOf(ev(9, payload), p, ordinals);
      expect(line({ kind: 'round_ended', round: 4, changes: [] })).toBe('Round 4 ended');
      expect(line({ kind: 'objective_control_set', markerId: K1, teamId: T1 })).toBe('A to Team 1');   // the board's setup marker, labelled A
      expect(line({ kind: 'objective_control_set', markerId: K9 })).toBe(`${K9.slice(0, 8)} neutral`);
    });
  });
});

describe('a setup marker moved in play', () => {
  it('is copied into the play markers at the new spot, keeps its control, and the frame draws it once', () => {
    const projector = new FrameProjector(board, game);
    projector.add(ev(1, { kind: 'objective_control_set', markerId: K1, teamId: T1 }));
    const frame = projector.add(ev(2, { kind: 'marker_moved', markerId: K1, position: { x: 10, y: 12, z: 0 }, yaw: { degrees: 30 } }));
    expect(frame.markers.map((m) => m.id)).toEqual([K1]);
    expect(frame.markers[0]!.controlledBy).toBe(T1);
    const a = new Applier(board, game);
    a.apply(ev(1, { kind: 'marker_moved', markerId: K1, position: { x: 10, y: 12, z: 0 }, yaw: { degrees: 30 } }));
    expect(a.projection.markers.get(K1)).toMatchObject({ id: K1, kind: 'objective', label: 'A', position: { x: 10, y: 12, z: 0 }, yaw: { degrees: 30 } });
    expect(board.table.markers[0]!.position.x).toBe(36);   // the board row is untouched
  });
});

describe('the frame projector lists markers', () => {
  it('setup first, then play, each with its controller', () => {
    const projector = new FrameProjector(board, game);
    projector.add(ev(1, { kind: 'marker_placed', marker: { id: K9, kind: 'token', position: { x: 4, y: 4, z: 0 }, yaw: { degrees: 0 }, diameter: 1, visual: { builtin: 'chip' } } }));
    const frame = projector.add(ev(2, { kind: 'objective_control_set', markerId: K1, teamId: T2 }));
    expect(frame.markers.map((m) => m.id)).toEqual([K1, K9]);
    expect(frame.markers[0]!.controlledBy).toBe(T2);
    expect('controlledBy' in frame.markers[1]!).toBe(false);
  });
});
