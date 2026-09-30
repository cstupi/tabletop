// web/tests/stl.test.ts
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { bounds, triangleCount } from '../src/lib/mesh/types';
import { mergeSoups, parseStl, StlError } from '../src/lib/mesh/stl';
import { weld } from '../src/lib/mesh/weld';

const load = (name: string) => { const b = readFileSync(`tests/fixtures/${name}`); return b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength); };

describe('parseStl', () => {
  it('reads ASCII', () => {
    const soup = parseStl(load('cube-ascii.stl'));
    expect(soup.length).toBe(12 * 9);
    expect(bounds(soup)).toEqual({ min: [0, 0, 0], max: [20, 20, 20] });
  });
  it('reads binary, even when the header says solid', () => {
    const buf = load('cylinder-binary.stl');
    expect(parseStl(buf).length).toBe(256 * 9);
    const lying = new Uint8Array(buf.slice(0)); lying.set(new TextEncoder().encode('solid but binary'), 0);
    expect(parseStl(lying.buffer).length).toBe(256 * 9);
  });
  it('rejects garbage', () => {
    expect(() => parseStl(new TextEncoder().encode('hello').buffer)).toThrow(StlError);
  });
  it('rejects a vertex that is not a number', () => {
    const bad = 'solid x\nfacet normal 0 0 0\nouter loop\nvertex 1.2.3 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid x\n';
    expect(() => parseStl(new TextEncoder().encode(bad).buffer as ArrayBuffer)).toThrow(StlError);
  });
});

describe('mergeSoups and weld', () => {
  it('concatenates parts as they come', () => {
    const merged = mergeSoups([parseStl(load('kit-body.stl')), parseStl(load('kit-arm.stl'))]);
    expect(merged.length).toBe((32 * 4 + 12) * 9);
    expect(bounds(merged).max[0]).toBe(20);      // the arm reaches x = 14 + 6
  });
  it('welds shared corners and drops degenerate triangles', () => {
    const cube = weld(parseStl(load('cube-ascii.stl')));
    expect(cube.positions.length).toBe(8 * 3);
    expect(triangleCount(cube)).toBe(12);
    const soup = new Float32Array([0, 0, 0, 1, 0, 0, 1, 0, 0]);   // two identical corners
    expect(triangleCount(weld(soup))).toBe(0);
  });
  it('welds a fan apex written as negative zero', () => {
    const fan = weld(new Float32Array([-0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, -1, 0, 0]));
    expect(fan.positions.length / 3).toBe(4);
    expect(triangleCount(fan)).toBe(2);
  });
});
