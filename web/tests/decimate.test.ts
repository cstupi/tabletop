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
