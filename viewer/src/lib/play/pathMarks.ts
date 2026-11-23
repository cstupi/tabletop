// viewer/src/lib/play/pathMarks.ts
// client/src/Core/PathMarks.cs: the marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and
// lines… Vehicles should show treads for their path"; "a line of a pair of footprints" per model). A model leaves one trail of prints alternating
// left and right a stride apart, GAIT either side of the route, stride, gait and print size scaled with the base (sizeOf), following the measured
// route and turning in place at each bend and where the model pivots at either end; a vehicle leaves two
// tracks of tread links a TREAD_PITCH apart at the base's width; a mark on a stretch where the base touches dangerous ground is dangerous.
// Inches; the shapes are drawn about the mark, +Y along its facing.

import type { Base } from '../protocol/wire.generated';
import type { PathStretch, XY } from '../geometry';

export type MarkKind = 'foot' | 'tread';
/** One mark: where it stands, its facing in degrees (yaw 0 faces +Y, counter-clockwise), a left print or on the left track, dangerous, its
 *  station, the walked length along the route, and its size, how many times the shape's own size it draws. */
export interface PathMark {
  readonly kind: MarkKind; readonly at: XY; readonly facing: number; readonly left: boolean; readonly dangerous: boolean; readonly station: number;
  readonly size: number;
}

/** An infantryman's (a 32 mm base, REFERENCE across) prints STRIDE apart, GAIT either side of the route. */
export const STRIDE = 0.4, GAIT = 0.07, REFERENCE = 32 / 25.4;
export const TREAD_PITCH = 0.22, TRACK_WIDTH = 0.32;

/** A base's print size against an infantryman's: the square root of its mean width over REFERENCE, held to 0.8–2. */
export const sizeOf = (b: Base): number => Math.min(2, Math.max(0.8, Math.sqrt(Math.sqrt(b.a * b.b) / REFERENCE)));
export const strideOf = (b: Base): number => STRIDE * sizeOf(b);
export const gaitOf = (b: Base): number => GAIT * sizeOf(b);

const oval = (c: XY, rx: number, ry: number, n: number): XY[] =>
  Array.from({ length: n }, (_, i) => { const t = (2 * Math.PI * i) / n; return { x: c.x + rx * Math.cos(t), y: c.y + ry * Math.sin(t) }; });
/** A right boot print as two outlines, sole then heel, toe along +Y; a left print is it mirrored in X. */
export const BOOT: readonly (readonly XY[])[] = [oval({ x: 0.004, y: 0.05 }, 0.055, 0.1, 12), oval({ x: 0, y: -0.1 }, 0.045, 0.05, 10)];
/** One tread link: a bar across the track with a chevron's point along the facing. */
export const TREAD_LINK: readonly XY[] = [
  { x: -TRACK_WIDTH / 2, y: -0.06 }, { x: 0, y: -0.02 }, { x: TRACK_WIDTH / 2, y: -0.06 }, { x: TRACK_WIDTH / 2, y: 0.02 }, { x: 0, y: 0.06 }, { x: -TRACK_WIDTH / 2, y: 0.02 },
];

const rotate = (p: XY, degrees: number): XY => {
  const r = (degrees * Math.PI) / 180, c = Math.cos(r), s = Math.sin(r);
  return { x: p.x * c - p.y * s, y: p.x * s + p.y * c };
};

/** A vehicle's two tracks' offsets across the path (+ to the left), their links inside its width. */
function tracks(w: number): number[] {
  const track = w / 2 - TRACK_WIDTH / 2;
  return track > TRACK_WIDTH / 2 ? [-track, track] : [-w / 4, w / 4];
}

const dangerousAt = (station: number, stretches: readonly PathStretch[]): boolean =>
  stretches.some((s) => station >= s.from - 1e-9 && station <= s.to + 1e-9);

/** A pivot or bend of feet: a print every TURN_STEP radians of it at most; a turn under PIVOT_MIN radians shows none. */
export const TURN_STEP = Math.PI / 4, PIVOT_MIN = Math.PI / 18;

/** The direction a facing in degrees points: 0 along +Y, counter-clockwise. */
const facingDir = (degrees: number): XY => { const r = (degrees * Math.PI) / 180; return { x: -Math.sin(r), y: Math.cos(r) }; };

/** A route's marks. Each leg's prints face along it; at a bend the feet turn in place at the corner and a vehicle's outer track swings round it;
 *  given the model's `start` and `end` facings in degrees, it pivots in place at either end where they differ from the route's. */
export function marksOf(route: readonly XY[], b: Base, stretches: readonly PathStretch[], vehicle: boolean, start?: number, end?: number): PathMark[] {
  const marks: PathMark[] = [];
  let walked = 0;
  // A vehicle's two tracks keep its gauge, the base's width, whichever way it goes; a model walks the route itself.
  const lanes = vehicle ? tracks(b.b) : [0];
  const step = vehicle ? TREAD_PITCH : strideOf(b);
  const size = vehicle ? 1 : sizeOf(b);
  const gait = gaitOf(b);
  const next = lanes.map(() => ({ station: step / 2, count: 0 }));
  const mark = (on: XY, dir: XY, lane: number, count: number, station: number): PathMark => {
    const isLeft = vehicle ? lane > 0 : count % 2 === 0;
    const side = vehicle ? lane : isLeft ? gait : -gait;
    return {
      kind: vehicle ? 'tread' : 'foot', at: { x: on.x - dir.y * side, y: on.y + dir.x * side },
      facing: (Math.atan2(-dir.x, dir.y) * 180) / Math.PI, left: isLeft, dangerous: dangerousAt(station, stretches), station, size,
    };
  };
  // Turning in place at `at` from one direction to another: the start's pivot lays its first facing and stops short of the route's, a bend lays
  // only the facings between, the end's pivot runs on to the end's facing.
  const pivot = (at: XY, from: XY, to: XY, station: number, first: boolean, last = true): void => {
    const turn = Math.atan2(from.x * to.y - from.y * to.x, from.x * to.x + from.y * to.y);
    if (Math.abs(turn) < PIVOT_MIN) return;
    for (let j = 0; j < lanes.length; j++) {
      const n = vehicle ? Math.max(1, Math.ceil(Math.abs(turn * lanes[j]!) / TREAD_PITCH)) : Math.ceil(Math.abs(turn) / TURN_STEP);
      const [lo, hi] = first ? [0, n - 1] : last ? [1, n] : [1, n - 1];
      for (let k = lo; k <= hi; k++) marks.push(mark(at, rotate(from, ((turn * k) / n) * 180 / Math.PI), lanes[j]!, next[j]!.count++, station));
    }
  };
  let before: XY | null = null;
  for (let i = 1; i < route.length; i++) {
    const from = route[i - 1]!, to = route[i]!;
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    if (length <= 1e-9) continue;
    const dir = { x: (to.x - from.x) / length, y: (to.y - from.y) / length };
    if (before === null && start !== undefined) pivot(from, facingDir(start), dir, walked, true);
    if (before !== null && !vehicle) pivot(from, before, dir, walked, false, false);
    for (let j = 0; j < lanes.length; j++) {
      let { station, count } = next[j]!;
      // At a corner a track on the outside of the turn swings round it, so its links run on without a gap.
      if (before !== null && vehicle) {
        const turn = Math.atan2(before.x * dir.y - before.y * dir.x, before.x * dir.x + before.y * dir.y);
        const arcs = lanes[j]! * turn < 0 ? Math.floor(Math.abs(lanes[j]! * turn) / step) : 0;
        for (let k = 1; k <= arcs; k++, count++) marks.push(mark(from, rotate(before, ((turn * k) / (arcs + 1)) * 180 / Math.PI), lanes[j]!, count, walked));
      }
      for (; station <= walked + length + 1e-9; station += step, count++)
        marks.push(mark({ x: from.x + dir.x * (station - walked), y: from.y + dir.y * (station - walked) }, dir, lanes[j]!, count, station));
      next[j] = { station, count };
    }
    before = dir;
    walked += length;
  }
  if (before !== null && end !== undefined) pivot(route[route.length - 1]!, before, facingDir(end), walked, false);
  return marks;
}

/** A shape's outlines placed at a mark: turned to its facing, a left print mirrored, `scale` times its size (the rim under it is larger) times
 *  the mark's own size. */
export function placed(m: PathMark, scale = 1): XY[][] {
  const s = scale * m.size;
  const mirror = m.kind === 'foot' && m.left ? -s : s;
  const parts = m.kind === 'foot' ? BOOT : [TREAD_LINK];
  return parts.map((part) => part.map((p) => { const q = rotate({ x: p.x * mirror, y: p.y * s }, m.facing); return { x: q.x + m.at.x, y: q.y + m.at.y }; }));
}
