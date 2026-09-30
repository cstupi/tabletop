# Task 7: Mesh pipeline I — STL parsing, merging, welding, and the test fixtures

**Files:**
- Create: `web/src/lib/mesh/types.ts`, `web/src/lib/mesh/stl.ts`, `web/src/lib/mesh/weld.ts`, `web/tests/fixtures/make-fixtures.ts`
- Test: `web/tests/stl.test.ts`

**Interfaces:**
- Produces: `IndexedMesh { positions: Float32Array; indices: Uint32Array; normals?: Float32Array }`, `Bounds { min: Vec3; max: Vec3 }`, `Vec3 = [number, number, number]`, `triangleCount(mesh)`, `bounds(positions)`; `parseStl(buffer: ArrayBuffer): Float32Array` (triangle soup, 9 floats per triangle, ASCII or binary), `StlError` (also thrown for an ASCII vertex value that parses to a non-finite number, such as `1.2.3`); `mergeSoups(parts: Float32Array[]): Float32Array`; `weld(soup: Float32Array): IndexedMesh` (exact-position welding on the float bits, `-0` canonicalised to `+0` before hashing so a fan apex or pole is one vertex, degenerate triangles dropped); fixtures `tests/fixtures/{cube-ascii,cylinder-binary,kit-body,kit-arm,supported-binary,dense-binary}.stl` written by `npm run fixtures` (deterministic, committed).
- Decision — the pipeline's own STL reader rather than three's `STLLoader`: the format is 80 bytes + count + 50 bytes per facet, or `vertex x y z` lines, and reading it here keeps `three` out of the headless pipeline (mesh conversion → "plain JavaScript over typed arrays and runs headless under Node"). Binary detection is by length (`84 + 50·count === byteLength`) before looking for `solid`, because some binary exporters write `solid` in the header.
- Fixtures are synthetic because the spec's "small set of real STLs" cannot be committed by this plan; each is shaped to exercise one behaviour (ASCII, binary, a two-part kit, a supported piece, a dense Y-up piece). Real files dropped into `tests/fixtures/real/` are picked up by Task 9's tests when present.

Spec: mesh conversion → What conversion does ("Parse ASCII and binary STL"; "Merge several files into one mesh, positioned as they come … no fitting is attempted"), Testing.

- [ ] **Step 1: Write the fixture generator and run it**

```ts
// web/tests/fixtures/make-fixtures.ts — deterministic STLs. Units are millimetres as slicers export them.
import { mkdirSync, writeFileSync } from 'node:fs';
type V = [number, number, number];
const tris: V[][] = [];
function quad(a: V, b: V, c: V, d: V) { tris.push([a, b, c], [a, c, d]); }
function box(cx: number, cy: number, cz: number, sx: number, sy: number, sz: number) {
  const [x0, x1, y0, y1, z0, z1] = [cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, cz - sz / 2, cz + sz / 2] as const;
  quad([x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]); quad([x0, y0, z1], [x0, y1, z1], [x1, y1, z1], [x1, y0, z1]);
  quad([x0, y0, z0], [x0, y0, z1], [x1, y0, z1], [x1, y0, z0]); quad([x1, y1, z0], [x1, y1, z1], [x0, y1, z1], [x0, y1, z0]);
  quad([x0, y0, z0], [x0, y1, z0], [x0, y1, z1], [x0, y0, z1]); quad([x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0]);
}
/** A cylinder along `up` (2 = Z, 1 = Y), base at 0. */
function cylinder(r: number, h: number, segments: number, up: 1 | 2) {
  const P = (a: number, z: number, rr = r): V => { const x = rr * Math.cos(a), y = rr * Math.sin(a); return up === 2 ? [x, y, z] : [x, z, -y]; };
  for (let i = 0; i < segments; i++) {
    const a0 = (2 * Math.PI * i) / segments, a1 = (2 * Math.PI * (i + 1)) / segments;
    quad(P(a0, 0), P(a1, 0), P(a1, h), P(a0, h));
    tris.push([P(a0, 0, 0), P(a1, 0), P(a0, 0)], [P(a0, h, 0), P(a0, h), P(a1, h)]);
  }
}
function ellipsoid(rx: number, ry: number, rz: number, rings: number, segs: number) {
  const P = (t: number, p: number): V => [rx * Math.sin(t) * Math.cos(p), ry * Math.cos(t), rz * Math.sin(t) * Math.sin(p)];
  for (let i = 0; i < rings; i++) for (let j = 0; j < segs; j++) {
    const t0 = (Math.PI * i) / rings, t1 = (Math.PI * (i + 1)) / rings, p0 = (2 * Math.PI * j) / segs, p1 = (2 * Math.PI * (j + 1)) / segs;
    quad(P(t0, p0), P(t1, p0), P(t1, p1), P(t0, p1));
  }
}
function binary(): Buffer {
  const buf = Buffer.alloc(84 + 50 * tris.length); buf.write('binary stl fixture', 0, 'latin1'); buf.writeUInt32LE(tris.length, 80);
  tris.forEach((t, i) => { const o = 84 + 50 * i; buf.fill(0, o, o + 12); t.forEach((v, k) => v.forEach((c, m) => buf.writeFloatLE(c, o + 12 + 12 * k + 4 * m))); buf.writeUInt16LE(0, o + 48); });
  return buf;
}
function ascii(): string {
  return `solid cube\n${tris.map((t) => `facet normal 0 0 0\nouter loop\n${t.map((v) => `vertex ${v.join(' ')}`).join('\n')}\nendloop\nendfacet`).join('\n')}\nendsolid cube\n`;
}
function emit(name: string, body: Buffer | string) { writeFileSync(`tests/fixtures/${name}`, body); tris.length = 0; }

mkdirSync('tests/fixtures', { recursive: true });
box(10, 10, 10, 20, 20, 20);                          emit('cube-ascii.stl', ascii());                 // 12 triangles, 0..20 mm
cylinder(10, 40, 64, 2);                              emit('cylinder-binary.stl', binary());           // 256 triangles, Z up, taller than wide
cylinder(8, 30, 32, 2);                               emit('kit-body.stl', binary());
box(14, 0, 22, 12, 4, 4);                             emit('kit-arm.stl', binary());                   // sits beside the body, already placed
box(0, 0, 35, 20, 20, 10); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) box(-7.5 + 5 * i, -7.5 + 5 * j, 15, 0.8, 0.8, 30);
                                                      emit('supported-binary.stl', binary());          // a blob on sixteen 0.8 mm pillars
ellipsoid(8, 20, 8, 120, 200);                        emit('dense-binary.stl', binary());              // 48,000 triangles, Y up
console.log('fixtures written');
```

Run: `cd web && node tests/fixtures/make-fixtures.ts && ls -la tests/fixtures` — six `.stl` files; `dense-binary.stl` is `84 + 50 × 48000 = 2,400,084` bytes. Welding drops the 200 degenerate triangles of its north-pole fan, so every count taken after `weld` is 47,800.

- [ ] **Step 2: Write the failing tests**

```ts
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
```

- [ ] **Step 3: Run; expect failures. Write `types.ts`, `stl.ts`, `weld.ts`:**

```ts
// web/src/lib/mesh/types.ts
export type Vec3 = [number, number, number];
export interface IndexedMesh { positions: Float32Array; indices: Uint32Array; normals?: Float32Array }
export interface Bounds { min: Vec3; max: Vec3 }
export const triangleCount = (m: IndexedMesh) => m.indices.length / 3;
export function bounds(positions: Float32Array): Bounds {
  const min: Vec3 = [Infinity, Infinity, Infinity], max: Vec3 = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < positions.length; i += 3) for (let k = 0; k < 3; k++) { const v = positions[i + k]!; if (v < min[k]!) min[k] = v; if (v > max[k]!) max[k] = v; }
  return { min, max };
}
```

```ts
// web/src/lib/mesh/stl.ts — STL in, triangle soup out (9 floats per triangle). Per-face normals are discarded; Task 8 recomputes them.
export class StlError extends Error {}

export function parseStl(buffer: ArrayBuffer): Float32Array {
  if (buffer.byteLength >= 84) {
    const count = new DataView(buffer).getUint32(80, true);
    if (84 + 50 * count === buffer.byteLength) return parseBinary(buffer, count);
  }
  const text = new TextDecoder('latin1').decode(new Uint8Array(buffer, 0, Math.min(buffer.byteLength, 512)));
  if (/^\s*solid/.test(text)) return parseAscii(new TextDecoder().decode(buffer));
  throw new StlError('Not an STL file: neither a binary facet table nor an ASCII "solid".');
}

function parseBinary(buffer: ArrayBuffer, count: number): Float32Array {
  const view = new DataView(buffer), out = new Float32Array(count * 9);
  for (let i = 0; i < count; i++) {
    const o = 84 + 50 * i + 12;                                   // skip the facet normal
    for (let k = 0; k < 9; k++) out[i * 9 + k] = view.getFloat32(o + 4 * k, true);
  }
  return out;
}

function parseAscii(text: string): Float32Array {
  const values: number[] = [];
  const re = /vertex\s+([-+.\deE]+)\s+([-+.\deE]+)\s+([-+.\deE]+)/g;
  for (let m = re.exec(text); m; m = re.exec(text)) values.push(Number(m[1]), Number(m[2]), Number(m[3]));
  if (values.length === 0 || values.length % 9 !== 0) throw new StlError('ASCII STL has no complete facets.');
  if (!values.every(Number.isFinite)) throw new StlError('ASCII STL has a vertex that is not a number.');
  return Float32Array.from(values);
}

/** Kits ship as parts already placed relative to each other; merging is concatenation and no fitting is attempted. */
export function mergeSoups(parts: Float32Array[]): Float32Array {
  const out = new Float32Array(parts.reduce((n, p) => n + p.length, 0));
  let o = 0; for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}
```

```ts
// web/src/lib/mesh/weld.ts — exact welding on the float bits with a spatial hash; STL exporters repeat corners bit-for-bit.
import type { IndexedMesh } from './types';

export function weld(soup: Float32Array): IndexedMesh {
  // -0 and +0 are one point but different bits, and the hash reads the bits: a fan apex written as `0 * cos(a)`
  // would otherwise land in two buckets and weld into two vertices.
  const clean = Float32Array.from(soup, (v) => (v === 0 ? 0 : v));
  const bits = new Uint32Array(clean.buffer, clean.byteOffset, clean.length);
  const buckets = new Map<number, number[]>();
  const positions: number[] = [], indices: number[] = [];
  const cornerCount = clean.length / 3;
  const remap = new Uint32Array(cornerCount);
  for (let c = 0; c < cornerCount; c++) {
    const x = bits[c * 3]!, y = bits[c * 3 + 1]!, z = bits[c * 3 + 2]!;
    const h = (Math.imul(x, 73856093) ^ Math.imul(y, 19349663) ^ Math.imul(z, 83492791)) >>> 0;
    let bucket = buckets.get(h);
    if (!bucket) buckets.set(h, (bucket = []));
    let found = -1;
    for (const v of bucket) if (positions[v * 3] === clean[c * 3] && positions[v * 3 + 1] === clean[c * 3 + 1] && positions[v * 3 + 2] === clean[c * 3 + 2]) { found = v; break; }
    if (found < 0) { found = positions.length / 3; positions.push(clean[c * 3]!, clean[c * 3 + 1]!, clean[c * 3 + 2]!); bucket.push(found); }
    remap[c] = found;
  }
  for (let t = 0; t < cornerCount; t += 3) {
    const a = remap[t]!, b = remap[t + 1]!, c = remap[t + 2]!;
    if (a !== b && b !== c && a !== c) indices.push(a, b, c);
  }
  return { positions: Float32Array.from(positions), indices: Uint32Array.from(indices) };
}
```

- [ ] **Step 4: Run `npm test`; expect 24 passed. Commit: `Parse, merge and weld STL meshes; add the synthetic fixtures`.**
