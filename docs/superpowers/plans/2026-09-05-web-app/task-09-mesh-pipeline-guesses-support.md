# Task 9: Mesh pipeline III — the guesses, the support warning, the bounding-box volume, and `convert()`

**Files:**
- Create: `web/src/lib/mesh/guess.ts`, `web/src/lib/mesh/supports.ts`, `web/src/lib/mesh/bounds.ts`, `web/src/lib/mesh/convert.ts`, `web/src/lib/mesh/convert.worker.ts`
- Test: `web/tests/guess.test.ts`, `web/tests/convert.test.ts`

**Interfaces:**
- Consumes: everything Tasks 7–8 produce; `Orientation`, `Volume` from `api/types.ts`; `defaultVolume`, `mmToInches`.
- Produces: `UpAxis = 'y' | 'z'`; `guessUpAxis(bounds): UpAxis`; `Proposal { scale: number; orientation: Orientation; upAxis: UpAxis; unitsGuess: 'mm' }`; `propose(bounds): Proposal` (`scale = 1/25.4`; Z-up → identity orientation; Y-up → `{ pitch: 90, yaw: 0, roll: 0 }`); `worldExtent(bounds, proposal): { width: number; length: number; height: number; min: [number, number, number]; max: [number, number, number] }` (inches, in the table frame); `recentre(mesh, upAxis): IndexedMesh` (translates in place so the footprint is centred on the origin and the lowest point along the up-axis is at zero — mesh conversion → What conversion does, *Centre*); `detectSupports(mesh): SupportReport { suspected: boolean; components: number; supportLike: number }`; `terrainVolume(bounds, { scale, orientation }): Volume` (the bounding box in the table frame: rect footprint, `bottom 0`, `top` the height, `kind: solid`; the caller passes the *confirmed* scale and orientation, not the proposal); `ConversionInput { kind: 'miniature' | 'terrain'; budget?: number; creaseAngle?: number; files: { name: string; bytes: ArrayBuffer }[] }`; `ConversionResult { glb: Uint8Array; trianglesBefore: number; trianglesAfter: number; bounds: Bounds; proposal: Proposal; supports: SupportReport; volume: Volume }`; `convert(input, onProgress?): Promise<ConversionResult>`; `convertInWorker(input, onProgress?): Promise<ConversionResult>` (same result, off the main thread); `describeGlb(bytes): Promise<{ triangles: number; bounds: Bounds }>` for a dropped GLB.
- Orientation convention (fixed by the contracts plan on `Tabletop.Geometry.Orientation`, Task 2 — "rotated Pitch about X, then Roll about Y, then Yaw about Z (right-handed, extrinsic), then scaled; a Y-up glTF stands at Pitch = 90" — and applied by the client's `MeshLoader.Orient`): `Orientation` is applied to mesh coordinates as a rotation of `pitch` degrees about the table's X axis, then `roll` about Y, then `yaw` about Z, right-handed. A Y-up STL stood upright is therefore `pitch = 90` (Y → Z). `worldExtent` applies the same rotation so the preview, the volume and the footprint agree.
- The up-axis rule, verbatim from the spec: whichever of Y or Z "gives a footprint that fits a base and a height taller than it is wide"; when both or neither qualify, Z wins because slicers export Z-up.
- Support heuristic (mesh conversion → "Many thin, tall, disconnected components at the bottom of the mesh"): connected components by shared vertices; a component is support-like when its horizontal extent is under 6% of the model's, it is at least 3× taller than wide, and its lowest point is within 2% of the model's height from the bottom; `suspected` when at least 6 such components exist. Tuned against `supported-binary.stl` (16 pillars) and the four clean fixtures.

Spec: mesh conversion → What conversion does ("Guess units and up-axis … The guess becomes the proposed scale and orientation"; "Warn about pre-supported files … No support removal, ever"), What it writes, What it does not do ("The browser computes the bounding-box volume from the mesh it already holds — a converted STL or a dropped GLB alike"); geometry values → Asset scale and orientation, Where volumes come from; geometry plan → Coordinate frame (Z up).

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/guess.test.ts
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { guessUpAxis, propose, worldExtent } from '../src/lib/mesh/guess';
import { detectSupports } from '../src/lib/mesh/supports';
import { terrainVolume } from '../src/lib/mesh/bounds';
import { parseStl } from '../src/lib/mesh/stl';
import { bounds } from '../src/lib/mesh/types';
import { weld } from '../src/lib/mesh/weld';

const load = (name: string) => { const b = readFileSync(`tests/fixtures/${name}`); return weld(parseStl(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength))); };

describe('guesses', () => {
  it('picks the axis along which the model is taller than wide, Z when in doubt', () => {
    expect(guessUpAxis(bounds(load('cylinder-binary.stl').positions))).toBe('z');   // 20 wide, 40 tall along Z
    expect(guessUpAxis(bounds(load('dense-binary.stl').positions))).toBe('y');      // 16 wide, 40 tall along Y
    expect(guessUpAxis(bounds(load('cube-ascii.stl').positions))).toBe('z');        // a cube: neither
  });
  it('proposes millimetres and the rotation that stands the model up', () => {
    const z = propose(bounds(load('cylinder-binary.stl').positions));
    expect(z).toEqual({ scale: 1 / 25.4, orientation: { pitch: 0, yaw: 0, roll: 0 }, upAxis: 'z', unitsGuess: 'mm' });
    const y = propose(bounds(load('dense-binary.stl').positions));
    expect(y.orientation).toEqual({ pitch: 90, yaw: 0, roll: 0 });
    const ext = worldExtent(bounds(load('dense-binary.stl').positions), y);
    expect(ext.height).toBeCloseTo(40 / 25.4, 3);
    expect(ext.width).toBeCloseTo(16 / 25.4, 3);
  });
  it('writes the bounding-box volume in inches, bottom 0, solid', () => {
    const b = bounds(load('cube-ascii.stl').positions);
    expect(terrainVolume(b, propose(b))).toEqual({ kind: 'solid', footprint: { kind: 'rect', a: 20 / 25.4, b: 20 / 25.4 }, offset: { x: 0, y: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 20 / 25.4, declarations: 'none' });
  });
});

describe('detectSupports', () => {
  it('fires on the pillared fixture and on nothing else', () => {
    const r = detectSupports(load('supported-binary.stl'));
    expect(r.components).toBe(17);
    expect(r.supportLike).toBe(16);
    expect(r.suspected).toBe(true);
    for (const f of ['cube-ascii.stl', 'cylinder-binary.stl', 'dense-binary.stl']) expect(detectSupports(load(f)).suspected).toBe(false);
  });
});
```

```ts
// web/tests/convert.test.ts
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { convert, describeGlb } from '../src/lib/mesh/convert';
import { BUDGETS } from '../src/lib/mesh/decimate';

const file = (name: string) => { const b = readFileSync(name); return { name, bytes: b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength) }; };

describe('convert', () => {
  it('merges a kit, meets the budget, carries normals, guesses, warns, and describes its own GLB', async () => {
    const steps: string[] = [];
    const r = await convert({ kind: 'miniature', files: [file('tests/fixtures/kit-body.stl'), file('tests/fixtures/kit-arm.stl')] }, (s) => steps.push(s.step));
    expect(r.trianglesBefore).toBe(140);
    expect(r.trianglesAfter).toBe(140);
    expect(r.proposal.upAxis).toBe('z');
    expect(r.supports.suspected).toBe(false);
    expect(steps).toEqual(['parse', 'weld', 'decimate', 'normals', 'export', 'analyse']);
    const d = await describeGlb(r.glb);
    expect(d.triangles).toBe(140);
    expect(d.bounds.max[0]).toBeCloseTo(14, 4);      // centred: the kit spanned x = -8..20
    expect(d.bounds.min[2]).toBeCloseTo(0, 4);       // standing on z = 0
  });
  it('decimates a dense terrain piece to its budget and flags supports on the supported one', async () => {
    const dense = await convert({ kind: 'terrain', files: [file('tests/fixtures/dense-binary.stl')] });
    expect(dense.trianglesBefore).toBe(48_000);
    expect(dense.trianglesAfter).toBeLessThanOrEqual(BUDGETS.terrain);
    expect(dense.volume.top).toBeCloseTo(40 / 25.4, 3);
    const supported = await convert({ kind: 'miniature', files: [file('tests/fixtures/supported-binary.stl')] });
    expect(supported.supports.suspected).toBe(true);
  });
  const real = 'tests/fixtures/real';
  it.skipIf(!existsSync(real))('converts every real STL dropped in tests/fixtures/real within budget', async () => {
    for (const name of readdirSync(real).filter((n) => n.toLowerCase().endsWith('.stl'))) {
      const r = await convert({ kind: 'miniature', files: [file(`${real}/${name}`)] });
      expect(r.trianglesAfter, name).toBeLessThanOrEqual(BUDGETS.miniature);
      expect(r.glb.byteLength, name).toBeGreaterThan(0);
    }
  });
});
```

- [ ] **Step 2: Run; expect failures. Write `guess.ts` and `bounds.ts`:**

```ts
// web/src/lib/mesh/guess.ts — STL has no units and no up-axis; propose millimetres and the axis along which the model is tall.
import type { Orientation } from '$lib/api/types';
import { mmToInches } from '$lib/units/units';
import { type Bounds, type IndexedMesh, bounds } from './types';

export type UpAxis = 'y' | 'z';
export interface Proposal { scale: number; orientation: Orientation; upAxis: UpAxis; unitsGuess: 'mm' }

const size = (b: Bounds, k: 0 | 1 | 2) => b.max[k]! - b.min[k]!;

export function guessUpAxis(b: Bounds): UpAxis {
  const [sx, sy, sz] = [size(b, 0), size(b, 1), size(b, 2)];
  const tallY = sy > Math.max(sx, sz), tallZ = sz > Math.max(sx, sy);
  return tallY && !tallZ ? 'y' : 'z';
}

export function propose(b: Bounds): Proposal {
  const upAxis = guessUpAxis(b);
  return { scale: mmToInches(1), orientation: upAxis === 'y' ? { pitch: 90, yaw: 0, roll: 0 } : { pitch: 0, yaw: 0, roll: 0 }, upAxis, unitsGuess: 'mm' };
}

/** Centres the footprint on the origin and puts the lowest point at zero, in mesh space, so the model stands on its base at the origin. */
export function recentre(mesh: IndexedMesh, up: UpAxis): IndexedMesh {
  const b = bounds(mesh.positions), upIndex = up === 'y' ? 1 : 2;
  const shift = [0, 1, 2].map((k) => (k === upIndex ? b.min[k]! : (b.min[k]! + b.max[k]!) / 2));
  for (let i = 0; i < mesh.positions.length; i += 3) for (let k = 0; k < 3; k++) mesh.positions[i + k] = mesh.positions[i + k]! - shift[k]!;
  return mesh;
}

/** Rotates a mesh-space point into the table frame: pitch about X, then roll about Y, then yaw about Z (degrees, right-handed), then scales. */
export function toWorld([x, y, z]: [number, number, number], p: { scale: number; orientation: Orientation }): [number, number, number] {
  const r = (d: number) => (d * Math.PI) / 180;
  const [cp, sp, cr, sr, cy, sy] = [Math.cos(r(p.orientation.pitch)), Math.sin(r(p.orientation.pitch)), Math.cos(r(p.orientation.roll)), Math.sin(r(p.orientation.roll)), Math.cos(r(p.orientation.yaw)), Math.sin(r(p.orientation.yaw))];
  let [px, py, pz] = [x, y * cp - z * sp, y * sp + z * cp];                 // pitch about X
  [px, py, pz] = [px * cr + pz * sr, py, -px * sr + pz * cr];               // roll about Y
  [px, py, pz] = [px * cy - py * sy, px * sy + py * cy, pz];                // yaw about Z
  return [px * p.scale, py * p.scale, pz * p.scale];
}

/** The model's extent in the table frame, from the eight corners of its mesh-space bounds. */
export function worldExtent(b: Bounds, p: { scale: number; orientation: Orientation }): { width: number; length: number; height: number; min: [number, number, number]; max: [number, number, number] } {
  const min: [number, number, number] = [Infinity, Infinity, Infinity], max: [number, number, number] = [-Infinity, -Infinity, -Infinity];
  for (const x of [b.min[0]!, b.max[0]!]) for (const y of [b.min[1]!, b.max[1]!]) for (const z of [b.min[2]!, b.max[2]!]) {
    const w = toWorld([x, y, z], p);
    for (let k = 0; k < 3; k++) { min[k] = Math.min(min[k]!, w[k]!) ; max[k] = Math.max(max[k]!, w[k]!); }
  }
  return { width: max[0] - min[0], length: max[1] - min[1], height: max[2] - min[2], min, max };
}
```

```ts
// web/src/lib/mesh/bounds.ts — every terrain asset gets its bounding box as one solid prism at confirm.
import type { Volume } from '$lib/api/types';
import { defaultVolume } from '$lib/units/volumes';
import type { Proposal } from './guess';
import { worldExtent } from './guess';
import type { Bounds } from './types';

export function terrainVolume(b: Bounds, p: Pick<Proposal, 'scale' | 'orientation'>): Volume {
  const e = worldExtent(b, p);
  return defaultVolume(e.width, e.length, e.height);
}
```

- [ ] **Step 3: Write `supports.ts`**

```ts
// web/src/lib/mesh/supports.ts — the signature of print supports: many thin, tall, disconnected components standing on the bottom. Detected, never removed.
import { guessUpAxis } from './guess';
import type { IndexedMesh } from './types';
import { bounds } from './types';

export interface SupportReport { suspected: boolean; components: number; supportLike: number }

export function detectSupports(mesh: IndexedMesh, { thinFraction = 0.06, tallRatio = 3, floorFraction = 0.02, minCount = 6 } = {}): SupportReport {
  const n = mesh.positions.length / 3, parent = new Uint32Array(n).map((_, i) => i);
  const find = (i: number): number => { while (parent[i] !== i) { parent[i] = parent[parent[i]!]!; i = parent[i]!; } return i; };
  const union = (a: number, b: number) => { a = find(a); b = find(b); if (a !== b) parent[a] = b; };
  for (let t = 0; t < mesh.indices.length; t += 3) { union(mesh.indices[t]!, mesh.indices[t + 1]!); union(mesh.indices[t]!, mesh.indices[t + 2]!); }
  const all = bounds(mesh.positions), up = guessUpAxis(all) === 'y' ? 1 : 2, h = ([0, 1, 2] as const).filter((k) => k !== up);
  const modelHeight = all.max[up]! - all.min[up]!, modelWidth = Math.max(all.max[h[0]!]! - all.min[h[0]!]!, all.max[h[1]!]! - all.min[h[1]!]!);
  const comp = new Map<number, { min: [number, number, number]; max: [number, number, number] }>();
  for (let v = 0; v < n; v++) {
    const root = find(v); let c = comp.get(root);
    if (!c) comp.set(root, (c = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] }));
    for (let k = 0; k < 3; k++) { const x = mesh.positions[v * 3 + k]!; if (x < c.min[k]!) c.min[k] = x; if (x > c.max[k]!) c.max[k] = x; }
  }
  let supportLike = 0;
  for (const c of comp.values()) {
    const width = Math.max(c.max[h[0]!]! - c.min[h[0]!]!, c.max[h[1]!]! - c.min[h[1]!]!), height = c.max[up]! - c.min[up]!;
    if (width <= thinFraction * modelWidth && height >= tallRatio * width && c.min[up]! - all.min[up]! <= floorFraction * modelHeight) supportLike++;
  }
  return { suspected: supportLike >= minCount, components: comp.size, supportLike };
}
```

- [ ] **Step 4: Write `convert.ts` and the worker**

```ts
// web/src/lib/mesh/convert.ts — the pipeline, in the spec's order: parse, merge, weld, decimate, normals, export, then the analysis the preview needs.
import type { Volume } from '$lib/api/types';
import { terrainVolume } from './bounds';
import { BUDGETS, decimate } from './decimate';
import { exportGlb, readGlb } from './glb';
import { type Proposal, guessUpAxis, propose, recentre } from './guess';
import { creasedNormals } from './normals';
import { mergeSoups, parseStl } from './stl';
import { type SupportReport, detectSupports } from './supports';
import { type Bounds, bounds, triangleCount } from './types';
import { weld } from './weld';

export interface ConversionInput { kind: 'miniature' | 'terrain'; budget?: number; creaseAngle?: number; files: { name: string; bytes: ArrayBuffer }[] }
export interface ConversionResult { glb: Uint8Array; trianglesBefore: number; trianglesAfter: number; bounds: Bounds; proposal: Proposal; supports: SupportReport; volume: Volume }
export type Progress = { step: 'parse' | 'weld' | 'decimate' | 'normals' | 'export' | 'analyse' };

export async function convert(input: ConversionInput, onProgress: (p: Progress) => void = () => {}): Promise<ConversionResult> {
  onProgress({ step: 'parse' });
  const soup = mergeSoups(input.files.map((f) => parseStl(f.bytes)));
  onProgress({ step: 'weld' });                                    // weld, then centre on the footprint (mesh conversion → What conversion does: Centre)
  const raw = weld(soup);
  const welded = recentre(raw, guessUpAxis(bounds(raw.positions)));
  const trianglesBefore = triangleCount(welded);
  const supports = detectSupports(welded);                       // on the full mesh: decimation can merge thin pillars
  onProgress({ step: 'decimate' });
  const decimated = await decimate(welded, input.budget ?? BUDGETS[input.kind]);
  onProgress({ step: 'normals' });
  const shaded = creasedNormals(decimated, input.creaseAngle ?? 60);
  onProgress({ step: 'export' });
  const glb = await exportGlb(shaded, input.files[0]?.name.replace(/\.stl$/i, '') ?? 'mesh');
  onProgress({ step: 'analyse' });
  const b = bounds(shaded.positions), proposal = propose(b);
  return { glb, trianglesBefore, trianglesAfter: triangleCount(shaded), bounds: b, proposal, supports, volume: terrainVolume(b, proposal) };
}

/** A dropped GLB skips conversion; the preview still needs its count and extent, and terrain still needs its volume. */
export async function describeGlb(bytes: Uint8Array): Promise<{ triangles: number; bounds: Bounds }> {
  const { positions, triangles } = await readGlb(bytes);
  return { triangles, bounds: bounds(positions) };
}

export function convertInWorker(input: ConversionInput, onProgress: (p: Progress) => void = () => {}): Promise<ConversionResult> {
  return new Promise((resolve, reject) => {
    const worker = new Worker(new URL('./convert.worker.ts', import.meta.url), { type: 'module' });
    worker.onmessage = (e: MessageEvent<{ progress?: Progress; result?: ConversionResult; error?: string }>) => {
      if (e.data.progress) onProgress(e.data.progress);
      if (e.data.result) { resolve(e.data.result); worker.terminate(); }
      if (e.data.error) { reject(new Error(e.data.error)); worker.terminate(); }
    };
    worker.postMessage(input, input.files.map((f) => f.bytes));
  });
}
```

```ts
// web/src/lib/mesh/convert.worker.ts
import { convert, type ConversionInput } from './convert';
self.onmessage = async (e: MessageEvent<ConversionInput>) => {
  try {
    const result = await convert(e.data, (progress) => self.postMessage({ progress }));
    self.postMessage({ result }, [result.glb.buffer as ArrayBuffer]);
  } catch (err) { self.postMessage({ error: err instanceof Error ? err.message : String(err) }); }
};
```

- [ ] **Step 5: Run `npm test`; expect 29 passed, 1 skipped (the real-STL test). If a real pre-supported STL is available on the Windows host, copy it to `tests/fixtures/real/` and confirm `detectSupports(...).suspected` on it by adding one line to the loop; adjust `thinFraction`/`minCount` only if it misses. Commit: `Guess units and up-axis, warn about supports, compute the bounding-box volume, and assemble convert()`.**
