import { describe, expect, it } from 'vitest';
import { SightCache, SightKey } from '../src/lib/play/sightKey';
import type { EventPayload, Location, TableState } from '../src/lib/protocol/wire.generated';
import { P1, P2, at, boardOf, emptyTable, gameOf, projectionOf, slotOf, unitOf } from './play';

const squad = unitOf('Squad', [slotOf({ name: 'S1' }), slotOf({ name: 'S2', ordinal: 2 })]);
const reserve = unitOf('Reserve');
const orcs = unitOf('Orcs');
const game = gameOf();
const mid = (id: string, x: number, y: number, from: Location = { kind: 'shelf' }): EventPayload =>
  ({ kind: 'models_moved', moves: [{ modelId: id, from, to: { kind: 'table', placement: at(x, y) } }] });
const deploy = [mid(squad.models[0]!.id, 10, 10), mid(squad.models[1]!.id, 12, 10), mid(orcs.models[0]!.id, 40, 30)];
const after = (more: EventPayload[] = [], table: TableState = emptyTable()) =>
  projectionOf(game, boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }], table), [...deploy, ...more]).projection;
const key = (p: ReturnType<typeof after>, from = squad.id, to = orcs.id) => SightKey.of(p, from, to);
const table = emptyTable();

describe('SightKey', () => {
  it('same placements give the same key', () => {
    expect(key(after([], table)).equals(key(after([], table)))).toBe(true);
  });

  it('an observer or target model moving changes the key', () => {
    const before = key(after());
    expect(before.equals(key(after([mid(squad.models[0]!.id, 11, 10, { kind: 'table', placement: at(10, 10) })])))).toBe(false);
    expect(before.equals(key(after([mid(orcs.models[0]!.id, 40, 31, { kind: 'table', placement: at(40, 30) })])))).toBe(false);
  });

  it('a model of another unit moving changes the key', () => {
    expect(key(after()).equals(key(after([mid(reserve.models[0]!.id, 20, 20)])))).toBe(false);
  });

  it('a model leaving the table changes the key', () => {
    const gone: EventPayload = { kind: 'models_moved', moves: [{ modelId: orcs.models[0]!.id, from: { kind: 'table', placement: at(40, 30) }, to: { kind: 'shelf' } }] };
    expect(key(after()).equals(key(after([gone])))).toBe(false);
  });

  it('terrain changing changes the key', () => {
    const piece = { id: 'piece', position: { x: 12, y: 12, z: 0 }, yaw: { degrees: 0 }, volumes: [] } as unknown as TableState['terrain'][number];
    const withPiece = { ...emptyTable(), terrain: [piece] } as TableState;
    const shifted = { ...emptyTable(), terrain: [{ ...piece, position: { x: 13, y: 12, z: 0 } }] } as TableState;
    const same = { ...emptyTable(), terrain: [{ ...piece }] } as TableState;
    expect(key(after([], withPiece)).equals(key(after([], shifted)))).toBe(false);
    expect(key(after([], withPiece)).equals(key(after([], same)))).toBe(true);
  });

  it('the pair is part of the key', () => {
    const p = after();
    expect(key(p).equals(key(p, reserve.id))).toBe(false);
  });

  it('cache asks a new pair once, then only after a change, and drops unassigned pairs', () => {
    const cache = new SightCache();
    const p = after();
    expect(cache.needsAsk(orcs.id, key(p))).toBe(true);
    cache.asked(orcs.id, key(p));
    expect(cache.needsAsk(orcs.id, key(p))).toBe(false);
    const moved = after([mid(squad.models[0]!.id, 11, 10, { kind: 'table', placement: at(10, 10) })]);
    expect(cache.needsAsk(orcs.id, key(moved))).toBe(true);
    cache.keep([]);
    expect(cache.needsAsk(orcs.id, key(p))).toBe(true);
  });
});
