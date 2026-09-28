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

export function propose(b: Bounds, upAxis: UpAxis = guessUpAxis(b)): Proposal {
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
