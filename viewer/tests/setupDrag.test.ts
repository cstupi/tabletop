import { describe, expect, it } from 'vitest';
import type { Geometry, PieceEnd, XY } from '../src/lib/geometry';
import type { Placement, TableState, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import { SetupDrag } from '../src/lib/setup/drag';

const piece = (id: string, x: number, y: number): TerrainPlacement => ({
  id, mesh: { builtin: 'b' }, position: { x, y, z: 0 }, yaw: { degrees: 0 }, scale: 1, volumes: [],
});
const draft = (terrain: TerrainPlacement[]): TableState => ({
  id: 'd', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, terrain, zones: [],
  markers: [{ id: 'm1', kind: 'token', position: { x: 5, y: 6, z: 0 }, yaw: { degrees: 0 }, diameter: 1, visual: { builtin: 'chip' } }],
});

interface Call { terrain: readonly TerrainPlacement[]; id: string; at: Placement; to: XY }
function fake(lands: (to: XY) => PieceEnd = (to) => ({ drawn: { x: to.x, y: to.y, z: 0.5 }, lands: { x: to.x, y: to.y, z: 0 } })) {
  const calls: Call[] = [];
  const g = { landPiece: (terrain: readonly TerrainPlacement[], id: string, at: Placement, to: XY) => { calls.push({ terrain, id, at, to }); return lands(to); } } as unknown as Geometry;
  return { g, calls };
}

describe('SetupDrag', () => {
  it('start takes terrain and markers, not models', () => {
    const { g } = fake();
    const drag = new SetupDrag(g);
    const d = draft([piece('p1', 10, 10)]);
    expect(drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 11, y: 12 })).toBe(true);
    expect(drag.held).toMatchObject({ kind: 'terrain', id: 'p1', offset: { x: 1, y: 2 } });
    expect(drag.start(d, { kind: 'marker', id: 'm1' }, { x: 5, y: 6 })).toBe(true);
    expect(drag.start(d, { kind: 'model', id: 'x' }, { x: 5, y: 6 })).toBe(false);
    expect(drag.held).toBeNull();
    expect(drag.start(d, { kind: 'terrain', id: 'nope', volumeId: 'v' }, { x: 0, y: 0 })).toBe(false);
  });

  it('move lands through landPiece with the grab offset', () => {
    const { g, calls } = fake();
    const drag = new SetupDrag(g);
    const d = draft([piece('p1', 10, 10)]);
    drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 11, y: 12 });
    const held = drag.move(d, { x: 21, y: 32 });
    expect(calls[0]).toMatchObject({ id: 'p1', to: { x: 20, y: 30 } });
    expect(held).toMatchObject({ kind: 'terrain', end: { lands: { x: 20, y: 30, z: 0 } } });
  });

  it('turn re-lands at the new yaw', () => {
    const { g, calls } = fake();
    const drag = new SetupDrag(g);
    const d = draft([piece('p1', 10, 10)]);
    drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    drag.move(d, { x: 20, y: 20 });
    drag.turn(d, 15);
    drag.turn(d, -30);
    expect(calls.at(-2)!.at.yaw.degrees).toBe(15);
    expect(calls.at(-1)!.at.yaw.degrees).toBe(345);
    expect(calls.at(-1)!.to).toEqual({ x: 20, y: 20 });
  });

  it('the drop sends MoveTerrain at the landing', () => {
    const { g } = fake(() => ({ drawn: { x: 30, y: 30, z: 1 }, lands: { x: 31, y: 29, z: 2 } }));
    const drag = new SetupDrag(g);
    const d = draft([piece('p1', 10, 10)]);
    drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    drag.move(d, { x: 30, y: 30 });
    expect(drag.end(d)).toEqual({ kind: 'move_terrain', placementId: 'p1', position: { x: 31, y: 29, z: 2 }, yaw: { degrees: 0 } });
    expect(drag.held).toBeNull();
  });

  it('a marker drops on the table plane with MoveSetupMarker', () => {
    const { g, calls } = fake();
    const drag = new SetupDrag(g);
    const d = draft([]);
    drag.start(d, { kind: 'marker', id: 'm1' }, { x: 5.5, y: 6.5 });
    drag.move(d, { x: 20.5, y: 20.5 });
    expect(drag.end(d)).toEqual({ kind: 'move_setup_marker', markerId: 'm1', position: { x: 20, y: 20, z: 0 }, yaw: { degrees: 0 } });
    expect(calls).toHaveLength(0);
  });

  it('a still drop sends nothing', () => {
    const { g } = fake((to) => ({ drawn: { x: 10, y: 10, z: 0 }, lands: { x: 10, y: 10, z: 0 } }));
    const drag = new SetupDrag(g);
    const d = draft([piece('p1', 10, 10)]);
    drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    expect(drag.end(d)).toBeNull();
    drag.start(d, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    drag.move(d, { x: 10, y: 10 });
    expect(drag.end(d)).toBeNull();
  });

  it('a held piece that leaves the draft cancels the drag', () => {
    const { g } = fake();
    const drag = new SetupDrag(g);
    drag.start(draft([piece('p1', 10, 10)]), { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    expect(drag.move(draft([]), { x: 12, y: 12 })).toBeNull();
    expect(drag.held).toBeNull();
    drag.start(draft([piece('p1', 10, 10)]), { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    drag.move(draft([piece('p1', 10, 10)]), { x: 12, y: 12 });
    expect(drag.end(draft([]))).toBeNull();
  });

  it('the drop lands against the draft current at the drop', () => {
    const { g, calls } = fake();
    const drag = new SetupDrag(g);
    const a = draft([piece('p1', 10, 10), piece('p2', 40, 40)]);
    const b = draft([piece('p1', 10, 10), piece('p2', 15, 15)]);
    drag.start(a, { kind: 'terrain', id: 'p1', volumeId: 'v' }, { x: 10, y: 10 });
    drag.move(a, { x: 14, y: 14 });
    drag.end(b);
    expect(calls.at(-1)!.terrain).toBe(b.terrain);
  });
});
