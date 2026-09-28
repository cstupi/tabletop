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
    // glTF requires unit normals. A zero-area triangle has a (0,0,0) face normal, which fails its own crease test and
    // excludes itself, leaving nothing to normalise; +Z is arbitrary but valid on a face with no area to shade.
    const len = Math.hypot(nx, ny, nz);
    if (len > 0) { nx /= len; ny /= len; nz /= len; } else { nx = 0; ny = 0; nz = 1; }
    // Math.round, not toFixed: toFixed renders -0.00001 as "-0.0000" and +0.00001 as "0.0000", splitting one vertex
    // into two across zero. String(-0) is "0", so an integer key has no such band.
    const key = `${v}|${Math.round(nx * 1e4)}|${Math.round(ny * 1e4)}|${Math.round(nz * 1e4)}`;
    let idx = seen.get(key);
    if (idx === undefined) { idx = outPositions.length / 3; seen.set(key, idx); outPositions.push(positions[v * 3]!, positions[v * 3 + 1]!, positions[v * 3 + 2]!); outNormals.push(nx, ny, nz); }
    outIndices[f * 3 + k] = idx;
  }
  return { positions: Float32Array.from(outPositions), normals: Float32Array.from(outNormals), indices: outIndices };
}
