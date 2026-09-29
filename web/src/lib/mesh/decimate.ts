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

/** Drops vertices no triangle references any more. Rewrites `mesh.indices` in place (meshoptimizer's compactMesh does). */
export function compact(mesh: IndexedMesh): IndexedMesh {
  const [remap, unique] = MeshoptSimplifier.compactMesh(mesh.indices);
  const positions = new Float32Array(unique * 3);
  for (let v = 0; v < mesh.positions.length / 3; v++) {
    const to = remap[v];
    // remap only covers 0..maxindex, so a vertex above the highest surviving index reads undefined, which is as unreferenced as 0xffffffff.
    if (to === undefined || to === 0xffffffff) continue;
    positions.set(mesh.positions.subarray(v * 3, v * 3 + 3), to * 3);
  }
  // compactMesh has already rewritten mesh.indices in place through the remap; remapping them again would index the remap with
  // new ids (unreferenced vertices map to 0xffffffff), so the caller's array is the result.
  return { positions, indices: mesh.indices };
}
