// web/src/lib/mesh/glb.ts — one mesh, one primitive, positions + normals + indices, a default material. Read back for previews and bounds.
import { Document, WebIO } from '@gltf-transform/core';
import type { IndexedMesh } from './types';

const io = new WebIO();

export async function exportGlb(mesh: IndexedMesh, name: string): Promise<Uint8Array> {
  if (!mesh.normals) throw new Error('exportGlb needs normals; run creasedNormals first.');
  const doc = new Document();
  const buffer = doc.createBuffer();
  // TypeScript 5.7 made the typed arrays generic in their backing buffer: a bare Float32Array widens to
  // Float32Array<ArrayBufferLike>, while gltf-transform's TypedArray is over ArrayBuffer. Every IndexedMesh here is
  // built with Float32Array.from or new Float32Array(n), so the backing buffer is never shared.
  const position = doc.createAccessor('POSITION').setType('VEC3').setArray(mesh.positions as Float32Array<ArrayBuffer>).setBuffer(buffer);
  const normal = doc.createAccessor('NORMAL').setType('VEC3').setArray(mesh.normals as Float32Array<ArrayBuffer>).setBuffer(buffer);
  const indices = doc.createAccessor('indices').setType('SCALAR').setArray(mesh.indices as Uint32Array<ArrayBuffer>).setBuffer(buffer);
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
