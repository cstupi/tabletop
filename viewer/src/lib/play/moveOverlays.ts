// viewer/src/lib/play/moveOverlays.ts
// What RulerNode, LandingMarks, MoveRegionNode (the area in its bands, or a circle until they are built), ViolationMarks and the pick box draw, as data
// and then as board Overlays.

import type { Rect } from './hudFrame';
import type { Overlay } from '../board/interaction';
import { drawLabel, measureOf, viewportWidthOf } from '../board/labelPlacement';
import type { MoveBands, XY } from '../geometry';
import { bandColour, layers, STRIPE_SPACING, striped } from './moveBandLook';
import { readoutText, type RulerReadout } from './moveRuler';
import type { Held } from './modelDrag';
import { markColour, RIM_SCALE, rimColour, type ModelPath } from './movePaths';
import { marksOf, placed, type PathMark } from './pathMarks';
import type { Violations } from './violations';
import { MARK_GAP, placeMarkLabels } from './markLabels';
import type { CoherencyPicture } from './coherencyView';

/** `path`: the bent path the move is measured along (from `from` to `to`), drawn instead of the straight line. */
export interface RulerLine { readonly from: XY; readonly to: XY; readonly text: string; readonly outOfRange: boolean; readonly path?: readonly XY[] }
export interface Ring { readonly centre: XY; readonly radius: number }
/** `modelId`: whose label it is, so one model's labels stack (placeMarkLabels); absent for a label of its own. */
export interface Label { readonly at: XY; readonly text: string; readonly modelId?: string }

/** RulerNode: the line from where each piece started to where it is. One reading for the drag: the model that moved farthest, which binds
 *  the allowance; the other lines carry none. */
export function rulerLines(readouts: readonly { from: XY; to: XY; readout: RulerReadout; path?: readonly XY[] }[]): RulerLine[] {
  const far = readouts.reduce((best, r, i) => (best < 0 || r.readout.distance > readouts[best]!.readout.distance ? i : best), -1);
  return readouts.map((r, i) => ({ from: r.from, to: r.to, text: i === far ? readoutText(r.readout) : '', outOfRange: r.readout.outOfRange,
    ...(r.path !== undefined && r.path.length > 2 ? { path: r.path } : {}) }));
}

const apart = (a: number, b: number): boolean => Math.abs(a - b) > 1e-6;

/** A ring where each held model lands while it is drawn elsewhere (lifted over another model or terrain). */
export function landingRings(held: Held, radius: (modelId: string) => number): Ring[] {
  const rings: Ring[] = [];
  for (const id of held.ids) {
    const end = held.ends.get(id);
    if (end === undefined || !(apart(end.drawn.x, end.lands.x) || apart(end.drawn.y, end.lands.y) || apart(end.drawn.z, end.lands.z))) continue;
    rings.push({ centre: { x: end.lands.x, y: end.lands.y }, radius: radius(id) });
  }
  return rings;
}

/** One label per reason over its first model (Violations.labels), held models first. */
export function violationLabels(v: Violations, order: readonly string[], centreOf: (id: string) => XY | null): Label[] {
  return v.labels(order).flatMap((l) => {
    const at = centreOf(l.modelId);
    return at === null ? [] : [{ modelId: l.modelId, at, text: l.reason }];
  });
}

const RED = '#ff5c5c';
const circle = (ctx: CanvasRenderingContext2D, project: (p: XY) => XY, ppi: number, ring: Ring): void => {
  const c = project(ring.centre);
  ctx.beginPath();
  ctx.arc(c.x, c.y, Math.max(3, ring.radius * ppi), 0, 2 * Math.PI);
  ctx.stroke();
};

export const allowanceOverlay = (rings: readonly Ring[], colour: string): Overlay => (ctx, project, ppi) => {
  ctx.strokeStyle = colour; ctx.lineWidth = 1.5; ctx.setLineDash([6, 4]);
  for (const ring of rings) circle(ctx, project, ppi, ring);
};

/** One area's band traced once, in table inches, each triangle wound counter-clockwise (so the board's flip winds them all one way): kept per
 *  triangle list, which an area keeps for its Ask, so a drag frame only fills it. */
const traced = new WeakMap<readonly XY[], { path: Path2D; minX: number; minY: number; maxX: number; maxY: number }>();
function tracedOf(t: readonly XY[]) {
  const known = traced.get(t);
  if (known !== undefined) return known;
  const path = new Path2D();
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (let i = 0; i + 2 < t.length; i += 3) {
    let a = t[i]!, b = t[i + 1]!;
    const c = t[i + 2]!;
    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) < 0) [a, b] = [b, a];
    path.moveTo(a.x, a.y); path.lineTo(b.x, b.y); path.lineTo(c.x, c.y); path.closePath();
    for (const q of [a, b, c]) { minX = Math.min(minX, q.x); minY = Math.min(minY, q.y); maxX = Math.max(maxX, q.x); maxY = Math.max(maxY, q.y); }
  }
  const found = { path, minX, minY, maxX, maxY };
  traced.set(t, found);
  return found;
}

/** Stripes across the band's box on the screen, half of each period drawn, inside the clip already set. */
function stripes(ctx: CanvasRenderingContext2D, colour: string, ppi: number, minX: number, minY: number, maxX: number, maxY: number): void {
  const period = Math.max(4, STRIPE_SPACING * ppi);
  ctx.strokeStyle = colour; ctx.lineWidth = period / 2; ctx.setLineDash([]);
  ctx.beginPath();
  for (let x = minX - (maxY - minY); x <= maxX; x += period) { ctx.moveTo(x, maxY); ctx.lineTo(x + (maxY - minY), minY); }
  ctx.stroke();
}

/** MoveRegionNode.ShowBands: each band's triangles from every held model's area filled as one path (each triangle wound one way, so overlapping
 *  areas fill once), the dangerous band striped across the table. Each area's band is traced once (tracedOf) and drawn through the board's
 *  transform; without Path2D (the tests) each triangle is projected and traced as it is drawn. */
export const bandsOverlay = (areas: readonly MoveBands[], team: string): Overlay => (ctx, project, ppi) => {
  const cached = typeof Path2D !== 'undefined' && typeof ctx.getTransform === 'function';
  for (const { band, areas: parts } of layers(areas)) {
    if (parts.every((t) => t.length < 3)) continue;
    ctx.save();
    const colour = bandColour(band, team);
    if (cached) {
      const screen = ctx.getTransform();
      const o = project({ x: 0, y: 0 }), ex = project({ x: 1, y: 0 }), ey = project({ x: 0, y: 1 });
      ctx.transform(ex.x - o.x, ex.y - o.y, ey.x - o.x, ey.y - o.y, o.x, o.y);
      const all = new Path2D();
      let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      for (const t of parts) {
        if (t.length < 3) continue;
        const one = tracedOf(t);
        all.addPath(one.path);
        for (const q of [project({ x: one.minX, y: one.minY }), project({ x: one.maxX, y: one.maxY })]) {
          minX = Math.min(minX, q.x); minY = Math.min(minY, q.y); maxX = Math.max(maxX, q.x); maxY = Math.max(maxY, q.y);
        }
      }
      ctx.fillStyle = colour;
      ctx.fill(all, 'nonzero');
      if (striped(band)) { ctx.clip(all, 'nonzero'); ctx.setTransform(screen); stripes(ctx, colour, ppi, minX, minY, maxX, maxY); }
      ctx.restore();
      continue;
    }
    ctx.beginPath();
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const t of parts)
      for (let i = 0; i + 2 < t.length; i += 3) {
        let a = project(t[i]!), b = project(t[i + 1]!);
        const c = project(t[i + 2]!);
        if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) < 0) [a, b] = [b, a];
        ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.lineTo(c.x, c.y); ctx.closePath();
        for (const q of [a, b, c]) { minX = Math.min(minX, q.x); minY = Math.min(minY, q.y); maxX = Math.max(maxX, q.x); maxY = Math.max(maxY, q.y); }
      }
    ctx.fillStyle = colour;
    ctx.fill('nonzero');
    if (striped(band)) { ctx.clip('nonzero'); stripes(ctx, colour, ppi, minX, minY, maxX, maxY); }
    ctx.restore();
  }
};

/** The theme's look for the marks: Fantasy's ink stamp, else Sci-fi's light trace (read when drawn; the board redraws on a theme switch). */
const inkLook = (): boolean => typeof document !== 'undefined' && document.documentElement.dataset['theme'] === 'fantasy';

/** Each path's marks, laid once per ModelPath (a ghost that stayed put hands back the same path). */
const laid = new WeakMap<ModelPath, { vehicle: boolean; marks: PathMark[] }>();
const marksOfPath = (path: ModelPath, vehicle: boolean): PathMark[] => {
  const known = laid.get(path);
  if (known !== undefined && known.vehicle === vehicle) return known.marks;
  const marks = marksOf(path.route, path.start.body.base, path.stretches, vehicle, path.start.placement.yaw.degrees, path.end);
  laid.set(path, { vehicle, marks });
  return marks;
};

/** MovePathNode: each model's marks along its route (pathMarks: boot prints, a vehicle's treads) in the team colour in the theme's look, a mark
 *  where the base touches dangerous ground in the dangerous colour, each over a dark rim; `kept`, the last move's, fainter. One fill per colour. */
export const pathsOverlay = (paths: readonly ModelPath[], team: string, vehicle: (modelId: string) => boolean, kept = false): Overlay => (ctx, project) => {
  if (paths.length === 0) return;
  const ink = inkLook();
  const all = paths.flatMap((path) => marksOfPath(path, vehicle(path.modelId)));
  const fill = (marks: readonly PathMark[], style: string, scale: number): void => {
    if (marks.length === 0) return;
    ctx.fillStyle = style;
    ctx.beginPath();
    for (const m of marks)
      for (const part of placed(m, scale)) {
        const points = part.map(project);
        ctx.moveTo(points[0]!.x, points[0]!.y);
        for (const q of points.slice(1)) ctx.lineTo(q.x, q.y);
        ctx.closePath();
      }
    ctx.fill('nonzero');
  };
  ctx.save();
  fill(all, rimColour(kept), RIM_SCALE);
  for (const dangerous of [false, true]) fill(all.filter((m) => m.dangerous === dangerous), markColour({ dangerous }, team, ink, kept), 1);
  ctx.restore();
};

/** CoherencyNode: each ghost's ring at the coherency distance from its base edge, faint in the team's colour, and per model out of coherency a
 *  dashed line in the warning colour from its base to the model it is measured to, with its words over the line's middle. */
export const coherencyOverlay = (picture: CoherencyPicture, team: string, warning: string): Overlay => (ctx, project) => {
  if (picture.rings.length + picture.lines.length === 0) return;
  ctx.save();
  ctx.globalAlpha = 0.55; ctx.strokeStyle = team; ctx.lineWidth = 1; ctx.setLineDash([]);
  ctx.beginPath();
  for (const ring of picture.rings) for (const s of ring.segments) { const a = project(s.a), b = project(s.b); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); }
  ctx.stroke();
  ctx.globalAlpha = 1; ctx.strokeStyle = warning; ctx.lineWidth = 2; ctx.setLineDash([6, 4]);
  for (const line of picture.lines) {
    const a = project(line.from), b = project(line.to);
    ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
  }
  ctx.setLineDash([]); ctx.font = 'bold 11px ui-monospace, monospace'; ctx.textAlign = 'center';
  ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)'; ctx.lineWidth = 3; ctx.lineJoin = 'round'; ctx.fillStyle = warning;
  for (const line of picture.lines) {
    const a = project(line.from), b = project(line.to), at = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 - 6 };
    ctx.strokeText(line.text, at.x, at.y); ctx.fillText(line.text, at.x, at.y);
  }
  ctx.restore();
};

export const rulerOverlay =(lines: readonly RulerLine[]): Overlay => (ctx, project) => {
  ctx.lineWidth = 1.5; ctx.font = '11px ui-monospace, monospace'; ctx.textAlign = 'center';
  for (const line of lines) {
    const points = (line.path ?? [line.from, line.to]).map(project);
    const colour = line.outOfRange ? RED : '#ffffff';
    ctx.strokeStyle = colour; ctx.fillStyle = colour;
    ctx.beginPath(); ctx.moveTo(points[0]!.x, points[0]!.y);
    for (const q of points.slice(1)) ctx.lineTo(q.x, q.y);
    ctx.stroke();
    // The reading sits over the middle of the longest leg.
    let leg = 0;
    for (let i = 1; i + 1 < points.length; i++)
      if (Math.hypot(points[i + 1]!.x - points[i]!.x, points[i + 1]!.y - points[i]!.y) > Math.hypot(points[leg + 1]!.x - points[leg]!.x, points[leg + 1]!.y - points[leg]!.y)) leg = i;
    const a = points[leg]!, b = points[leg + 1]!;
    if (line.text !== '') drawLabel(ctx, line.text, { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 - 6 }, colour, { lineHeight: 13, grow: 'up' });
  }
};

export const landingOverlay = (rings: readonly Ring[]): Overlay => (ctx, project, ppi) => {
  ctx.strokeStyle = '#6ddf7c'; ctx.lineWidth = 2; ctx.setLineDash([2, 2]);
  for (const ring of rings) circle(ctx, project, ppi, ring);
};

/** A Shift+drag pick box (TableInput.Picks' rectangle): dashed, from the press to the pointer. */
export const boxOverlay = (from: XY, to: XY, colour = '#ffffff'): Overlay => (ctx, project) => {
  const a = project(from), b = project(to);
  const x = Math.min(a.x, b.x), y = Math.min(a.y, b.y), w = Math.abs(b.x - a.x), h = Math.abs(b.y - a.y);
  ctx.save();
  ctx.strokeStyle = 'rgba(0, 0, 0, 0.45)'; ctx.lineWidth = 3; ctx.strokeRect(x, y, w, h);    // a rim, so the dashes read on either theme
  ctx.strokeStyle = colour; ctx.lineWidth = 1.5; ctx.setLineDash([5, 4]); ctx.strokeRect(x, y, w, h);
  ctx.restore();
};

/** ViolationMarks' labels: short words by each model, a model's stacked MARK_GAP apart under its base (the unit's name label stands over it),
 *  no two meeting, none leaving the board's canvas and all off the HUD frame's obstacles (placeMarkLabels). */
export const labelOverlay = (labels: readonly Label[], colour: string, obstacles: readonly Rect[] = []): Overlay => (ctx, project, ppi) => {
  if (labels.length === 0) return;
  ctx.fillStyle = colour; ctx.font = 'bold 11px ui-monospace, monospace'; ctx.textAlign = 'center'; ctx.textBaseline = 'top';
  const lineHeight = 13, measure = measureOf(ctx);
  const ids = labels.map((l, i) => l.modelId ?? `#${i}`);
  const stack = (id: string): number => ids.filter((x) => x === id).length * (lineHeight + MARK_GAP) - MARK_GAP;
  const below = 1.3 * (Number.isFinite(ppi) ? ppi : 10);                 // past a cavalry base's half-length
  const wanted = labels.map((l, i) => {
    const p = project(l.at);
    return { modelId: ids[i]!, anchor: { x: p.x, y: p.y + below + stack(ids[i]!) }, w: measure(l.text) + 4, h: lineHeight };
  });
  const canvas = (ctx as { canvas?: { width?: unknown; height?: unknown } }).canvas;
  const scale = typeof ctx.getTransform === 'function' ? ctx.getTransform()?.d : undefined;
  const w = viewportWidthOf(ctx), h = typeof canvas?.height === 'number' ? canvas.height / (typeof scale === 'number' && scale > 0 ? scale : 1) : Infinity;
  const view = Number.isFinite(w) && Number.isFinite(h) ? { x: 0, y: 0, w, h } : { x: -1e6, y: -1e6, w: 2e6, h: 2e6 };
  const placed = placeMarkLabels(wanted, view, obstacles);
  ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)'; ctx.lineWidth = 3; ctx.lineJoin = 'round';     // a halo, so the words read over the bands and paths
  labels.forEach((l, i) => { const r = placed[i]!; ctx.strokeText(l.text, r.x + r.w / 2, r.y); ctx.fillText(l.text, r.x + r.w / 2, r.y); });
  ctx.textBaseline = 'alphabetic';
};
