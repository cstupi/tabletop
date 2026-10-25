import { beforeAll, describe, expect, it } from 'vitest';
import { rotate } from '../src/lib/board/transform';
import type { Base, Geometry, PlacedModel, Point3 } from '../src/lib/geometry';
import { GAP, arrange } from '../src/lib/play/formation';
import { loadTestGeometry } from './geometry';

const circle = (mm: number): Base => ({ shape: 'circle', a: mm / 25.4, b: mm / 25.4 });
const mm25 = circle(25), mm32 = circle(32), mm60 = circle(60), mm100 = circle(100);
const oval: Base = { shape: 'oval', a: 75 / 25.4, b: 42 / 25.4 };
const centre: Point3 = { x: 30, y: 20, z: 1.5 };
const many = (b: Base, n: number): Base[] => Array.from({ length: n }, () => b);
const yaw = (degrees: number) => ({ degrees });

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

const placed = (bases: Base[], at: Point3[], facing: number): PlacedModel[] =>
  bases.map((b, i) => ({ body: { base: b, height: 1 }, placement: { position: at[i]!, yaw: yaw(facing) } }));

/** Row sizes front to back: models grouped by their distance along the facing, the front (largest) first. */
function rowSizes(at: Point3[], facing: number): number[] {
  const rows = new Map<number, number>();
  for (const p of at) {
    const y = Math.round(rotate({ x: p.x - centre.x, y: p.y - centre.y }, -facing).y * 1000) / 1000;
    rows.set(y, (rows.get(y) ?? 0) + 1);
  }
  return [...rows].sort((a, b) => b[0] - a[0]).map(([, n]) => n);
}

describe('Formation', () => {
  it('ten models in three rows split four, three, three, front row first', () => {
    const at = arrange(g, many(mm32, 10), 3, centre, yaw(0));
    expect(rowSizes(at, 0)).toEqual([4, 3, 3]);
    for (const p of at.slice(0, 4)) expect(p.y).toBeCloseTo(at[0]!.y, 6);
    expect(at[0]!.y).toBeGreaterThan(at[4]!.y);
  });

  it('rows run across the facing', () => {
    const at = arrange(g, many(mm32, 10), 3, centre, yaw(90));
    expect(rowSizes(at, 90)).toEqual([4, 3, 3]);
    for (const p of at.slice(0, 4)) expect(p.x).toBeCloseTo(at[0]!.x, 6);
    expect(at[0]!.x).toBeLessThan(at[4]!.x);
  });

  it('the formation is centred on the point at its height', () => {
    const at = arrange(g, many(mm32, 10), 3, centre, yaw(0));
    const r = mm32.a / 2;
    expect((Math.min(...at.map((p) => p.x)) - r + Math.max(...at.map((p) => p.x)) + r) / 2).toBeCloseTo(centre.x, 6);
    expect((Math.min(...at.map((p) => p.y)) - r + Math.max(...at.map((p) => p.y)) + r) / 2).toBeCloseTo(centre.y, 6);
    for (const p of at) expect(p.z).toBe(centre.z);
  });

  it('neighbours in a row sit one gap apart', () => {
    const bases = many(mm32, 5);
    const ps = placed(bases, arrange(g, bases, 1, centre, yaw(30)), 30);
    for (let i = 0; i + 1 < ps.length; i++) expect(g.edgeToEdge(ps[i]!, ps[i + 1]!)).toBeCloseTo(GAP, 6);
  });

  it('rows clamp to one and to the model count', () => {
    expect(rowSizes(arrange(g, many(mm25, 3), 0, centre, yaw(0)), 0)).toEqual([3]);
    expect(rowSizes(arrange(g, many(mm25, 3), 5, centre, yaw(0)), 0)).toEqual([1, 1, 1]);
    expect(arrange(g, [], 3, centre, yaw(0))).toEqual([]);
  });

  const units: [string, Base[], number, number][] = [
    ['10x32', many(mm32, 10), 1, 0], ['10x32', many(mm32, 10), 3, 37], ['10x32', many(mm32, 10), 5, 210],
    ['leader60+9x25', [mm60, ...many(mm25, 9)], 2, 15], ['leader60+9x25', [mm60, ...many(mm25, 9)], 5, 15],
    ['9x25+leader60', [...many(mm25, 9), mm60], 3, 15], ['5x100', many(mm100, 5), 3, 45], ['6xoval', many(oval, 6), 3, 12],
  ];
  it.each(units)('%s in %i rows at %i degrees: no bases closer than the gap, each within 1" of another', (_n, bases, rows, facing) => {
    const ps = placed(bases, arrange(g, bases, rows, centre, yaw(facing)), facing);
    for (let i = 0; i < ps.length; i++) {
      let nearest = Infinity;
      for (let j = 0; j < ps.length; j++) {
        if (i === j) continue;
        const d = g.edgeToEdge(ps[i]!, ps[j]!);
        if (j > i) expect(d).toBeGreaterThanOrEqual(GAP - 1e-6);
        nearest = Math.min(nearest, d);
      }
      expect(nearest).toBeLessThanOrEqual(1);
    }
  });
});
