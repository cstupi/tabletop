// web/src/lib/units/bases.ts — presets are names over a shape; the stored value is always inches.
import type { Base } from '$lib/api/types';
import { inchesToMm, mmToInches, round } from './units';

export interface BasePreset { label: string; base: Base }
export const circle = (mm: number): Base => ({ shape: 'circle', a: mmToInches(mm), b: mmToInches(mm) });
export const oval = (lengthMm: number, widthMm: number): Base => ({ shape: 'oval', a: mmToInches(lengthMm), b: mmToInches(widthMm) });
export const rect = (lengthMm: number, widthMm: number): Base => ({ shape: 'rect', a: mmToInches(lengthMm), b: mmToInches(widthMm) });

/** The sizes One Page Rules publishes for its units (every `bases.round` in the official Grimdark Future army books), and the hull a
 * vehicle with no published base starts on (management's `ArmyForgeImport.DefaultBody`). Any other size is a custom one. */
export const BASE_PRESETS: BasePreset[] = [
  ...[25, 32, 40, 50, 60].map((mm) => ({ label: `${mm} mm round`, base: circle(mm) })),
  ...([[60, 35], [75, 46], [90, 52], [105, 70], [120, 92], [160, 122]] as const).map(([l, w]) => ({ label: `${l} × ${w} mm oval`, base: oval(l, w) })),
  ...([[120, 80]] as const).map(([l, w]) => ({ label: `${l} × ${w} mm vehicle`, base: rect(l, w) })),
];

/** Two bases are the same when the shape matches and both dimensions agree within 0.005 in. */
export const sameBase = (x: Base, y: Base): boolean => x.shape === y.shape && Math.abs(x.a - y.a) < 0.005 && Math.abs(x.b - y.b) < 0.005;
export const presetFor = (b: Base) => BASE_PRESETS.find((p) => sameBase(p.base, b));

export function baseLabel(b: Base): string {
  const preset = presetFor(b);
  if (preset) return preset.label;
  const mm = (x: number) => round(inchesToMm(x), 1);
  if (b.shape === 'circle') return `${mm(b.a)} mm round (custom)`;
  return `${mm(b.a)} × ${mm(b.b)} mm ${b.shape} (custom)`;
}
