// viewer/src/lib/play/moveBandLook.ts
// client/src/Core/MoveBandLook.cs: what each band of a move's area draws in. Clear: the moving team's colour. Difficult: that colour muted halfway to
// grey and fainter. Dangerous: the terrain's own dangerous colour (terrainEffects.ts), striped every STRIPE_SPACING inches.

import type { MoveBands, XY } from '../geometry';
import { effectOf } from './terrainEffects';

export type MoveBand = 'clear' | 'difficult' | 'dangerous';

export const CLEAR_ALPHA = 0.25, DIFFICULT_ALPHA = 0.2, DANGEROUS_ALPHA = 0.4;
/** The stripes' period on the table, in inches; half of it is drawn. */
export const STRIPE_SPACING = 0.6;

const rgb = (hex: string): [number, number, number] => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as [number, number, number];

/** The band's colour as a CSS rgba(), for a team colour given as #rrggbb. */
export function bandColour(band: MoveBand, team: string): string {
  if (band === 'dangerous') { const [r, g, b] = rgb(effectOf('dangerous').colour); return `rgba(${r}, ${g}, ${b}, ${DANGEROUS_ALPHA})`; }
  const [r, g, b] = rgb(team);
  if (band === 'clear') return `rgba(${r}, ${g}, ${b}, ${CLEAR_ALPHA})`;
  const mid = (c: number): number => Math.round((c + 127.5) / 2);
  return `rgba(${mid(r)}, ${mid(g)}, ${mid(b)}, ${DIFFICULT_ALPHA})`;
}

export const striped = (band: MoveBand): boolean => band === 'dangerous';

/** The bands in drawing order, each with its triangles from every model's area. */
export function layers(areas: readonly MoveBands[]): { band: MoveBand; areas: (readonly XY[])[] }[] {
  return [
    { band: 'clear', areas: areas.map((a) => a.clear) },
    { band: 'difficult', areas: areas.map((a) => a.difficult) },
    { band: 'dangerous', areas: areas.map((a) => a.dangerous) },
  ];
}
