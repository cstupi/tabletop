// viewer/src/lib/components/loading.ts
// What a wait says and when it shows (Loading.svelte draws it). The texts match the Godot client's.

/** A wait shorter than this shows nothing, so a cached load does not flash. */
export const SHOW_AFTER_MS = 200;

export interface Count { readonly done: number; readonly total: number }

/** "Loading table tools 3 of 9" with a count, "Loading table tools…" without one. */
export function countText(base: string, count: Count | null): string {
  return count !== null && count.total > 0 ? `${base} ${count.done} of ${count.total}` : `${base}…`;
}

export const tableToolsText = (count: Count | null): string => countText('Loading table tools', count);

/** Calls `show` after `ms` (at once for 0); the returned function cancels it. */
export function revealAfter(ms: number, show: () => void): () => void {
  if (ms <= 0) { show(); return () => {}; }
  const timer = setTimeout(show, ms);
  return () => clearTimeout(timer);
}
