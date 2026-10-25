import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry, Placement } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { move, placement } from '../src/lib/play/contactCheck';
import type { MoveRegion } from '../src/lib/protocol/wire.generated';
import { P1, P2, boardOf, gameOf, slotOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

const at = (x: number, y: number): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: 0 } });
const size = { width: 72, length: 48 };
const region = (ids: string[], over: Partial<MoveRegion> = {}): MoveRegion => ({
  kind: 'move_region', modelIds: ids, allowance: 30, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false, ...over,
});

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

function deployed() {
  const squad = unitOf('Squad', [1, 2, 3].map((i) => slotOf({ name: `S${i}`, ordinal: i })));
  const orcs = unitOf('Orc Boyz', [slotOf({ name: 'Orc' })]);
  const reserve = unitOf('Reserve', [slotOf({ name: 'R' })]);
  const applier = new Applier(boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }]), gameOf());
  const [s0, s1, s2] = squad.models.map((m) => m.id) as [string, string, string];
  const orc = orcs.models[0]!.id, r0 = reserve.models[0]!.id;
  const relocate = (id: string, to: Placement) => ({ modelId: id, from: { kind: 'shelf' } as const, to: { kind: 'table', placement: to } as const });
  applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [
    relocate(s0, at(10, 10)), relocate(s1, at(12, 10)), relocate(s2, at(14, 10)), relocate(orc, at(40, 30))] } } as never);
  return { p: applier.projection, s0, s1, s2, orc, r0, orcsId: orcs.id };
}

describe('contactCheck over the real geometry', () => {
  it('a landing on another model\'s base overlaps', () => {
    const d = deployed();
    const found = move(g, d.p, region([d.s0]), new Map([[d.s0, at(40.5, 30)]]), size);
    expect(found.get(d.s0)).toMatchObject({ kind: 'on_base', unitId: d.orcsId });
  });

  it('a landing within the separation of another unit\'s model is too close', () => {
    const d = deployed();
    expect(move(g, d.p, region([d.s0], { separation: 1 }), new Map([[d.s0, at(38.2, 30)]]), size).get(d.s0)).toMatchObject({ kind: 'too_close', unitId: d.orcsId });
    expect(move(g, d.p, region([d.s0], { separation: 1 }), new Map([[d.s0, at(38, 30)]]), size).size).toBe(0);
    expect(move(g, d.p, region([d.s0]), new Map([[d.s0, at(38.2, 30)]]), size).size).toBe(0);
  });

  it('a model answered where it stands has not moved', () => {
    const d = deployed();
    d.p.models.get(d.orc)!.location = { kind: 'table', placement: at(10.5, 10) };       // on s0's base
    expect(move(g, d.p, region([d.s0], { separation: 1 }), new Map([[d.s0, at(10, 10)]]), size).size).toBe(0);
  });

  it('a hand placement is held to overlap only', () => {
    const d = deployed();
    expect(placement(g, d.p, new Map([[d.r0, at(40.5, 30)]])).get(d.r0)).toMatchObject({ kind: 'on_base' });
    expect(placement(g, d.p, new Map([[d.r0, at(38.2, 30)]])).size).toBe(0);
    expect(placement(g, d.p, new Map([[d.s0, at(16, 10)]])).size).toBe(0);
  });
});
