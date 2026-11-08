// web/src/lib/meshpicker/nav.ts — arrow keys over the Suggested row and the grid, one index space (mesh-picker spec → Keyboard).

export interface GridShape {
  cols: number;
  /** Item counts of the stacked sections (Suggested, then the grid); each starts on a new row. */
  sections: number[];
  /** Rows a PageUp or PageDown moves. */
  page: number;
}

interface Row { start: number; length: number }

function rows(shape: GridShape): Row[] {
  const out: Row[] = []; let start = 0;
  for (const n of shape.sections) {
    for (let i = 0; i < n; i += shape.cols) out.push({ start: start + i, length: Math.min(shape.cols, n - i) });
    start += n;
  }
  return out;
}

/** The index a key moves to, or the same index for a key that does not move; -1 (nothing highlighted) goes to the first item. */
export function gridMove(index: number, key: string, shape: GridShape): number {
  const total = shape.sections.reduce((a, b) => a + b, 0);
  if (total === 0) return -1;
  if (index < 0 || index >= total) return ['ArrowDown', 'ArrowRight', 'ArrowUp', 'ArrowLeft', 'PageDown', 'PageUp', 'Home', 'End'].includes(key) ? (key === 'End' ? total - 1 : 0) : index;
  switch (key) {
    case 'ArrowLeft': return Math.max(0, index - 1);
    case 'ArrowRight': return Math.min(total - 1, index + 1);
    case 'Home': return 0;
    case 'End': return total - 1;
  }
  const by = { ArrowDown: 1, ArrowUp: -1, PageDown: shape.page, PageUp: -shape.page }[key];
  if (by === undefined) return index;
  const rs = rows(shape);
  const r = rs.findIndex((x) => index >= x.start && index < x.start + x.length);
  const col = index - rs[r]!.start;
  const to = rs[Math.max(0, Math.min(rs.length - 1, r + by))]!;
  return to.start + Math.min(col, to.length - 1);
}
