// viewer/src/lib/play/moveOverlays.ts
// What RulerNode, LandingMarks, MoveRegionNode (as a circle), ViolationMarks and the pick box draw, as data and then as board Overlays.

import type { Overlay } from '../board/interaction';
import { drawLabel } from '../board/labelPlacement';
import type { XY } from '../geometry';
import { readoutText, type RulerReadout } from './moveRuler';
import type { Held } from './modelDrag';
import type { Violations } from './violations';

export interface RulerLine { readonly from: XY; readonly to: XY; readonly text: string; readonly outOfRange: boolean }
export interface Ring { readonly centre: XY; readonly radius: number }
export interface Label { readonly at: XY; readonly text: string }

/** RulerNode: the line from where each piece started to where it is. One reading for the drag: the model that moved farthest, which binds
 *  the allowance; the other lines carry none. */
export function rulerLines(readouts: readonly { from: XY; to: XY; readout: RulerReadout }[]): RulerLine[] {
  const far = readouts.reduce((best, r, i) => (best < 0 || r.readout.distance > readouts[best]!.readout.distance ? i : best), -1);
  return readouts.map((r, i) => ({ from: r.from, to: r.to, text: i === far ? readoutText(r.readout) : '', outOfRange: r.readout.outOfRange }));
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

export const rulerOverlay = (lines: readonly RulerLine[]): Overlay => (ctx, project) => {
  ctx.lineWidth = 1.5; ctx.font = '11px ui-monospace, monospace'; ctx.textAlign = 'center';
  for (const line of lines) {
    const a = project(line.from), b = project(line.to);
    const colour = line.outOfRange ? RED : '#ffffff';
    ctx.strokeStyle = colour; ctx.fillStyle = colour;
    ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
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
