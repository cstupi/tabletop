import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry, Placement } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { exitStart, exitWords } from '../src/lib/play/moveRuler';
import { sentence } from '../src/lib/play/contactCheck';
import { shortWords } from '../src/lib/play/markLabels';
import type { MoveRegion } from '../src/lib/protocol/wire.generated';
import { P1, boardOf, gameOf, slotOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

// MoveRuler.ExitStart and ExitWords (DisembarkStartTests): five infantry and a cavalry model inside a Light APC on a 5" by 3" base.
const size = { width: 72, length: 48 };
let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

function inside(x: number, y: number, yaw = 0) {
  const riders = [1, 2, 3, 4, 5].map((i) => slotOf({ name: `m${i}`, ordinal: i, base: { shape: 'circle', a: 1.26, b: 1.26 }, height: 1.5 }))
    .concat([slotOf({ name: 'm6', ordinal: 6, base: { shape: 'oval', a: 2.36, b: 1.38 }, height: 2 })]);
  const apc = slotOf({ name: 'Light APC', base: { shape: 'rect', a: 5, b: 3 }, height: 2.5 });
  const squad = unitOf('Veterans', riders), transport = unitOf('Light APC', [apc]);
  const applier = new Applier(boardOf([{ participantId: P1, units: [squad, transport] }]), gameOf());
  applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [
    { modelId: apc.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees: yaw } } } },
    ...riders.map((r) => ({ modelId: r.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: apc.id } }))] } } as never);
  const legal: MoveRegion = { kind: 'move_region', modelIds: riders.map((r) => r.id), allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [],
    stoppable: false, passesModels: false, ignoresTerrain: false, separation: 1, containment: { enterable: [], exitingId: apc.id, exitWithin: 6 } } as MoveRegion;
  return { p: applier.projection, legal, apc, riders };
}

describe('a disembark\'s ghosts over the real geometry', () => {
  it.each([[36, 24, 0], [36, 24, 37], [2.6, 24, 90], [3, 2, 0]])('every passenger starts beside the transport within the exit reach (%s, %s, %s°)', (x, y, yaw) => {
    const { p, legal, apc } = inside(x, y, yaw);
    const start = exitStart(g, p, legal, size);
    expect(start.map((r) => r.modelId)).toEqual(legal.modelIds);
    const container = { body: { base: apc.base, height: apc.height }, placement: { position: { x, y, z: 0 }, yaw: { degrees: yaw } } };
    const bodies = start.map((r) => ({ body: { base: p.models.get(r.modelId)!.slot.base, height: 1 }, placement: (r.to as { placement: Placement }).placement }));
    for (const b of bodies) {
      expect(g.bodyToBody(b, container)).toBeGreaterThanOrEqual(1 - 1e-6);
      expect(g.tableHoldsBase(size, b)).toBe(true);
      expect(exitWords(g, p, legal, new Map(start.map((r) => [r.modelId, (r.to as { placement: Placement }).placement])), size, () => 'Light APC').size).toBe(0);
    }
    for (let i = 0; i < bodies.length; i++) for (let j = i + 1; j < bodies.length; j++) expect(g.overlapBases(bodies[i]!, bodies[j]!)).toBe(false);
  });

  it('a ghost beyond the exit reach, or over the table edge, is marked', () => {
    const { p, legal, riders } = inside(36, 24);
    const far = new Map([[riders[0]!.id, { position: { x: 36, y: 24 + 1.5 + 7, z: 0 }, yaw: { degrees: 0 } }]]);
    const words = exitWords(g, p, legal, far, size, () => 'Light APC');
    expect(words.get(riders[0]!.id)).toBe('beyond 6" of Light APC');
    expect(shortWords(words.get(riders[0]!.id)!)).toBe('Beyond 6"');
    expect(sentence(words.get(riders[0]!.id)!, 'm1')).toBe('m1 would be placed beyond 6" of Light APC');
    const edge = inside(3, 24);
    const off = new Map([[edge.riders[0]!.id, { position: { x: 0.2, y: 24, z: 0 }, yaw: { degrees: 0 } }]]);
    expect(exitWords(g, edge.p, edge.legal, off, size, () => 'Light APC').get(edge.riders[0]!.id)).toBe('would leave the table');
  });
});
