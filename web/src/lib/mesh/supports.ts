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
    // Positive extent first: an orphan vertex left by a dropped degenerate triangle has width 0 and height 0, which
    // passes both `width <= thinFraction * modelWidth` and `height >= tallRatio * width` on any model at all.
    if (width > 0 && height > 0 && width <= thinFraction * modelWidth && height >= tallRatio * width && c.min[up]! - all.min[up]! <= floorFraction * modelHeight) supportLike++;
  }
  return { suspected: supportLike >= minCount, components: comp.size, supportLike };
}
