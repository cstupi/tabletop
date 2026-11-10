// viewer/src/lib/play/moveOverlays.ts
// What RulerNode, LandingMarks, MoveRegionNode (the area in its bands, or a circle until they are built), ViolationMarks and the pick box draw, as data
// and then as board Overlays.

import type { Overlay } from '../board/interaction';
import { drawLabel } from '../board/labelPlacement';
import type { MoveBands, XY } from '../geometry';
import { bandColour, layers, STRIPE_SPACING, striped } from './moveBandLook';
import { readoutText, type RulerReadout } from './moveRuler';
import type { Held } from './modelDrag';
import type { Violations } from './violations';

/** `path`: the bent path the move is measured along (from `from` to `to`), drawn instead of the straight line. */
export interface RulerLine { readonly from: XY; readonly to: XY; readonly text: string; readonly outOfRange: boolean; readonly path?: readonly XY[] }
export interface Ring { readonly centre: XY; readonly radius: number }
export interface Label { readonly at: XY; readonly text: string }

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
    return at === null ? [] : [{ at, text: l.reason }];
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

/** MoveRegionNode.ShowBands: each band's triangles from every held model's area filled as one path (each triangle wound one way, so overlapping
 *  areas fill once), the dangerous band striped across the table. */
export const bandsOverlay = (areas: readonly MoveBands[], team: string): Overlay => (ctx, project, ppi) => {
  for (const { band, areas: parts } of layers(areas)) {
    if (parts.every((t) => t.length < 3)) continue;
    ctx.save();
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
    ctx.fillStyle = bandColour(band, team);
    ctx.fill('nonzero');
    if (striped(band)) {
      // Diagonal stripes, half of each period drawn, clipped to the band.
      ctx.clip('nonzero');
      const period = Math.max(4, STRIPE_SPACING * ppi);
      ctx.strokeStyle = bandColour(band, team); ctx.lineWidth = period / 2; ctx.setLineDash([]);
      ctx.beginPath();
      for (let x = minX - (maxY - minY); x <= maxX; x += period) { ctx.moveTo(x, maxY); ctx.lineTo(x + (maxY - minY), minY); }
      ctx.stroke();
    }
    ctx.restore();
  }
};

export const rulerOverlay = (lines: readonly RulerLine[]): Overlay => (ctx, project) => {
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

export const labelOverlay = (labels: readonly Label[], colour: string): Overlay => (ctx, project) => {
  ctx.fillStyle = colour; ctx.font = 'bold 11px ui-monospace, monospace'; ctx.textAlign = 'center';
  for (const label of labels) { const p = project(label.at); drawLabel(ctx, label.text, { x: p.x, y: p.y - 16 }, colour, { lineHeight: 13, grow: 'up' }); }
};
