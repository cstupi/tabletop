// viewer/src/lib/play/rangeRings.ts
// client/src/Core/RangeRings.cs and RangeRingCache: per distinct printed Range, the union outline of the rings at that distance from the
// base edge of each model on the table carrying a weapon of that range (Geometry.rangeOutline). No Range, "-" or 0 is melee and draws
// none. The label is the printed value; a rule that changes a range is not shown.

import type { Overlay } from '../board/interaction';
import { placed, type Geometry, type Placement, type Segment } from '../geometry';
import type { ModelInPlay, Projection } from '../live/apply';

export interface RangeRing { readonly inches: number; readonly label: string; readonly segments: readonly Segment[] }

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
    .map(([d, label]) => ({ inches: d, label: label.trim(), segments: g.rangeOutline(carriers.filter((c) => c.ranges.has(d)).map((c) => c.at), d) }));
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

/** The rings' outlines and each label at the outline's far edge (RangeRingNode). */
export const ringsOverlay = (rings: readonly RangeRing[], colour: string): Overlay => (ctx, project) => {
  ctx.save();
  ctx.lineWidth = 2;
  ctx.font = '600 13px ui-monospace, SFMono-Regular, Menlo, monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  for (const r of rings) {
    if (r.segments.length === 0) continue;
    ctx.strokeStyle = colour;
    ctx.beginPath();
    for (const s of r.segments) { const a = project(s.a), b = project(s.b); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); }
    ctx.stroke();
    const far = r.segments.reduce((best, s) => (s.a.y > best.y ? s.a : best), r.segments[0]!.a);
    const at = project(far);
    ctx.lineWidth = 3; ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)'; ctx.strokeText(r.label, at.x, at.y - 4);
    ctx.lineWidth = 2; ctx.fillStyle = colour; ctx.fillText(r.label, at.x, at.y - 4);
  }
  ctx.restore();
};

