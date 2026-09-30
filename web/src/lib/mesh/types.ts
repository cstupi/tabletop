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
