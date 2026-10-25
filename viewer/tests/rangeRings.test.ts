import { describe, expect, it, vi } from 'vitest';
import type { Geometry, PlacedModel, Segment } from '../src/lib/geometry';
import { RangeRingCache, inches, rangeRingsOf } from '../src/lib/play/rangeRings';
import type { EventPayload, Weapon } from '../src/lib/protocol/wire.generated';
import { P1, P2, at, boardOf, gameOf, projectionOf, slotOf, unitOf, weaponOf } from './play';

const gun = (name: string, range?: string): Weapon => ({ ...weaponOf(name), stats: range === undefined ? {} : { Range: range } });
const squad = unitOf('Squad', [
  slotOf({ name: 'A', ordinal: 1, weapons: [gun('Rifle', '24"'), gun('Knife')] }),
  slotOf({ name: 'B', ordinal: 2, weapons: [gun('Rifle', '24"')] }),
  slotOf({ name: 'C', ordinal: 3, weapons: [gun('Pistol', '12"')] }),
]);
const orcs = unitOf('Orcs', [slotOf({ name: 'Orc', weapons: [gun('Choppa', '-')] })]);
const game = gameOf();
const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [orcs] }]);
const place = (id: string, x: number, y: number): EventPayload => ({ kind: 'models_moved', moves: [{ modelId: id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } }] });
const deploy = [place(squad.models[0]!.id, 10, 10), place(squad.models[1]!.id, 12, 10), place(squad.models[2]!.id, 14, 10), place(orcs.models[0]!.id, 40, 30)];

function fake() {
  const calls: { models: readonly PlacedModel[]; range: number }[] = [];
  const rangeOutline = vi.fn((models: readonly PlacedModel[], range: number): Segment[] => {
    calls.push({ models, range });
    return [{ a: { x: range, y: 0 }, b: { x: range, y: 1 } }];
  });
  return { g: { rangeOutline } as unknown as Geometry, calls, rangeOutline };
}

describe('RangeRings', () => {
  it('inches reads the printed number and treats melee as none', () => {
    expect([inches('24"'), inches('-'), inches('0'), inches(undefined), inches(' 6.5" ')]).toEqual([24, null, null, null, 6.5]);
  });

  it('one ring per distinct printed range around the models carrying it', () => {
    const { g, calls } = fake();
    const p = projectionOf(game, board, deploy).projection;
    const rings = rangeRingsOf(g, p, squad.id);
    expect(rings.map((r) => [r.inches, r.label])).toEqual([[12, '12"'], [24, '24"']]);
    expect(calls.map((c) => [c.range, c.models.map((m) => m.placement.position.x)])).toEqual([[12, [14]], [24, [10, 12]]]);
    expect(rangeRingsOf(g, p, orcs.id)).toEqual([]);                                 // melee only
  });

  it('the cache recomputes only when the unit or its placements change', () => {
    const { g, rangeOutline } = fake();
    const a = projectionOf(game, board, deploy);
    const cache = new RangeRingCache();
    const first = cache.of(g, a.projection, squad.id);
    expect(cache.of(g, a.projection, squad.id)).toBe(first);
    expect(cache.of(g, a.projection, orcs.id)).toEqual([]);
    const back = cache.of(g, a.projection, squad.id);
    expect(back).not.toBe(first);
    const calls = rangeOutline.mock.calls.length;
    a.apply({ seq: 99, actor: P1, payload: { kind: 'models_moved', moves: [{ modelId: squad.models[2]!.id, from: { kind: 'table', placement: at(14, 10) }, to: { kind: 'table', placement: at(20, 10) } }] } } as never);
    const moved = cache.of(g, a.projection, squad.id);
    expect(moved).not.toBe(back);
    expect(rangeOutline.mock.calls.length).toBeGreaterThan(calls);
  });
});
