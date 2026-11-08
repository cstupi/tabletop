// viewer/src/lib/board/tablePick.ts
// client/src/Core/TablePick.cs: which piece the pointer picks: a model it is on, the nearest; else a model within the slop of it, the closest;
// else the nearest terrain piece or marker it is on. A terrain area encloses its whole volume, so a model in or beside it is never lost to it.

export type HitKind = 'model' | 'other';
/** along: how far down the pointer's ray (or draw order) the piece is; miss: how far the pointer is beside it (0: on it). */
export interface TableHit { readonly kind: HitKind; readonly along: number; readonly miss: number }

/** The slop in screen pixels: a click this close to a model picks it over the terrain under the pointer. */
export const SLOP_PIXELS = 12;

const rank = (h: TableHit, slop: number): number | null =>
  h.kind === 'model' ? (h.miss <= 0 ? 0 : h.miss <= slop ? 1 : null) : h.miss <= 0 ? 2 : null;
const key = (h: TableHit): number => (h.kind === 'model' && h.miss > 0 ? h.miss : h.along);

export function bestHit(hits: readonly TableHit[], slop: number = SLOP_PIXELS): number | null {
  let best: number | null = null;
  hits.forEach((h, i) => {
    const r = rank(h, slop);
    if (r === null) return;
    const b = best === null ? null : rank(hits[best]!, slop)!;
    if (best === null || r < b! || (r === b && key(h) < key(hits[best]!))) best = i;
  });
  return best;
}
