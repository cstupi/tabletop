// viewer/src/lib/play/pathMarks.ts
// client/src/Core/PathMarks.cs: the marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and
// lines… Vehicles should show treads for their path"). Walkers spread across the base's swept width each print left and right a STRIDE apart; a
// vehicle leaves two tracks of tread links a TREAD_PITCH apart at the base's width; a mark on a stretch where the base touches dangerous ground is
// dangerous. Inches; the shapes are drawn about the mark, +Y along its facing.

import type { Base } from '../protocol/wire.generated';
import type { PathStretch, XY } from '../geometry';

export type MarkKind = 'foot' | 'tread';
/** One mark: where it stands, its facing in degrees (yaw 0 faces +Y, counter-clockwise), a left print or on the left track, dangerous, and its
 *  station, the walked length along the route. */
export interface PathMark { readonly kind: MarkKind; readonly at: XY; readonly facing: number; readonly left: boolean; readonly dangerous: boolean; readonly station: number }

export const STRIDE = 0.4, LANE = 0.45, GAIT = 0.07, INSET = 0.14;
export const TREAD_PITCH = 0.22, TRACK_WIDTH = 0.32;

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

/** The base's width across a direction of travel (a unit vector), the base at `yaw` degrees. */
export function width(b: Base, yaw: number, dir: XY): number {
  const n = { x: -dir.y, y: dir.x };
  const x = rotate({ x: 1, y: 0 }, yaw), y = rotate({ x: 0, y: 1 }, yaw);
  const u = (Math.abs(n.x * x.x + n.y * x.y) * b.b) / 2, v = (Math.abs(n.x * y.x + n.y * y.y) * b.a) / 2;
  return 2 * (b.shape === 'rect' ? u + v : Math.sqrt(u * u + v * v));
}

function lanes(w: number, vehicle: boolean): number[] {
  if (vehicle) {
    const track = w / 2 - TRACK_WIDTH / 2;
    return track > TRACK_WIDTH / 2 ? [-track, track] : [-w / 4, w / 4];
  }
  const usable = w - 2 * INSET;
  if (usable <= 1e-9) return [0];
  const count = 1 + Math.ceil(usable / LANE - 1e-9);
  return Array.from({ length: count }, (_, j) => -usable / 2 + (j * usable) / (count - 1));
}

const dangerousAt = (station: number, stretches: readonly PathStretch[]): boolean =>
  stretches.some((s) => station >= s.from - 1e-9 && station <= s.to + 1e-9);

/** A route's marks for a base at `yaw` degrees (the start's facing). */
export function marksOf(route: readonly XY[], b: Base, yaw: number, stretches: readonly PathStretch[], vehicle: boolean): PathMark[] {
  const marks: PathMark[] = [];
  let walked = 0;
  const next = new Map<number, { station: number; count: number }>();
  const step = vehicle ? TREAD_PITCH : STRIDE;
  const mark = (on: XY, dir: XY, lane: number, j: number, count: number, station: number): PathMark => {
    const isLeft = vehicle ? lane > 0 : (count + j) % 2 === 0;
    const side = vehicle ? lane : lane + (isLeft ? GAIT : -GAIT);
    return {
      kind: vehicle ? 'tread' : 'foot', at: { x: on.x - dir.y * side, y: on.y + dir.x * side },
      facing: (Math.atan2(-dir.x, dir.y) * 180) / Math.PI, left: isLeft, dangerous: dangerousAt(station, stretches), station,
    };
  };
  let before: XY | null = null;
  for (let i = 1; i < route.length; i++) {
    const from = route[i - 1]!, to = route[i]!;
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    if (length <= 1e-9) continue;
    const dir = { x: (to.x - from.x) / length, y: (to.y - from.y) / length };
    // Prints cover the swept width across this leg; a vehicle's tracks keep its gauge, the base's width, whichever way it goes.
    const ls = lanes(vehicle ? b.b : width(b, yaw, dir), vehicle);
    for (let j = 0; j < ls.length; j++) {
      let { station, count } = next.get(j) ?? { station: vehicle ? TREAD_PITCH / 2 : STRIDE / 2 + ((j % 2) * STRIDE) / 2, count: 0 };
      // At a corner a lane on the outside of the turn swings round it, so its marks run on without a gap.
      if (before !== null) {
        const turn = Math.atan2(before.x * dir.y - before.y * dir.x, before.x * dir.x + before.y * dir.y);
        const arcs = ls[j]! * turn < 0 ? Math.floor(Math.abs(ls[j]! * turn) / step) : 0;
        for (let k = 1; k <= arcs; k++, count++) marks.push(mark(from, rotate(before, ((turn * k) / (arcs + 1)) * 180 / Math.PI), ls[j]!, j, count, walked));
      }
      for (; station <= walked + length + 1e-9; station += step, count++)
        marks.push(mark({ x: from.x + dir.x * (station - walked), y: from.y + dir.y * (station - walked) }, dir, ls[j]!, j, count, station));
      next.set(j, { station, count });
    }
    before = dir;
    walked += length;
  }
  return marks;
}

/** A shape's outlines placed at a mark: turned to its facing, a left print mirrored, `scale` times its size (the rim under it is larger). */
export function placed(m: PathMark, scale = 1): XY[][] {
  const mirror = m.kind === 'foot' && m.left ? -scale : scale;
  const parts = m.kind === 'foot' ? BOOT : [TREAD_LINK];
  return parts.map((part) => part.map((p) => { const q = rotate({ x: p.x * mirror, y: p.y * scale }, m.facing); return { x: q.x + m.at.x, y: q.y + m.at.y }; }));
}
