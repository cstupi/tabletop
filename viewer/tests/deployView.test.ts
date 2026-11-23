import { describe, expect, it } from 'vitest';
import type { ModelInPlay } from '../src/lib/live/apply';
import { around, layout, midDeploy, offTable, pickable } from '../src/lib/play/deployView';
import type { Ask, AskClosed, EventPayload, ModelRelocation } from '../src/lib/protocol/wire.generated';
import { TEAM1, TEAM2, askOf, at, newId, projectionOf, sample, slotOf } from './play';

const s = sample();
const SIZE = { width: 72, length: 48 };
const deployAsk = (question = 'deploy', team = TEAM1): Ask => askOf({ kind: 'pieces', eligible: [s.squad.id], min: 1, max: 1 }, { question, prompt: 'Deploy one unit', teams: [team], allowPass: false });
const closed = (a: Ask, answer: AskClosed['answers'][number]['answer'], team = TEAM1): EventPayload => ({ kind: 'ask_closed', askId: a.id, answers: [{ teamId: team, answer }] });
const placed: ModelRelocation = { modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } };
const open = (a: Ask): EventPayload => ({ kind: 'ask_opened', ask: a });
const pick = { kind: 'selected_pieces', pieceIds: [s.squad.id] } as const;
const applied = (...p: EventPayload[]) => projectionOf(s.game, s.board, p);

describe('DeployView', () => {
  it('a unit picked at deploy with models still on the shelf is mid deploy', () => {
    const ask = deployAsk();
    const a = applied(open(ask), { kind: 'ask_answered', askId: ask.id, teamId: TEAM1, answer: pick, outsideLegalSet: false }, closed(ask, pick));
    expect(midDeploy(a.projection, a.log, TEAM1)).toEqual({ unitId: s.squad.id, question: 'deploy', shelvedModelIds: [s.model.id], keepAway: null, zoneReach: null });
  });

  it('a pick carries its unit\'s keep-away', () => {
    const keepAway = { units: [{ unitId: newId(), over: 9 }] };
    const ask = askOf({ kind: 'pieces', eligible: [s.squad.id], min: 1, max: 1, keepAway: { [s.squad.id]: keepAway } },
      { question: 'deploy_ambusher', prompt: 'Deploy an Ambusher or pass', teams: [TEAM1], allowPass: true });
    const a = applied(open(ask), closed(ask, pick));
    expect(midDeploy(a.projection, a.log, TEAM1)!.keepAway).toEqual(keepAway);
  });

  it('a pick made again after an undo is mid deploy whatever the undone events were', () => {
    const ask = deployAsk(), next = deployAsk();
    const a = applied(open(ask), closed(ask, pick), { kind: 'models_moved', moves: [placed] }, { kind: 'turn_ended', teamId: TEAM1, passed: false, changes: [] },
      { kind: 'turn_started', teamId: TEAM1 }, open(next), { kind: 'undone', toSeq: 1 }, closed(ask, pick));
    expect(midDeploy(a.projection, a.log, TEAM1)!.unitId).toBe(s.squad.id);
  });

  it.each(['deploy_scouts', 'deploy_ambusher', 'deploy_reinforcement'])('%s deploys the same way', (question) => {
    const ask = deployAsk(question);
    const a = applied(open(ask), closed(ask, pick));
    expect(midDeploy(a.projection, a.log, TEAM1)!.unitId).toBe(s.squad.id);
  });

  it('nothing is mid deploy once every model is placed or while the Ask is open or for the other team or after a pass or a later Ask', () => {
    const ask = deployAsk();
    const picked: EventPayload[] = [open(ask), closed(ask, pick)];
    const done = applied(...picked, { kind: 'models_moved', moves: [placed] });
    expect(midDeploy(done.projection, done.log, TEAM1)).toBeNull();
    const opened = applied(open(ask));
    expect(midDeploy(opened.projection, opened.log, TEAM1)).toBeNull();
    const o2 = applied(...picked);
    expect(midDeploy(o2.projection, o2.log, TEAM2)).toBeNull();
    expect(midDeploy(o2.projection, o2.log, null)).toBeNull();
    const passed = applied(open(ask), closed(ask, { kind: 'passed' }));
    expect(midDeploy(passed.projection, passed.log, TEAM1)).toBeNull();
    const activate = askOf({ kind: 'options', choices: [] }, { teams: [TEAM2] });
    const later = applied(...picked, open(activate));
    expect(midDeploy(later.projection, later.log, TEAM1)).toBeNull();
  });

  it('nothing is mid deploy once the server ends the turn on a withdrawn pick', () => {
    const ask = deployAsk();
    const a = applied(open(ask), closed(ask, pick), { kind: 'turn_ended', teamId: TEAM1, passed: false, changes: [] });
    expect(midDeploy(a.projection, a.log, TEAM1)).toBeNull();
  });

  const model = (ordinal: number, a: number): ModelInPlay => ({
    id: newId(), unitId: s.squad.id, slot: slotOf({ name: `m${ordinal}`, ordinal, base: { shape: 'circle', a, b: a } }), location: { kind: 'shelf' },
    wounds: 0, conditions: new Set(), spellTokens: 0,
  });

  it('models lay out in a row centred on the point spaced by the widest base', () => {
    const models = [model(1, 1), model(2, 2), model(3, 1)];
    const moves = layout(models, { x: 36, y: 24, z: 0 }, SIZE);
    expect(moves.map((m) => m.modelId)).toEqual(models.map((m) => m.id));
    expect(moves.every((m) => m.from.kind === 'shelf')).toBe(true);
    const placements = moves.map((m) => (m.to as { placement: { position: { x: number; y: number }; yaw: { degrees: number } } }).placement);
    expect(placements.every((p) => p.yaw.degrees === 0)).toBe(true);
    expect(placements.every((p) => p.position.y === 24)).toBe(true);
    const pitch = placements[1]!.position.x - placements[0]!.position.x;
    expect(pitch).toBeGreaterThan(2);
    expect(pitch).toBeLessThan(2.6);
    expect(placements[2]!.position.x - placements[1]!.position.x).toBeCloseTo(pitch, 9);
    expect(placements[1]!.position.x).toBeCloseTo(36, 9);
  });

  it('oval bases space a row by their width across the facing and the rows by their length, within 1" (p.7)', () => {
    // 60x35mm bikes: a = 2.362" along the facing, b = 1.378" across it. Spaced by a, side by side the edges sat 1.2" apart.
    const bike = (ordinal: number): ModelInPlay => ({ ...model(ordinal, 1), slot: slotOf({ name: `b${ordinal}`, ordinal, base: { shape: 'oval', a: 2.362, b: 1.378 } }) });
    const ps = layout(Array.from({ length: 8 }, (_, i) => bike(i + 1)), { x: 36, y: 24, z: 0 }, SIZE)
      .map((m) => (m.to as { placement: { position: { x: number; y: number } } }).placement.position);
    const across = ps[1]!.x - ps[0]!.x, along = ps[0]!.y - ps[5]!.y;
    expect(across - 1.378).toBeGreaterThan(0);
    expect(across - 1.378).toBeLessThanOrEqual(1);
    expect(along - 2.362).toBeGreaterThan(0);
    expect(along - 2.362).toBeLessThanOrEqual(1);
  });

  it('a large unit wraps to rows of five and stays on the table', () => {
    const models = Array.from({ length: 12 }, (_, i) => model(i + 1, 1));
    const at2 = layout(models, { x: 0.5, y: 0.5, z: 0 }, SIZE).map((m) => (m.to as { placement: { position: { x: number; y: number } } }).placement.position);
    expect(new Set(at2.map((p) => p.y)).size).toBe(3);
    expect(new Set(at2.map((p) => p.x)).size).toBe(5);
    for (const p of at2) { expect(p.x).toBeGreaterThanOrEqual(0.5); expect(p.x).toBeLessThanOrEqual(71.5); expect(p.y).toBeGreaterThanOrEqual(0.5); expect(p.y).toBeLessThanOrEqual(47.5); }
    expect(new Set(at2.map((p) => `${p.x},${p.y}`)).size).toBe(12);
  });

  it('pieces with no node are the eligible units and models not on the table', () => {
    const shelved = applied();
    expect(offTable(shelved.projection, [s.squad.id])).toEqual([s.squad.id]);
    expect(offTable(shelved.projection, [s.model.id])).toEqual([s.model.id]);
    const on = applied({ kind: 'models_moved', moves: [placed] });
    expect(offTable(on.projection, [s.squad.id, s.model.id])).toEqual([]);
  });

  it('a deploy pick lists only units with a model on the shelf', () => {
    const rhino = s.rivals;
    const ask = askOf({ kind: 'pieces', eligible: [rhino.id, s.squad.id], min: 1, max: 1 }, { question: 'deploy', teams: [TEAM1] });
    const inside = applied({ kind: 'models_moved', moves: [placed] });
    expect(pickable(inside.projection, ask)).toEqual([rhino.id]);
    expect(pickable(applied().projection, ask)).toEqual([rhino.id, s.squad.id]);
    expect(pickable(inside.projection, { ...ask, question: 'pick_target' })).toEqual([rhino.id, s.squad.id]);
    expect(pickable(inside.projection, askOf({ kind: 'confirmation' }))).toEqual([]);
  });
});

describe('DeployView.around', () => {
  const squad = (n: number) => Array.from({ length: n }, (_, i) => model(i + 1, 1));
  const model = (ordinal: number, a: number): ModelInPlay => ({
    id: newId(), unitId: s.squad.id, slot: slotOf({ name: `m${ordinal}`, ordinal, base: { shape: 'circle', a, b: a } }), location: { kind: 'shelf' },
    wounds: 0, conditions: new Set(), spellTokens: 0,
  });
  const pos = (r: ModelRelocation) => (r.to as { placement: { position: { x: number; y: number } } }).placement.position;

  it('shelf models ring the anchor touching neither it nor each other', () => {
    const models = squad(4);
    const anchor = at(30, 20);
    const base = { shape: 'circle', a: 2, b: 2 } as const;
    const out = around(models, anchor, base, SIZE);
    expect(out.map((r) => r.modelId)).toEqual(models.map((m) => m.id));
    for (const r of out) expect(Math.hypot(pos(r).x - 30, pos(r).y - 20)).toBeCloseTo(1 + 0.2 + 0.5, 6);
    for (let i = 0; i < out.length; i++) for (let j = i + 1; j < out.length; j++)
      expect(Math.hypot(pos(out[i]!).x - pos(out[j]!).x, pos(out[i]!).y - pos(out[j]!).y)).toBeGreaterThanOrEqual(1.2 - 1e-9);
    for (const r of around(models, anchor, base, SIZE, 1)) expect(Math.hypot(pos(r).x - 30, pos(r).y - 20)).toBeCloseTo(1 + 1 + 0.5, 6);
  });

  it.each([[0.5, 0.5, false], [2, 24, false], [71, 47, true], [36, 1.5, true]])('near the edge (%s, %s) every model lands on the table and clear of the anchor', (x, y, rect) => {
    const models = squad(6);
    const base = rect ? { shape: 'rect', a: 5, b: 3 } as const : { shape: 'circle', a: 1, b: 1 } as const;
    const out = around(models, at(x, y), base, SIZE, 1);
    expect(out).toHaveLength(models.length);
    const reach = rect ? Math.hypot(5, 3) / 2 : 0.5;
    for (const r of out) {
      const p = pos(r);
      expect(p.x).toBeGreaterThanOrEqual(0.5); expect(p.x).toBeLessThanOrEqual(SIZE.width - 0.5);
      expect(p.y).toBeGreaterThanOrEqual(0.5); expect(p.y).toBeLessThanOrEqual(SIZE.length - 0.5);
      expect(Math.hypot(p.x - x, p.y - y)).toBeGreaterThanOrEqual(reach + 1 + 0.5 - 1e-6);
    }
    for (let i = 0; i < out.length; i++) for (let j = i + 1; j < out.length; j++)
      expect(Math.hypot(pos(out[i]!).x - pos(out[j]!).x, pos(out[i]!).y - pos(out[j]!).y)).toBeGreaterThan(1);
  });
});
