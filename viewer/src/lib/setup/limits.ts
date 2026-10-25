// viewer/src/lib/setup/limits.ts
// SetupLimits and TextLimits (client/src/Core/SetupLimits.cs), word for word. The browser judges nothing: these mirror the server's
// limits only so a heading can count and an add can say why before the server refuses. Names are `api/names.ts`.

import type { Marker, Point3, TableSize, TableState } from '../protocol/wire.generated';

export const TERRAIN = 64, MARKERS = 32, MIN_EDGE = 12, MAX_EDGE = 144, LABEL = 64, NOTES = 500;

export const SIZE_HINT = `Width × length, ${MIN_EDGE}–${MAX_EDGE}" a side`;

export const terrainHeading = (d: TableState | null): string => d === null ? 'Terrain' : `Terrain (${d.terrain.length}/${TERRAIN})`;
export const markersHeading = (d: TableState | null): string => d === null ? 'Markers' : `Markers (${d.markers.length}/${MARKERS})`;

const NOT_YET = 'The table has not arrived yet.';

/** Null when `adding` more pieces fit. */
export function cannotAddTerrain(d: TableState | null, adding = 1): string | null {
  if (d === null) return NOT_YET;
  if (d.terrain.length + adding <= TERRAIN) return null;
  return adding === 1
    ? `The table holds at most ${TERRAIN} terrain pieces: remove some first.`
    : `These ${adding} pieces would take the table past ${TERRAIN} terrain pieces (it has ${d.terrain.length}): remove some first.`;
}

/** Null when the table has terrain for Clear terrain to remove. */
export function cannotClearTerrain(d: TableState | null): string | null {
  if (d === null) return NOT_YET;
  return d.terrain.length === 0 ? 'There is no terrain to clear.' : null;
}

export function cannotAddMarker(d: TableState | null): string | null {
  if (d === null) return NOT_YET;
  return d.markers.length < MARKERS ? null : `The table holds at most ${MARKERS} markers: remove some first.`;
}

/** C#'s Math.Round(x, 2) on the values that matter here (never an exact midpoint of a binary double). */
const round2 = (v: number): number => Math.round(v * 100) / 100;

/**
 * Where a new setup marker goes: the table's centre, else the first spot on rings 6" apart about it (eight, then sixteen, … points a
 * ring) whose marker would sit on the table and clear every marker already placed, base to base. The centre when none is.
 */
export function freeSpot(size: TableSize, markers: readonly Marker[], diameter: number): Point3 {
  const cx = size.width / 2, cy = size.length / 2;
  const free = (x: number, y: number): boolean =>
    x - diameter / 2 >= 0 && x + diameter / 2 <= size.width && y - diameter / 2 >= 0 && y + diameter / 2 <= size.length
    && markers.every((m) => Math.hypot(m.position.x - x, m.position.y - y) >= (m.diameter + diameter) / 2);
  if (free(cx, cy)) return { x: cx, y: cy, z: 0 };
  for (let ring = 1; ring * 6 <= Math.max(size.width, size.length); ring++) {
    for (let i = 0; i < 8 * ring; i++) {
      const angle = (2 * Math.PI * i) / (8 * ring);
      const x = round2(cx + ring * 6 * Math.cos(angle)), y = round2(cy + ring * 6 * Math.sin(angle));
      if (free(x, y)) return { x, y, z: 0 };
    }
  }
  return { x: cx, y: cy, z: 0 };
}

/** SetupToolbar.Beside: x = W + 4 + 8·{0,1,2}, y = 4 + 8·{0..3}, z 0; `random` in [0, 1). */
export function beside(size: TableSize, random: () => number = Math.random): Point3 {
  return { x: size.width + 4 + Math.floor(random() * 3) * 8, y: 4 + Math.floor(random() * 4) * 8, z: 0 };
}
