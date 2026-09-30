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
