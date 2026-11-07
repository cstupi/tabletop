// viewer/src/lib/play/ringShape.ts
// client/src/Core/RingShape.cs (RingLook, the loops and the label candidates) and RingLabels.cs: how a ring of each kind draws, its outline's
// segments chained into closed loops, points spread along them where a label may stand, the label's anchor (the candidate lowest on the
// screen inside the view) and the placement that keeps labels apart and inside the view. The fill is the loops filled even-odd on the
// canvas, so the triangulation is Godot's alone.

import type { Segment, XY } from '../geometry';

export type RingKind = 'range' | 'charge' | 'shoot';

export interface RingLook { readonly fillAlpha: number; readonly lineWidth: number; readonly icon: 'charge' | 'shoot' }
const LOOKS: Record<RingKind, RingLook> = {
  charge: { fillAlpha: 0.22, lineWidth: 3, icon: 'charge' },
  shoot: { fillAlpha: 0.12, lineWidth: 3, icon: 'shoot' },
  range: { fillAlpha: 0.08, lineWidth: 2.5, icon: 'shoot' },
};
export const ringLook = (kind: RingKind): RingLook => LOOKS[kind];

export const JOIN = 1e-3;
export const LABEL_CANDIDATES = 48;

const dist = (a: XY, b: XY): number => Math.hypot(a.x - b.x, a.y - b.y);

/** Signed area, positive counter-clockwise. */
export function loopArea(loop: readonly XY[]): number {
  let a = 0;
  for (let i = 0; i < loop.length; i++) { const p = loop[i]!, q = loop[(i + 1) % loop.length]!; a += p.x * q.y - p.y * q.x; }
  return a / 2;
}

/** Each segment's end joined to the start of another within JOIN; a chain that does not close is dropped. */
export function ringLoops(segments: readonly Segment[]): XY[][] {
  const cell = (p: XY): string => `${Math.floor(p.x / JOIN)},${Math.floor(p.y / JOIN)}`;
  const starts = new Map<string, number[]>();
  segments.forEach((s, i) => { const k = cell(s.a); const list = starts.get(k); if (list === undefined) starts.set(k, [i]); else list.push(i); });
  const used = new Array<boolean>(segments.length).fill(false);
  const next = (at: XY): number => {
    const cx = Math.floor(at.x / JOIN), cy = Math.floor(at.y / JOIN);
    let best = -1, bestD = JOIN;
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++)
      for (const i of starts.get(`${cx + dx},${cy + dy}`) ?? []) {
        const d = dist(segments[i]!.a, at);
        if (!used[i] && d <= bestD) { best = i; bestD = d; }
      }
    return best;
  };
  const loops: XY[][] = [];
  for (let s = 0; s < segments.length; s++) {
    if (used[s]) continue;
    used[s] = true;
    const loop: XY[] = [segments[s]!.a];
    let at = segments[s]!.b, closed = false;
    for (;;) {
      if (dist(at, loop[0]!) <= JOIN && loop.length > 2) { closed = true; break; }
      const n = next(at);
      if (n < 0) break;
      used[n] = true; loop.push(segments[n]!.a); at = segments[n]!.b;
    }
    if (closed) loops.push(loop);
  }
  return loops;
}

/** Points spread evenly by length along every loop, at most count. */
export function spread(loops: readonly (readonly XY[])[], count = LABEL_CANDIDATES): XY[] {
  let total = 0;
  for (const l of loops) for (let i = 0; i < l.length; i++) total += dist(l[i]!, l[(i + 1) % l.length]!);
  if (total <= 0 || count <= 0) return [];
  const step = total / count, points: XY[] = [];
  let carry = 0;
  for (const l of loops) for (let i = 0; i < l.length; i++) {
    const a = l[i]!, b = l[(i + 1) % l.length]!, len = dist(a, b);
    while (carry <= len && points.length < count) { points.push(len === 0 ? a : { x: a.x + (b.x - a.x) * carry / len, y: a.y + (b.y - a.y) * carry / len }); carry += step; }
    carry -= len;
  }
  return points;
}

export interface RingShape { readonly loops: readonly (readonly XY[])[]; readonly candidates: readonly XY[] }
export const ringShapeOf = (segments: readonly Segment[]): RingShape => { const loops = ringLoops(segments); return { loops, candidates: spread(loops) }; };

// RingLabels.cs

export interface Rect { readonly x: number; readonly y: number; readonly w: number; readonly h: number }
/** Pixels between the outline and its label, and between two labels. */
export const LABEL_GAP = 6;

const overlaps = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

/** The candidate lowest on the screen inside the view (less the margin); when none is, the one nearest the view, pulled inside. */
export function ringAnchor(projected: readonly XY[], view: Rect, margin = 0): XY | null {
  if (projected.length === 0) return null;
  const l = view.x + margin, t = view.y + margin, r = view.x + view.w - margin, b = view.y + view.h - margin;
  let best: XY | null = null;
  for (const p of projected) if (p.x >= l && p.x <= r && p.y >= t && p.y <= b && (best === null || p.y > best.y)) best = p;
  if (best !== null) return best;
  const out = (v: number, lo: number, hi: number): number => (v < lo ? lo - v : v > hi ? v - hi : 0);
  let near = projected[0]!, nearD = Infinity;
  for (const p of projected) { const d = out(p.x, l, r) + out(p.y, t, b); if (d < nearD) { near = p; nearD = d; } }
  return { x: Math.min(Math.max(near.x, l), r), y: Math.min(Math.max(near.y, t), b) };
}

/** The rectangle a label of this size wants under its anchor, centred on it. */
export const labelUnder = (anchor: XY, w: number, h: number): Rect => ({ x: anchor.x - w / 2, y: anchor.y + LABEL_GAP, w, h });

const clampRect = (r: Rect, view: Rect): Rect => ({
  ...r,
  x: Math.min(Math.max(r.x, view.x), Math.max(view.x, view.x + view.w - r.w)),
  y: Math.min(Math.max(r.y, view.y), Math.max(view.y, view.y + view.h - r.h)),
});
const grown = (r: Rect): Rect => ({ x: r.x - LABEL_GAP / 2, y: r.y - LABEL_GAP / 2, w: r.w + LABEL_GAP, h: r.h + LABEL_GAP });

function slide(r: Rect, placed: readonly Rect[], view: Rect, dir: -1 | 1): Rect | null {
  for (let n = 0; n <= placed.length; n++) {
    const hit = placed.filter((p) => overlaps(grown(p), r));
    if (hit.length === 0) return r;
    r = { ...r, y: dir < 0 ? Math.min(...hit.map((p) => p.y)) - LABEL_GAP - r.h : Math.max(...hit.map((p) => p.y + p.h)) + LABEL_GAP };
    if (r.y < view.y || r.y + r.h > view.y + view.h) return null;
  }
  return null;
}

/** Each wanted rectangle, in order, kept inside the view; one that would overlap a label already placed moves up past it, or, when that
 * leaves the view, down past it from where it wanted to be. One that fits neither way stays where it wanted to be. */
export function placeRingLabels(wanted: readonly Rect[], view: Rect): Rect[] {
  const placed: Rect[] = [];
  for (const w of wanted) {
    const start = clampRect(w, view);
    placed.push(slide(start, placed, view, -1) ?? slide(start, placed, view, 1) ?? start);
  }
  return placed;
}
