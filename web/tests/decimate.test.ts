import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { BUDGETS, decimate } from '../src/lib/mesh/decimate';
import { creasedNormals } from '../src/lib/mesh/normals';
import { parseStl } from '../src/lib/mesh/stl';
import { bounds, triangleCount } from '../src/lib/mesh/types';
import { weld } from '../src/lib/mesh/weld';

const load = (name: string) => { const b = readFileSync(`tests/fixtures/${name}`); return weld(parseStl(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength))); };

describe('decimate', () => {
  it('brings a dense mesh within the miniature budget and keeps its extent', async () => {
    const dense = load('dense-binary.stl');
    expect(triangleCount(dense)).toBe(47_800);   // 48,000 in the file; welding the north-pole fan drops its 200 degenerate triangles
    const out = await decimate(dense, BUDGETS.miniature);
    expect(triangleCount(out)).toBeLessThanOrEqual(BUDGETS.miniature);
    expect(triangleCount(out)).toBeGreaterThan(BUDGETS.miniature / 2);
    const b = bounds(out.positions);
    expect(b.max[1]! - b.min[1]!).toBeCloseTo(40, 0);
    expect(out.positions.length / 3).toBeLessThan(dense.positions.length / 3);          // compacted
  });
  it('keeps every decimated index inside the vertex buffer, through creasedNormals', async () => {
    const out = await decimate(load('dense-binary.stl'), BUDGETS.miniature);
    const vertices = out.positions.length / 3;
    for (const i of out.indices) expect(i).toBeLessThan(vertices);
    const shaded = creasedNormals(out, 60);
    expect(triangleCount(shaded)).toBeLessThanOrEqual(BUDGETS.miniature);
    for (const i of shaded.indices) expect(i).toBeLessThan(shaded.positions.length / 3);
  });
  it('keeps every surviving vertex at its own position when the highest-indexed vertices are collapsed away', async () => {
    const n = 21, pos: number[] = [];
    for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) pos.push(x, y, 0);
    const swap = (a: number, b: number) => { for (let k = 0; k < 3; k++) { const t = pos[a * 3 + k]!; pos[a * 3 + k] = pos[b * 3 + k]!; pos[b * 3 + k] = t; } };
    const last = n * n - 1, centre = 10 * n + 10;
    swap(last, centre);                                                    // the interior centre now has the highest index; the corner has the centre's old one
    const at = new Map(pos.map((_, i) => i).filter((i) => i % 3 === 0).map((i) => [`${pos[i]},${pos[i + 1]}`, i / 3]));
    const idx: number[] = [];
    for (let y = 0; y < n - 1; y++) for (let x = 0; x < n - 1; x++) {
      const a = at.get(`${x},${y}`)!, b = at.get(`${x + 1},${y}`)!, c = at.get(`${x + 1},${y + 1}`)!, d = at.get(`${x},${y + 1}`)!;
      idx.push(a, b, c, a, c, d);
    }
    const input = { positions: Float32Array.from(pos), indices: Uint32Array.from(idx) };
    const out = await decimate(input, 50);
    expect(triangleCount(out)).toBeLessThanOrEqual(50);
    const original = new Set<string>(); for (let v = 0; v < n * n; v++) original.add(`${pos[v * 3]},${pos[v * 3 + 1]},${pos[v * 3 + 2]}`);
    const kept = new Set<string>();
    for (const i of out.indices) kept.add(`${out.positions[i * 3]},${out.positions[i * 3 + 1]},${out.positions[i * 3 + 2]}`);
    for (const k of kept) expect(original.has(k)).toBe(true);
    const b = bounds(out.positions);
    expect([b.min[0], b.min[1], b.max[0], b.max[1]]).toEqual([0, 0, n - 1, n - 1]);   // all four corners survive at their own positions
    for (const corner of ['0,0,0', `${n - 1},0,0`, `0,${n - 1},0`, `${n - 1},${n - 1},0`]) expect(kept.has(corner)).toBe(true);
  });
  it('leaves a mesh under budget alone', async () => {
    const cube = load('cube-ascii.stl');
    expect(await decimate(cube, BUDGETS.terrain)).toBe(cube);
  });
});

describe('creasedNormals', () => {
  it('keeps a cube faceted and a cylinder wall smooth', () => {
    const cube = creasedNormals(load('cube-ascii.stl'), 60);
    expect(cube.positions.length / 3).toBe(24);                        // 8 corners × 3 faces
    expect(cube.normals!.length).toBe(cube.positions.length);
    const cyl = creasedNormals(load('cylinder-binary.stl'), 60);
    const wallVertices = cyl.positions.length / 3;
    expect(wallVertices).toBeLessThan(256 * 3);                        // shared across the 5.6° wall facets
    for (let i = 0; i < cyl.normals!.length; i += 3) {
      const n = Math.hypot(cyl.normals![i]!, cyl.normals![i + 1]!, cyl.normals![i + 2]!);
      expect(n).toBeCloseTo(1, 5);
    }
  });

  it('gives a unit normal on a zero-area triangle', () => {
    const flat = { positions: Float32Array.from([0, 0, 0, 1, 0, 0, 2, 0, 0]), indices: Uint32Array.from([0, 1, 2]) };
    const out = creasedNormals(flat, 60);
    for (let i = 0; i < out.normals!.length; i += 3) {
      expect(Math.hypot(out.normals![i]!, out.normals![i + 1]!, out.normals![i + 2]!)).toBeCloseTo(1, 5);
    }
  });
});
