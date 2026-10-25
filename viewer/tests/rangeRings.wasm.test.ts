import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { rangeRingsOf } from '../src/lib/play/rangeRings';
import { P1, at, boardOf, gameOf, projectionOf, slotOf, unitOf, weaponOf } from './play';
import { loadTestGeometry } from './geometry';

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe('rangeRingsOf through the WebAssembly', () => {
  it('a lone 1" base at range 6 is one closed ring 6.5" out', () => {
    const lone = unitOf('Lone', [slotOf({ name: 'L', weapons: [{ ...weaponOf('Rifle'), stats: { Range: '6"' } }] })]);
    const p = projectionOf(gameOf(), boardOf([{ participantId: P1, units: [lone] }]), [
      { kind: 'models_moved', moves: [{ modelId: lone.models[0]!.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(20, 20) } }] },
    ]).projection;
    const [ring] = rangeRingsOf(g, p, lone.id);
    expect(ring!.label).toBe('6"');
    const points = ring!.segments.flatMap((s) => [s.a, s.b]);
    for (const q of points) expect(Math.hypot(q.x - 20, q.y - 20)).toBeCloseTo(6.5, 2);
    for (const q of points) expect(ring!.segments.filter((s) => Math.hypot(s.a.x - q.x, s.a.y - q.y) < 1e-3 || Math.hypot(s.b.x - q.x, s.b.y - q.y) < 1e-3).length).toBe(2);
  });
});
