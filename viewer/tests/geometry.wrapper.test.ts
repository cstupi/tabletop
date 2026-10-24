import { beforeAll, describe, expect, it } from 'vitest';
import { loadGeometry, placed, wallsOf } from '../src/lib/geometry';
import type { Geometry, TerrainPlacement } from '../src/lib/geometry';
import { loadTestGeometry } from './geometry';

const circle = (d: number) => ({ shape: 'circle', a: d, b: d }) as const;
const at = (x: number, y: number, z = 0) => ({ position: { x, y, z }, yaw: { degrees: 0 } });
const model = (x: number, y: number, d = 1) => placed({ base: circle(d), height: 1.5 }, at(x, y));
const solid = (id: string, x: number, y: number, top: number, declarations: string): TerrainPlacement => ({
  id: id.replace("-0000-000", "-0001-000"), mesh: { builtin: 'block' }, position: { x, y, z: 0 }, yaw: { degrees: 0 }, scale: 1,
  volumes: [{ id, volume: { kind: 'solid', footprint: { kind: 'rect', a: 4, b: 4 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top, declarations, notes: null } }],
} as unknown as TerrainPlacement);

const ID = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe('the typed Geometry', () => {
  it('restModel without a level takes the highest open surface', () => {
    expect(g.restModel(model(10, 10), [solid(ID(1), 10, 10, 3, 'none')])).toBeCloseTo(3, 9);
  });

  it('contacts returns a Map keyed by model id', () => {
    const unitA = ID(100), unitB = ID(101);
    const mover = { id: ID(10), unitId: unitA, body: { base: circle(1), height: 1.5 }, at: at(10, 10) };
    const other = { id: ID(11), unitId: unitB, body: { base: circle(1), height: 1.5 }, at: at(30, 30) };
    const found = g.contacts([mover, other], new Map([[mover.id, at(30.5, 30)]]), [], { passesModels: true, landing: false });
    expect(found.get(mover.id)?.kind).toBe('on_base');
    expect(found.get(mover.id)?.unitId).toBe(unitB);
    expect(found.has(other.id)).toBe(false);
  });

  it('nearestPair of an empty list is null', () => {
    expect(g.nearestPair([], [model(1, 1)])).toBeNull();
    expect(g.nearestPair([model(1, 1)], [])).toBeNull();
    expect(g.nearestPair([model(0, 0), model(20, 0)], [model(23, 0)])).toMatchObject({ from: 1, to: 0 });
  });

  it('wallsOf picks Impassable volumes, or the listed ids inside an Ask', () => {
    const wall = solid(ID(1), 10, 10, 3, 'impassable'), hill = solid(ID(2), 30, 10, 3, 'none');
    expect(wallsOf([wall, hill])).toEqual([ID(1)]);
    expect(wallsOf([wall, hill], [ID(2)])).toEqual([ID(2)]);
  });

  it('landModels lands beside an Impassable solid', () => {
    const wall = solid(ID(1), 10, 10, 3, 'impassable');
    const [end] = g.landModels([{ id: ID(5), at: model(2, 2), to: { x: 10, y: 10 } }], [], wallsOf([wall]), [wall]);
    expect(end!.lands.x).toBeCloseTo(12.5, 9);
    expect(end!.lands.y).toBeCloseTo(10, 9);
    expect(end!.lands.z).toBeCloseTo(0, 9);
    expect(end!.drawn.z).toBeCloseTo(3.5, 9);
  });

  it('loadGeometry loads once', async () => {
    expect(await loadTestGeometry()).toBe(g);
  });

  it('a bad request throws an Error', () => {
    const bad = { body: { base: { shape: 'hexagon', a: 1, b: 1 }, height: 1 }, placement: at(0, 0) };
    expect(() => g.restModel(bad as never, [])).toThrow(Error);
  });
});

describe('loadGeometry', () => {
  it('forgets a failed load, so the next call retries', async () => {
    const missing = 'file:///nonexistent/_framework/';
    await expect(loadGeometry(missing)).rejects.toThrow();
    await expect(loadGeometry(missing)).rejects.toThrow();
  });
});
