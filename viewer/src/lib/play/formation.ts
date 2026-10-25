// viewer/src/lib/play/formation.ts
// client/src/Core/Formation.cs: a held unit arranged in rows around a point. Rows run across the facing, the front row toward it; the
// models split as evenly as they go, the extra ones in the front rows (10 in 3 rows is 4-3-3), in input order, front row first.
// Neighbours in a row sit GAP apart edge to edge, and each row sits back from the one in front until its closest pair across the two is
// GAP apart, so every model is within 1" of another and the rows form one chain (p.7, unit coherency).

import type { Base, Geometry, PlacedModel, Point3, XY, Yaw } from '../geometry';
import { rotate } from '../board/transform';

/** Edge-to-edge spacing, inside p.7's 1" with room for a dragged unit's rounding. */
export const GAP = 0.5;

export function arrange(g: Geometry, bases: readonly Base[], rows: number, centre: Point3, facing: Yaw): Point3[] {
  const n = bases.length;
  if (n === 0) return [];
  rows = Math.min(Math.max(rows, 1), n);
  // The local frame faces +Y (yaw 0): a row runs along X, the front row is at Y = 0 and later rows step back toward -Y.
  const local: { x: number; y: number }[] = Array.from({ length: n }, () => ({ x: 0, y: 0 }));
  let ahead: number[] = [];
  let line = 0, first = 0;
  for (let r = 0; r < rows; r++) {
    const count = Math.floor(n / rows) + (r < n % rows ? 1 : 0);
    const row = Array.from({ length: count }, (_, k) => first + k);
    first += count;
    let x = -(row.reduce((s, i) => s + bases[i]!.b, 0) + (row.length - 1) * GAP) / 2;     // b is the width across the facing
    for (const i of row) { local[i] = { x: x + bases[i]!.b / 2, y: 0 }; x += bases[i]!.b + GAP; }
    if (r > 0) line = behind(g, bases, local, ahead, row, line);
    for (const i of row) local[i] = { x: local[i]!.x, y: line };
    ahead = row;
  }
  const front = Math.max(...local.map((p, i) => p.y + bases[i]!.a / 2));                  // a is the length along the facing
  const back = Math.min(...local.map((p, i) => p.y - bases[i]!.a / 2));
  const middle = { x: 0, y: (front + back) / 2 };
  return local.map((p) => rotate({ x: p.x - middle.x, y: p.y - middle.y }, facing.degrees))
    .map((p) => ({ x: centre.x + p.x, y: centre.y + p.y, z: centre.z }));
}

/** The line behind `line` at which the closest pair across the two rows is GAP apart. The closest distance only grows as the row steps
 *  back, so a bisection between touching and a full row depth behind finds it. */
function behind(g: Geometry, bases: readonly Base[], local: readonly XY[], ahead: readonly number[], row: readonly number[], line: number): number {
  const closest = (y: number): number => {
    let best = Infinity;
    for (const i of ahead) for (const j of row)
      best = Math.min(best, g.edgeToEdge(at(bases[i]!, { x: local[i]!.x, y: line }), at(bases[j]!, { x: local[j]!.x, y })));
    return best;
  };
  let near = 0, far = Math.max(...ahead.map((i) => bases[i]!.a)) / 2 + GAP + Math.max(...row.map((j) => bases[j]!.a)) / 2;
  for (let k = 0; k < 40; k++) {
    const step = (near + far) / 2;
    if (closest(line - step) < GAP) near = step; else far = step;
  }
  return line - far;
}

const at = (b: Base, p: XY): PlacedModel => ({ body: { base: b, height: 1 }, placement: { position: { x: p.x, y: p.y, z: 0 }, yaw: { degrees: 0 } } });
