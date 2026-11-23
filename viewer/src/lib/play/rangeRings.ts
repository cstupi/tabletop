// viewer/src/lib/play/rangeRings.ts
// client/src/Core/RangeRings.cs and RangeRingCache: per distinct printed Range, the union outline of the rings at that distance from the
// base edge of each model on the table carrying a weapon of that range (Geometry.rangeOutline). No Range, "-" or 0 is melee and draws
// none. The label is the printed value; a rule that changes a range is not shown.

import type { Overlay } from '../board/interaction';
import { measureOf, viewportWidthOf } from '../board/labelPlacement';
import { placed, type Geometry, type Placement, type Segment } from '../geometry';
import type { ModelInPlay, Projection } from '../live/apply';
import { labelUnder, placeRingLabels, ringAnchor, ringLook, ringShapeOf, type Rect, type RingIcon, type RingKind, type RingShape } from './ringShape';

/** `kind` is what the ring measures, which sets its look (`ringLook`): a printed range (V), or a threat's charge or shooting reach. */
export interface RangeRing { readonly inches: number; readonly label: string; readonly segments: readonly Segment[]; readonly kind: RingKind }

/** `24"` is 24; `-`, 0 and none are melee (null). */
export function inches(printed: string | null | undefined): number | null {
  if (printed === null || printed === undefined) return null;
  const text = printed.trim().replace(/"+$/, '');
  if (!/^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(text)) return null;
  const r = Number(text);
  return r > 0 ? r : null;
}

interface Carrier { readonly model: ModelInPlay; readonly placement: Placement }

function onTable(p: Projection, unitId: string): Carrier[] {
  const out: Carrier[] = [];
  for (const m of p.models.values()) if (m.unitId === unitId && m.location.kind === 'table') out.push({ model: m, placement: m.location.placement });
  return out;
}

/** One ring per distinct printed range, ascending. */
export function rangeRingsOf(g: Geometry, p: Projection, unitId: string): RangeRing[] {
  const models = onTable(p, unitId);
  const carriers = models.map((c) => ({
    ranges: new Set(c.model.slot.weapons.map((w) => inches(w.stats['Range'])).filter((r): r is number => r !== null)),
    at: placed(c.model.slot, c.placement),
  }));
  const printed = new Map<number, string>();
  for (const c of models) for (const w of c.model.slot.weapons) {
    const r = w.stats['Range'];
    const d = inches(r);
    if (r !== undefined && d !== null && !printed.has(d)) printed.set(d, r);
  }
  return [...printed].sort((a, b) => a[0] - b[0])
    .map(([d, label]) => ({ inches: d, label: label.trim(), segments: g.rangeOutline(carriers.filter((c) => c.ranges.has(d)).map((c) => c.at), d), kind: 'range' as const }));
}

const samePlacement = (a: Placement, b: Placement): boolean =>
  a.position.x === b.position.x && a.position.y === b.position.y && a.position.z === b.position.z && a.yaw.degrees === b.yaw.degrees;

/** One unit's range rings, recomputed only when the unit asked about or its models' slots or placements change. */
export class RangeRingCache {
  #unit: string | null = null;
  #key: readonly Carrier[] | null = null;
  #rings: readonly RangeRing[] = [];

  of(g: Geometry, p: Projection, unitId: string): readonly RangeRing[] {
    const key = onTable(p, unitId);
    const prev = this.#key;
    if (prev !== null && unitId === this.#unit && key.length === prev.length
      && key.every((c, i) => c.model.id === prev[i]!.model.id && c.model.slot === prev[i]!.model.slot && samePlacement(c.placement, prev[i]!.placement))) return this.#rings;
    this.#unit = unitId;
    this.#key = key;
    this.#rings = rangeRingsOf(g, p, unitId);
    return this.#rings;
  }
}

/** One set of rings in one colour: the hovered enemy's threat, my selected unit's ranges, or the area a placement keeps out of; `words`, the
 *  label's colour and `rim` the outline's when they are not the rings' (the keep-away's dark shade would not read on the pill or a dark board). */
export interface RingLayer { readonly rings: readonly RangeRing[]; readonly colour: string; readonly words?: string; readonly rim?: string }

const shapes = new WeakMap<RangeRing, RingShape>();
function shapeOf(r: RangeRing): RingShape {
  let s = shapes.get(r);
  if (s === undefined) { s = ringShapeOf(r.segments); shapes.set(r, s); }
  return s;
}

const LABEL_FONT = '600 14px system-ui, sans-serif';
const LABEL_H = 24, ICON = 14, PAD = 8, ICON_GAP = 6;

function viewportHeightOf(ctx: CanvasRenderingContext2D): number {
  const canvas = (ctx as { canvas?: { height?: unknown } }).canvas;
  const height = typeof canvas?.height === 'number' ? canvas.height : Number.POSITIVE_INFINITY;
  const scale = typeof ctx.getTransform === 'function' ? ctx.getTransform()?.d : undefined;
  return height / (typeof scale === 'number' && scale > 0 ? scale : 1);
}

/** The kind's icon in a box ICON wide, its top left at (x, y): a crosshair for shooting and ranges, two crossed blades for a charge, a warning
 *  triangle for a keep-away, a framed band for a zone's reach. */
function drawIcon(ctx: CanvasRenderingContext2D, icon: RingIcon, x: number, y: number): void {
  const c = ICON / 2;
  ctx.beginPath();
  if (icon === 'zone') {
    ctx.rect(x + 1, y + 1, ICON - 2, ICON - 2);
    ctx.moveTo(x + 1, y + c); ctx.lineTo(x + ICON - 1, y + c);
  } else if (icon === 'warning') {
    ctx.moveTo(x + c, y + 1); ctx.lineTo(x + ICON - 1, y + ICON - 1); ctx.lineTo(x + 1, y + ICON - 1); ctx.closePath();
    ctx.moveTo(x + c, y + c * 0.7); ctx.lineTo(x + c, y + c * 1.3); ctx.moveTo(x + c, y + ICON - 3.5); ctx.lineTo(x + c, y + ICON - 3);
  } else if (icon === 'shoot') {
    ctx.arc(x + c, y + c, c * 0.6, 0, 2 * Math.PI);
    ctx.moveTo(x + c, y); ctx.lineTo(x + c, y + c * 0.55); ctx.moveTo(x + c, y + ICON); ctx.lineTo(x + c, y + ICON - c * 0.55);
    ctx.moveTo(x, y + c); ctx.lineTo(x + c * 0.55, y + c); ctx.moveTo(x + ICON, y + c); ctx.lineTo(x + ICON - c * 0.55, y + c);
  } else {
    ctx.moveTo(x + 1, y + 1); ctx.lineTo(x + ICON - 3, y + ICON - 3); ctx.moveTo(x + ICON - 1, y + 1); ctx.lineTo(x + 3, y + ICON - 3);
    ctx.moveTo(x + ICON - 5, y + ICON - 2); ctx.lineTo(x + ICON - 2, y + ICON - 5); ctx.moveTo(x + 5, y + ICON - 2); ctx.lineTo(x + 2, y + ICON - 5);
  }
  ctx.stroke();
}

/** RangeRingNode: each ring's area filled in its kind's opacity of the colour (even-odd, so a hole stays open), its outline 2.5–3 px over a
 * dark halo, and a pill label (icon and words in the colour) under the outline's point lowest on the canvas; the labels of every layer are
 * kept apart, inside the canvas and off the HUD frame's obstacles (`placeRingLabels`), in layer order. */
export const ringsOverlay = (layers: readonly RingLayer[], obstacles: readonly Rect[] = []): Overlay => (ctx, project) => {
  ctx.save();
  const labels: { text: string; icon: RingIcon; colour: string; at: { x: number; y: number } }[] = [];
  const view: Rect = { x: 0, y: 0, w: viewportWidthOf(ctx), h: viewportHeightOf(ctx) };
  for (const { rings, colour, words, rim } of layers) for (const r of rings) {
    if (r.segments.length === 0) continue;
    const look = ringLook(r.kind), shape = shapeOf(r);
    ctx.beginPath();
    for (const loop of shape.loops) {
      loop.forEach((q, i) => { const s = project(q); if (i === 0) ctx.moveTo(s.x, s.y); else ctx.lineTo(s.x, s.y); });
      ctx.closePath();
    }
    ctx.globalAlpha = look.fillAlpha; ctx.fillStyle = colour; ctx.fill('evenodd');
    ctx.globalAlpha = 1; ctx.lineJoin = 'round';
    ctx.lineWidth = look.lineWidth + 2; ctx.strokeStyle = 'rgba(0, 0, 0, 0.35)'; ctx.stroke();
    ctx.lineWidth = look.lineWidth; ctx.strokeStyle = rim ?? colour; ctx.stroke();
    const at = ringAnchor(shape.candidates.map(project), view, 8, obstacles);
    if (at !== null) labels.push({ text: r.label, icon: look.icon, colour: words ?? colour, at });
  }
  ctx.font = LABEL_FONT; ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
  const measure = measureOf(ctx);
  const placed = placeRingLabels(labels.map((l) => labelUnder(l.at, PAD + ICON + ICON_GAP + measure(l.text) + PAD, LABEL_H)), view, obstacles);
  labels.forEach((l, i) => {
    const b = placed[i]!;
    ctx.beginPath();
    if (typeof ctx.roundRect === 'function') ctx.roundRect(b.x, b.y, b.w, b.h, 6); else ctx.rect(b.x, b.y, b.w, b.h);
    ctx.fillStyle = 'rgba(12, 16, 22, 0.88)'; ctx.fill();
    ctx.lineWidth = 1.5; ctx.strokeStyle = l.colour; ctx.stroke();
    ctx.lineWidth = 2; ctx.lineCap = 'round'; drawIcon(ctx, l.icon, b.x + PAD, b.y + (b.h - ICON) / 2);
    ctx.fillStyle = l.colour; ctx.fillText(l.text, b.x + PAD + ICON + ICON_GAP, b.y + b.h / 2 + 1);
  });
  ctx.restore();
};

