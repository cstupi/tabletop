# Task 8: Mesh pipeline II — decimation, creased normals, GLB out and in

**Files:**
- Create: `web/src/lib/mesh/decimate.ts`, `web/src/lib/mesh/normals.ts`, `web/src/lib/mesh/glb.ts`
- Test: `web/tests/decimate.test.ts`, `web/tests/glb.test.ts`

**Interfaces:**
- Consumes: `IndexedMesh`, `weld`, `parseStl`; `MeshoptSimplifier` from `meshoptimizer` (`ready: Promise<void>`, `simplify(indices, positions, 3, targetIndexCount, targetError, flags?) : [Uint32Array, number]`, `compactMesh(indices) : [Uint32Array remap, number uniqueVertices]`); `Document`, `WebIO` from `@gltf-transform/core`.
- Produces: `BUDGETS = { miniature: 15_000, terrain: 50_000 }`; `decimate(mesh, budgetTriangles): Promise<IndexedMesh>` (unchanged when already within budget; otherwise the simplifier's error is widened stepwise — 0.5%, 1%, 2%, 5%, 10%, 25%, 50% of the mesh extent — until the result fits, then the mesh is compacted); `creasedNormals(mesh, angleDegrees = 60): IndexedMesh` (splits vertices at creases sharper than the angle; returns positions, indices and normals); `exportGlb(mesh, name): Promise<Uint8Array>` (one mesh, one primitive, POSITION + NORMAL + indices, a default material, no textures); `readGlb(bytes): Promise<{ positions: Float32Array; triangles: number }>` (every primitive's POSITION concatenated in node-world space, and the triangle count over every mesh, the same sum management computes).
- Decisions: `meshoptimizer` is the WebAssembly simplifier (mesh conversion → Deliberately not decided; chosen because it embeds its WASM in the JS module so it loads under Node and in a Worker with no asset plumbing, and its error metric is the quality knob the spec names); `@gltf-transform/core` is the exporter (a `Document` → `writeBinary` is a valid GLB by construction, and the same library reads dropped GLBs for the preview count and the bounding box). The crease angle defaults to 60° (mesh conversion → Deliberately not decided: "tuned against the test STLs"); it is a parameter, not a constant.

Spec: mesh conversion → What conversion does ("Decimate to the budget for the chosen kind: 15,000 triangles for a miniature, 50,000 for terrain … Quality is the simplifier's own error metric"; "Recompute smooth normals with an angle threshold"; "Emit GLB: one file, meshes and normals only. No textures, no materials beyond a default, no rigging").

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/decimate.test.ts
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
    expect(triangleCount(dense)).toBe(48_000);
    const out = await decimate(dense, BUDGETS.miniature);
    expect(triangleCount(out)).toBeLessThanOrEqual(BUDGETS.miniature);
    expect(triangleCount(out)).toBeGreaterThan(BUDGETS.miniature / 2);
    const b = bounds(out.positions);
    expect(b.max[1]! - b.min[1]!).toBeCloseTo(40, 0);
    expect(out.positions.length / 3).toBeLessThan(dense.positions.length / 3);          // compacted
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
});
```

```ts
// web/tests/glb.test.ts
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { exportGlb, readGlb } from '../src/lib/mesh/glb';
import { creasedNormals } from '../src/lib/mesh/normals';
import { parseStl } from '../src/lib/mesh/stl';
import { bounds } from '../src/lib/mesh/types';
import { weld } from '../src/lib/mesh/weld';

describe('glb', () => {
  it('writes a GLB that reads back with the same triangles and extent', async () => {
    const b = readFileSync('tests/fixtures/cylinder-binary.stl');
    const mesh = creasedNormals(weld(parseStl(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength))));
    const glb = await exportGlb(mesh, 'cylinder');
    expect(new TextDecoder().decode(glb.subarray(0, 4))).toBe('glTF');
    expect(new DataView(glb.buffer, glb.byteOffset).getUint32(4, true)).toBe(2);
    const json = JSON.parse(new TextDecoder().decode(glb.subarray(20, 20 + new DataView(glb.buffer, glb.byteOffset).getUint32(12, true))));
    expect(json.meshes[0].primitives[0].attributes).toHaveProperty('NORMAL');
    expect(json.images).toBeUndefined();
    const back = await readGlb(glb);
    expect(back.triangles).toBe(256);
    expect(bounds(back.positions)).toEqual(bounds(mesh.positions));
  });
});
```

- [ ] **Step 2: Run; expect failures. Write `decimate.ts`:**

```ts
// web/src/lib/mesh/decimate.ts — meshoptimizer's simplifier, widened until the budget is met.
import { MeshoptSimplifier } from 'meshoptimizer';
import type { IndexedMesh } from './types';
import { triangleCount } from './types';

export const BUDGETS = { miniature: 15_000, terrain: 50_000 } as const;
const ERRORS = [0.005, 0.01, 0.02, 0.05, 0.1, 0.25, 0.5];

export async function decimate(mesh: IndexedMesh, budgetTriangles: number): Promise<IndexedMesh> {
  if (triangleCount(mesh) <= budgetTriangles) return mesh;
  await MeshoptSimplifier.ready;
  let best = mesh.indices;
  for (const error of ERRORS) {
    const [indices] = MeshoptSimplifier.simplify(mesh.indices, mesh.positions, 3, budgetTriangles * 3, error);
    best = indices;
    if (indices.length / 3 <= budgetTriangles) break;
  }
  return compact({ positions: mesh.positions, indices: best });
}

/** Drops vertices no triangle references any more. */
export function compact(mesh: IndexedMesh): IndexedMesh {
  const [remap, unique] = MeshoptSimplifier.compactMesh(mesh.indices);
  const positions = new Float32Array(unique * 3);
  for (let v = 0; v < mesh.positions.length / 3; v++) {
    const to = remap[v]!;
    if (to === 0xffffffff) continue;
    positions.set(mesh.positions.subarray(v * 3, v * 3 + 3), to * 3);
  }
  const indices = new Uint32Array(mesh.indices.length);
  for (let i = 0; i < indices.length; i++) indices[i] = remap[mesh.indices[i]!]!;
  return { positions, indices };
}
```

- [ ] **Step 3: Write `normals.ts`**

```ts
// web/src/lib/mesh/normals.ts — per-corner normals averaged over the incident faces within the crease angle, then re-welded on (position, normal).
import type { IndexedMesh } from './types';

export function creasedNormals(mesh: IndexedMesh, angleDegrees = 60): IndexedMesh {
  const { positions, indices } = mesh;
  const faceCount = indices.length / 3, faceNormals = new Float32Array(faceCount * 3);
  const incident: number[][] = Array.from({ length: positions.length / 3 }, () => []);
  for (let f = 0; f < faceCount; f++) {
    const [a, b, c] = [indices[f * 3]!, indices[f * 3 + 1]!, indices[f * 3 + 2]!];
    const ax = positions[a * 3]!, ay = positions[a * 3 + 1]!, az = positions[a * 3 + 2]!;
    const ux = positions[b * 3]! - ax, uy = positions[b * 3 + 1]! - ay, uz = positions[b * 3 + 2]! - az;
    const vx = positions[c * 3]! - ax, vy = positions[c * 3 + 1]! - ay, vz = positions[c * 3 + 2]! - az;
    let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    const len = Math.hypot(nx, ny, nz) || 1; nx /= len; ny /= len; nz /= len;
    faceNormals.set([nx, ny, nz], f * 3);
    incident[a]!.push(f); incident[b]!.push(f); incident[c]!.push(f);
  }
  const cosThreshold = Math.cos((angleDegrees * Math.PI) / 180);
  const outPositions: number[] = [], outNormals: number[] = [], outIndices = new Uint32Array(indices.length);
  const seen = new Map<string, number>();
  for (let f = 0; f < faceCount; f++) for (let k = 0; k < 3; k++) {
    const v = indices[f * 3 + k]!;
    let nx = 0, ny = 0, nz = 0;
    for (const g of incident[v]!) {
      const dot = faceNormals[f * 3]! * faceNormals[g * 3]! + faceNormals[f * 3 + 1]! * faceNormals[g * 3 + 1]! + faceNormals[f * 3 + 2]! * faceNormals[g * 3 + 2]!;
      if (dot >= cosThreshold) { nx += faceNormals[g * 3]!; ny += faceNormals[g * 3 + 1]!; nz += faceNormals[g * 3 + 2]!; }
    }
    const len = Math.hypot(nx, ny, nz) || 1; nx /= len; ny /= len; nz /= len;
    const key = `${v}|${nx.toFixed(4)}|${ny.toFixed(4)}|${nz.toFixed(4)}`;
    let idx = seen.get(key);
    if (idx === undefined) { idx = outPositions.length / 3; seen.set(key, idx); outPositions.push(positions[v * 3]!, positions[v * 3 + 1]!, positions[v * 3 + 2]!); outNormals.push(nx, ny, nz); }
    outIndices[f * 3 + k] = idx;
  }
  return { positions: Float32Array.from(outPositions), normals: Float32Array.from(outNormals), indices: outIndices };
}
```

- [ ] **Step 4: Write `glb.ts`**

```ts
// web/src/lib/mesh/glb.ts — one mesh, one primitive, positions + normals + indices, a default material. Read back for previews and bounds.
import { Document, WebIO } from '@gltf-transform/core';
import type { IndexedMesh } from './types';

const io = new WebIO();

export async function exportGlb(mesh: IndexedMesh, name: string): Promise<Uint8Array> {
  if (!mesh.normals) throw new Error('exportGlb needs normals; run creasedNormals first.');
  const doc = new Document();
  const buffer = doc.createBuffer();
  const position = doc.createAccessor('POSITION').setType('VEC3').setArray(mesh.positions).setBuffer(buffer);
  const normal = doc.createAccessor('NORMAL').setType('VEC3').setArray(mesh.normals).setBuffer(buffer);
  const indices = doc.createAccessor('indices').setType('SCALAR').setArray(mesh.indices).setBuffer(buffer);
  const material = doc.createMaterial('default').setBaseColorFactor([0.8, 0.8, 0.8, 1]).setMetallicFactor(0).setRoughnessFactor(0.8);
  const primitive = doc.createPrimitive().setAttribute('POSITION', position).setAttribute('NORMAL', normal).setIndices(indices).setMaterial(material);
  const node = doc.createNode(name).setMesh(doc.createMesh(name).addPrimitive(primitive));
  doc.createScene(name).addChild(node);
  doc.getRoot().getAsset().generator = 'tabletop-web mesh conversion';
  return io.writeBinary(doc);
}

/** Every primitive's positions in world space, and the triangle total management's header read will report. */
export async function readGlb(bytes: Uint8Array): Promise<{ positions: Float32Array; triangles: number }> {
  const doc = await io.readBinary(bytes);
  const chunks: Float32Array[] = [];
  let triangles = 0;
  for (const mesh of doc.getRoot().listMeshes()) for (const prim of mesh.listPrimitives()) {
    const count = (prim.getIndices() ?? prim.getAttribute('POSITION'))?.getCount() ?? 0;
    const mode = prim.getMode();
    triangles += mode === 4 ? Math.floor(count / 3) : mode === 5 || mode === 6 ? Math.max(0, count - 2) : 0;
  }
  for (const node of doc.getRoot().listNodes()) {
    const mesh = node.getMesh(); if (!mesh) continue;
    const m = node.getWorldMatrix();
    for (const prim of mesh.listPrimitives()) {
      const src = prim.getAttribute('POSITION')?.getArray() as Float32Array | null; if (!src) continue;
      const out = new Float32Array(src.length);
      for (let i = 0; i < src.length; i += 3) {
        const x = src[i]!, y = src[i + 1]!, z = src[i + 2]!;
        out[i] = m[0]! * x + m[4]! * y + m[8]! * z + m[12]!; out[i + 1] = m[1]! * x + m[5]! * y + m[9]! * z + m[13]!; out[i + 2] = m[2]! * x + m[6]! * y + m[10]! * z + m[14]!;
      }
      chunks.push(out);
    }
  }
  const positions = new Float32Array(chunks.reduce((n, c) => n + c.length, 0));
  let o = 0; for (const c of chunks) { positions.set(c, o); o += c.length; }
  return { positions, triangles };
}
```

- [ ] **Step 5: Run `npm test`; expect 23 passed (the dense decimation takes a few seconds under Node). Commit: `Decimate with meshoptimizer, recompute creased normals, and write and read GLB`.**
