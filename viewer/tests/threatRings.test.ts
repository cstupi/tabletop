import { describe, expect, it, vi } from 'vitest';
import type { Geometry, PlacedModel, Segment } from '../src/lib/geometry';
import { ThreatRingCache, ofAnotherTeam, threatRingsOf } from '../src/lib/play/threatRings';
import type { EventPayload, UnitThreat, Weapon } from '../src/lib/protocol/wire.generated';
import { P1, P2, at, boardOf, gameOf, projectionOf, slotOf, unitOf, weaponOf } from './play';

const gun = (name: string, range?: string): Weapon => ({ ...weaponOf(name), stats: range === undefined ? {} : { Range: range } });
const squad = unitOf('Squad', [
  slotOf({ name: 'A', ordinal: 1, weapons: [gun('Rifle', '24"')] }),
  slotOf({ name: 'B', ordinal: 2, weapons: [gun('Rifle', '24"')] }),
  slotOf({ name: 'C', ordinal: 3, weapons: [gun('Pistol', '12"')] }),
]);
const orcs = unitOf('Orcs', [slotOf({ name: 'Orc', weapons: [gun('Choppa')] })]);
const game = gameOf();
const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [orcs] }]);
const place = (id: string, x: number, y: number): EventPayload => ({ kind: 'models_moved', moves: [{ modelId: id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } }] });
const deploy = [place(squad.models[0]!.id, 10, 10), place(squad.models[1]!.id, 12, 10), place(squad.models[2]!.id, 14, 10), place(orcs.models[0]!.id, 40, 30)];
const squadThreat = (charge?: number, shootMove?: number): UnitThreat =>
  ({ charge, shootMove, ranges: { [squad.models[0]!.id]: 24, [squad.models[1]!.id]: 24, [squad.models[2]!.id]: 12 } });

function fake() {
  const calls: { models: readonly PlacedModel[]; range: number }[] = [];
  const rangeOutline = vi.fn((models: readonly PlacedModel[], range: number): Segment[] => {
    calls.push({ models, range });
    return [{ a: { x: range, y: 0 }, b: { x: range, y: 1 } }];
  });
  return { g: { rangeOutline } as unknown as Geometry, calls, rangeOutline };
}

describe('ThreatRings', () => {
  it('a charge ring round every model and a shoot ring per reach round its models', () => {
    const { g, calls } = fake();
    const p = projectionOf(game, board, deploy).projection;
    const rings = threatRingsOf(g, p, squad.id, squadThreat(12, 6));
    expect(rings.map((r) => [r.inches, r.label])).toEqual([[12, 'Charge 12"'], [18, 'Shoot 18"'], [30, 'Shoot 30"']]);
    expect(calls.map((c) => [c.range, c.models.map((m) => m.placement.position.x)])).toEqual([[12, [10, 12, 14]], [30, [10, 12]], [18, [14]]]);
  });

  it('no charge and no shot draw nothing; a unit without ranges draws only its charge', () => {
    const { g } = fake();
    const p = projectionOf(game, board, deploy).projection;
    expect(threatRingsOf(g, p, squad.id, squadThreat())).toEqual([]);
    expect(threatRingsOf(g, p, squad.id, squadThreat(undefined, 0)).map((r) => r.label)).toEqual(['Shoot 12"', 'Shoot 24"']);
    expect(threatRingsOf(g, p, orcs.id, { charge: 12, shootMove: 6, ranges: {} }).map((r) => r.label)).toEqual(['Charge 12"']);
  });

  it('the cache recomputes only when the unit, its placements or its threat change', () => {
    const { g, rangeOutline } = fake();
    const a = projectionOf(game, board, deploy);
    const cache = new ThreatRingCache();
    const first = cache.of(g, a.projection, squad.id, squadThreat(12, 6));
    expect(cache.of(g, a.projection, squad.id, squadThreat(12, 6))).toBe(first);
    const slower = cache.of(g, a.projection, squad.id, squadThreat(8, 4));
    expect(slower).not.toBe(first);
    expect(slower[0]!.inches).toBe(8);
    const calls = rangeOutline.mock.calls.length;
    a.apply({ seq: 99, actor: P1, payload: { kind: 'models_moved', moves: [{ modelId: squad.models[2]!.id, from: { kind: 'table', placement: at(14, 10) }, to: { kind: 'table', placement: at(20, 10) } }] } } as never);
    expect(cache.of(g, a.projection, squad.id, squadThreat(8, 4))).not.toBe(slower);
    expect(rangeOutline.mock.calls.length).toBeGreaterThan(calls);
  });

  it('only a unit of another team shows its threat, and a watcher sees every unit\'s', () => {
    const p = projectionOf(game, board, deploy).projection;
    const mine = game.participants.find((x) => x.id === P1)!.teamId!;
    expect([ofAnotherTeam(game, p, [mine], orcs.id), ofAnotherTeam(game, p, [mine], squad.id), ofAnotherTeam(game, p, [], squad.id), ofAnotherTeam(game, p, [mine], 'nope')])
      .toEqual([true, false, true, false]);
  });
});
