import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import type { TableState, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import { SetupDrag } from '../src/lib/setup/drag';
import { loadTestGeometry } from './geometry';

const ID = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const block = (n: number, x: number, y: number): TerrainPlacement => ({
  id: ID(n), mesh: { builtin: 'block' }, position: { x, y, z: 0 }, yaw: { degrees: 0 }, scale: 1,
  volumes: [{ id: ID(n + 100), volume: {
    kind: 'solid', footprint: { kind: 'rect', a: 4, b: 4 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 4,
    declarations: 'impassable',
  } }],
});

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe('SetupDrag over the real geometry', () => {
  it('a piece dropped onto an Impassable solid lands clear of it', () => {
    const draft: TableState = {
      id: 'd', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, markers: [], zones: [],
      terrain: [block(1, 10, 10), block(2, 30, 10)],
    };
    const drag = new SetupDrag(g);
    expect(drag.start(draft, { kind: 'terrain', id: ID(2), volumeId: ID(102) }, { x: 30, y: 10 })).toBe(true);
    drag.move(draft, { x: 10, y: 10 });
    const drop = drag.end(draft);
    expect(drop?.kind).toBe('move_terrain');
    if (drop?.kind !== 'move_terrain') return;
    expect(drop.position.z).toBe(0);
    expect(Math.max(Math.abs(drop.position.x - 10), Math.abs(drop.position.y - 10))).toBeGreaterThanOrEqual(4 - 1e-6);
  });
});
