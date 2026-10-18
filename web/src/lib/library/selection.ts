// web/src/lib/library/selection.ts — the Library's multi-select over the assets in view. Immutable: each step returns a new selection.
import type { Guid } from '$lib/api/types';

/** The selected ids and the anchor a range click extends from (the last tile toggled). */
export interface Selection { ids: Set<Guid>; anchor: Guid | null }
export const NONE: Selection = { ids: new Set(), anchor: null };
export const clear = (): Selection => ({ ids: new Set(), anchor: null });

/** A plain or Ctrl/Cmd click toggles the tile; a Shift click (`range`) adds every tile from the anchor to it, in view order. */
export function click(s: Selection, order: Guid[], id: Guid, mods: { range?: boolean }): Selection {
  const from = s.anchor === null ? -1 : order.indexOf(s.anchor), to = order.indexOf(id);
  if (mods.range && from >= 0 && to >= 0) {
    const ids = new Set(s.ids);
    for (let i = Math.min(from, to); i <= Math.max(from, to); i++) ids.add(order[i]!);
    return { ids, anchor: s.anchor };
  }
  const ids = new Set(s.ids);
  if (ids.has(id)) ids.delete(id); else ids.add(id);
  return { ids, anchor: id };
}
export const selectAll = (s: Selection, order: Guid[]): Selection => ({ ids: new Set([...s.ids, ...order]), anchor: s.anchor });
/** Drops ids no longer in view (filtered out, deleted); the same object when nothing changed. */
export function keepOnly(s: Selection, order: Guid[]): Selection {
  const inView = new Set(order);
  if ([...s.ids].every((id) => inView.has(id)) && (s.anchor === null || inView.has(s.anchor))) return s;
  return { ids: new Set([...s.ids].filter((id) => inView.has(id))), anchor: s.anchor !== null && inView.has(s.anchor) ? s.anchor : null };
}
