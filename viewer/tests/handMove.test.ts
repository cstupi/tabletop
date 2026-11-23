import { describe, expect, it } from 'vitest';
import type { Contact, Geometry } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { handMarks, handMoves, markerMove, mayDrag } from '../src/lib/play/handMove';
import { MarkerLocks } from '../src/lib/play/markerLocks';
import { Names } from '../src/lib/play/names';
import { PendingDrops } from '../src/lib/play/pendingDrops';
import { sceneOfPlay } from '../src/lib/play/scene';
import { P1, markerOf, sample } from './play';

const at = (x: number, y: number, yaw = 0) => ({ position: { x, y, z: 0 }, yaw: { degrees: yaw } });
const none = new Set<string>();
const locks = new MarkerLocks();
const models = (...ids: string[]) => ({ kind: 'models', ids }) as const;

describe('hand moves', () => {
  it('under strict a hand drag does not start', () => {
    expect(mayDrag(models('a'), false, none, 'strict', 'playing', null, locks)).toBe(false);
    expect(mayDrag(models('a'), false, none, 'warn', 'playing', null, locks)).toBe(true);
    expect(mayDrag(models('a'), false, none, 'silent', 'playing', null, locks)).toBe(true);
  });

  it('proposing models drag under strict', () => {
    expect(mayDrag(models('a', 'b'), false, new Set(['a', 'b']), 'strict', 'playing', null, locks)).toBe(true);
    expect(mayDrag(models('a', 'b'), false, new Set(['a']), 'strict', 'playing', null, locks)).toBe(false);
  });

  it('a model the open Ask holds still never drags: a disembark transport', () => {
    const transport = new Set(['apc']);
    expect(mayDrag(models('apc'), false, new Set(['p1']), 'warn', 'playing', null, locks, transport)).toBe(false);
    expect(mayDrag(models('apc'), false, none, 'silent', 'playing', null, locks, transport)).toBe(false);
    expect(mayDrag(models('p1'), false, new Set(['p1']), 'strict', 'playing', null, locks, transport)).toBe(true);
  });

  it('a watcher drags nothing', () => {
    expect(mayDrag(models('a'), true, new Set(['a']), 'silent', 'playing', null, locks)).toBe(false);
    expect(mayDrag({ kind: 'marker', id: 'm' }, true, none, 'silent', 'playing', null, locks)).toBe(false);
    expect(mayDrag({ kind: 'terrain', id: 't' }, true, none, 'silent', 'setup', null, locks)).toBe(false);
  });

  it('a locked objective does not drag until unlocked', () => {
    const s = sample();
    const objective = markerOf('Obj');
    const p = new Applier({ ...s.board, table: { ...s.board.table, markers: [objective] } }, s.game).projection;
    const l = new MarkerLocks();
    expect(mayDrag({ kind: 'marker', id: objective.id }, false, none, 'warn', 'playing', p, l)).toBe(false);
    l.itemFor('playing', p, objective.id)!.apply();
    expect(mayDrag({ kind: 'marker', id: objective.id }, false, none, 'warn', 'playing', p, l)).toBe(true);
  });

  it('terrain drags only in setup', () => {
    expect(mayDrag({ kind: 'terrain', id: 't' }, false, none, 'warn', 'setup', null, locks)).toBe(true);
    expect(mayDrag({ kind: 'terrain', id: 't' }, false, none, 'warn', 'playing', null, locks)).toBe(false);
  });

  it('hand moves go from the grab to the landing', () => {
    const moves = handMoves(new Map([['a', at(1, 1)], ['b', at(2, 2)]]), new Map([['a', at(5, 5, 90)], ['b', at(2, 3)]]));
    expect(moves).toEqual([
      { modelId: 'a', from: { kind: 'table', placement: at(1, 1) }, to: { kind: 'table', placement: at(5, 5, 90) } },
      { modelId: 'b', from: { kind: 'table', placement: at(2, 2) }, to: { kind: 'table', placement: at(2, 3) } },
    ]);
    expect(markerMove('m', { x: 1, y: 2, z: 0 }, { degrees: 30 })).toEqual({ kind: 'move_marker', markerId: 'm', position: { x: 1, y: 2, z: 0 }, yaw: { degrees: 30 } });
  });

  it('hand marks name overlaps while in hand', () => {
    const s = sample();
    const p = new Applier(s.board, s.game).projection;
    const calls: unknown[][] = [];
    const g = { contacts: (...args: unknown[]) => { calls.push(args); return new Map<string, Contact>([[s.model.id, { modelId: s.model.id, kind: 'on_base' }]]); } } as unknown as Geometry;
    const held = new Map([[s.model.id, at(40, 30)]]);
    expect(handMarks(g, p, held, new Names(s.game, p))).toEqual(new Map([[s.model.id, 'overlaps a base']]));
    expect(calls[0]![1]).toBe(held);
    expect(calls[0]![3]).toEqual({ passesModels: true, landing: false });       // overlap only
  });

  it('a dropped model is drawn at its drop until the move\'s event', () => {
    const s = sample();
    const applier = new Applier(s.board, s.game);
    const p = applier.projection;
    const home = at(10, 10), drop = at(20, 15, 90);
    applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: home } }] } } as never);
    const drops = new PendingDrops();
    const centre = () => sceneOfPlay(p, s.game, () => [], () => 'x', drops.all).models.find((m) => m.id === s.model.id)!.centre;
    const move = { modelId: s.model.id, from: { kind: 'table', placement: home }, to: { kind: 'table', placement: drop } } as const;
    expect(centre()).toEqual({ x: 10, y: 10 });
    drops.add('t1', [move]);
    expect(centre()).toEqual({ x: 20, y: 15 });                               // before the event: not snapped back
    applier.apply({ seq: 2, actor: P1, payload: { kind: 'models_moved', moves: [move] } } as never);
    expect(drops.consume(move)).toBe(true);
    expect(centre()).toEqual({ x: 20, y: 15 });                               // the projection says the same
    drops.add('t2', [{ ...move, from: move.to, to: { kind: 'table', placement: at(1, 1) } }]);
    expect(drops.refuse('t2')).toEqual([s.model.id]);
    expect(centre()).toEqual({ x: 20, y: 15 });                               // a refusal: back to the projection's truth
  });
});
