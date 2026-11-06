import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry, Placement } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { ModelDrag } from '../src/lib/play/modelDrag';
import type { MoveRegion } from '../src/lib/protocol/wire.generated';
import { P1, P2, boardOf, gameOf, slotOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

const at = (x: number, y: number): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: 0 } });
let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe('ModelDrag over the real geometry', () => {
  it('two models dropped on one spot land apart', () => {
    const squad = unitOf('Squad', [1, 2].map((i) => slotOf({ name: `S${i}`, ordinal: i })));
    const applier = new Applier(boardOf([{ participantId: P1, units: [squad] }]), gameOf());
    const [a, b] = squad.models.map((m) => m.id) as [string, string];
    const on = (id: string, to: Placement) => ({ modelId: id, from: { kind: 'shelf' } as const, to: { kind: 'table', placement: to } as const });
    applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [on(a, at(10, 10)), on(b, at(14, 10))] } } as never);
    const drag = new ModelDrag(g);
    drag.start(applier.projection, a, { x: 10, y: 10 }, [a], new Set(), new Map());
    const held = drag.move(applier.projection, { x: 14, y: 10 }, false, null, new Map())!;       // a is dropped on b's spot
    const lands = held.ends.get(a)!.lands;
    const body = (id: string, position: { x: number; y: number; z: number }) =>
      ({ body: { base: applier.projection.models.get(id)!.slot.base, height: 1 }, placement: { position, yaw: { degrees: 0 } } });
    expect(Math.hypot(lands.x - 14, lands.y - 10)).toBeGreaterThan(0);
    expect(g.edgeToEdge(body(a, lands), body(b, { x: 14, y: 10, z: 0 }))).toBeGreaterThanOrEqual(-1e-6);
  });

  it('a charger dropped 0.3" short of its target lands touching it', () => {
    const squad = unitOf('Squad', [slotOf({ name: 'S1' })]);
    const rivals = unitOf('Rivals', [slotOf({ name: 'Rival' })]);
    const applier = new Applier(boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [rivals] }]), gameOf());
    const a = squad.models[0]!.id, rival = rivals.models[0]!.id;
    const on = (id: string, to: Placement) => ({ modelId: id, from: { kind: 'shelf' } as const, to: { kind: 'table', placement: to } as const });
    applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [on(a, at(30, 20)), on(rival, at(40, 20))] } } as never);
    const charge: MoveRegion = { kind: 'move_region', modelIds: [a], allowance: 12, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false,
      ignoresTerrain: false, contactWith: [rival], contactGrace: 0.5 };
    const drag = new ModelDrag(g);
    drag.start(applier.projection, a, { x: 30, y: 20 }, [a], new Set([a]), new Map());
    const lands = drag.move(applier.projection, { x: 38.7, y: 20 }, false, charge, new Map())!.ends.get(a)!.lands;   // 1" bases: touching at 39
    expect(lands.x).toBeCloseTo(39, 6);
    expect(lands.y).toBeCloseTo(20, 6);
  });
});
