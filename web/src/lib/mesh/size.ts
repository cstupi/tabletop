// web/src/lib/mesh/size.ts — a dropped GLB's defaults, the plausibility check on its units, and the size readout.
import type { Orientation } from '$lib/api/types';
import { inchesToMm, UNIT_SCALES, type MeshUnit } from '$lib/units/units';
import { worldExtent } from './guess';
import type { Bounds } from './types';

/** glTF is metres, Y-up, front +Z: one unit is 39.37 inches, pitch 90 stands +Y on the table's +Z (and turns the front to -Y),
 *  and yaw 180 turns the front to +Y, the facing arrow. */
export const GLB_DEFAULTS: { scale: number; orientation: Orientation } = { scale: UNIT_SCALES.m, orientation: { pitch: 90, yaw: 180, roll: 0 } };

/** A miniature's largest dimension lies between these, in millimetres. */
export const PLAUSIBLE_MM = { min: 2, max: 1000 } as const;
const TYPICAL_MM = 32;

const largestMm = (b: Bounds, scale: number) => inchesToMm(Math.max(b.max[0] - b.min[0], b.max[1] - b.min[1], b.max[2] - b.min[2]) * scale);
const plausible = (mm: number) => mm >= PLAUSIBLE_MM.min && mm <= PLAUSIBLE_MM.max;

/** When the model is implausibly sized at `scale`, the unit (mm or cm) that makes it plausible, nearest a 32 mm miniature; null otherwise. */
export function suggestUnit(b: Bounds, scale: number): MeshUnit | null {
  if (plausible(largestMm(b, scale))) return null;
  const fits = (['mm', 'cm'] as const).filter((u) => plausible(largestMm(b, UNIT_SCALES[u])));
  if (fits.length === 0) return null;
  const off = (u: MeshUnit) => Math.abs(Math.log(largestMm(b, UNIT_SCALES[u]) / TYPICAL_MM));
  return fits.reduce<MeshUnit>((best, u) => (off(u) < off(best) ? u : best), fits[0]!);
}

const mm = (inches: number) => { const v = inchesToMm(inches); return v >= 10 ? v.toFixed(0) : v.toFixed(1); };
const inch = (inches: number) => (inches >= 10 ? inches.toFixed(1) : inches.toFixed(2));

/** "32 mm tall, 25 × 25 mm footprint (1.26 in tall, 0.98 × 0.98 in)": the table-frame extent of bounds × scale under the orientation. */
export function describeSize(b: Bounds, p: { scale: number; orientation: Orientation }): string {
  const e = worldExtent(b, p);
  return `${mm(e.height)} mm tall, ${mm(e.width)} × ${mm(e.length)} mm footprint (${inch(e.height)} in tall, ${inch(e.width)} × ${inch(e.length)} in)`;
}
