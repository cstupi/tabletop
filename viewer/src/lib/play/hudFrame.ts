// viewer/src/lib/play/hudFrame.ts
// client/src/Core/HudFrame.cs and CursorPanel.cs: one table of HUD parts (anchor, size rule, stacking neighbour), the frame they make for a
// box size, the parts that show and their measured sizes, and the Obstacles every overlay over the board keeps off. The play page places
// each part from `rects`; tests/fixtures/hudFrame.json (written by the client tests) checks the two agree.

export interface Rect { readonly x: number; readonly y: number; readonly w: number; readonly h: number }

export type HudPart =
  | 'corner' | 'toasts' | 'header' | 'roster' | 'unitCard' | 'decision' | 'dice' | 'logButton' | 'undoButton' | 'logPanel' | 'banner'
  | 'endMessage' | 'replay' | 'loading' | 'topBar' | 'rail' | 'toolPanel' | 'table' | 'terrainCard' | 'hints';

export type HudX = 'left' | 'right' | 'centre' | 'stretch';
export type HudY = 'top' | 'bottom' | 'middle' | 'third';
export interface HudSize { readonly kind: 'fixed' | 'atLeast' | 'fill'; readonly n: number }

export const fixed = (n: number): HudSize => ({ kind: 'fixed', n });
export const atLeast = (n: number): HudSize => ({ kind: 'atLeast', n });
export const fill = (max = Infinity): HudSize => ({ kind: 'fill', n: max });

/** HudRow: see HudFrame.cs for each field's rule. */
export interface HudRow {
  readonly part: HudPart; readonly x: HudX; readonly y: HudY; readonly w: HudSize; readonly h: HudSize;
  readonly beside?: HudPart; readonly on?: HudPart; readonly in?: HudPart; readonly hiddenWhile?: HudPart; readonly keeps?: HudY;
  readonly gap?: number; readonly overlay?: boolean; readonly room?: boolean; readonly always?: boolean;
}

/** Fits the part to its slot and returns its measured size, or null to take the slot as it is. */
export type HudFit = (part: HudPart, slot: Rect) => { w: number; h: number } | null;

export const M = 8, COLUMN = 420, CORNER_H = 40, HEADER_W = 760, HEADER_H = 96, TOAST_W = 360, TOAST_H = 120, DECISION_W = 480,
  DICE_H = 96, REPLAY_H = 112, LOG_W = 640, LOG_MAX_H = 720, CORNER_BUTTON = 38, LOADING_H = 36, LOADING_W = 280, BANNER_H = 136, END_W = 480,
  END_H = 360, TOP_BAR_H = 56, RAIL_W = 56, PANEL_W = 360, HINT_H = 32, SETUP_CORNER_W = 360,
  CARD_W = 480;   // the unit card, wider than the roster over it, so its weapons and rules wrap less and it stands lower

export const PLAY: readonly HudRow[] = [
  { part: 'corner', x: 'left', y: 'top', w: fixed(COLUMN), h: fixed(CORNER_H) },
  { part: 'logButton', x: 'right', y: 'top', w: atLeast(CORNER_BUTTON), h: atLeast(CORNER_BUTTON), always: true },
  { part: 'undoButton', x: 'right', y: 'top', w: atLeast(CORNER_BUTTON), h: atLeast(CORNER_BUTTON), beside: 'logButton', always: true },
  { part: 'toasts', x: 'right', y: 'top', w: fixed(TOAST_W), h: fixed(TOAST_H), overlay: true, always: true },
  { part: 'header', x: 'centre', y: 'top', w: fill(HEADER_W), h: atLeast(HEADER_H), always: true },
  { part: 'replay', x: 'left', y: 'bottom', w: fixed(COLUMN), h: atLeast(REPLAY_H) },
  { part: 'roster', x: 'left', y: 'top', w: fixed(COLUMN), h: fill() },
  { part: 'unitCard', x: 'left', y: 'top', w: fixed(CARD_W), h: fill(), on: 'roster', keeps: 'bottom' },
  { part: 'loading', x: 'centre', y: 'bottom', w: fixed(LOADING_W), h: fixed(LOADING_H), always: true },
  { part: 'decision', x: 'right', y: 'bottom', w: fixed(DECISION_W), h: fill() },
  { part: 'dice', x: 'right', y: 'bottom', w: fixed(DECISION_W), h: fixed(DICE_H), on: 'decision', hiddenWhile: 'logPanel' },
  { part: 'logPanel', x: 'right', y: 'top', w: fixed(LOG_W), h: fill(LOG_MAX_H), on: 'decision' },
  { part: 'banner', x: 'stretch', y: 'third', w: fill(), h: fixed(BANNER_H), overlay: true, always: true },
  { part: 'endMessage', x: 'centre', y: 'middle', w: fixed(END_W), h: fixed(END_H), overlay: true, always: true },
];

export const SETUP: readonly HudRow[] = [
  { part: 'topBar', x: 'stretch', y: 'top', w: fill(), h: atLeast(TOP_BAR_H), gap: 0, always: true },
  { part: 'hints', x: 'stretch', y: 'bottom', w: fill(), h: atLeast(HINT_H), gap: 0 },
  { part: 'corner', x: 'left', y: 'middle', w: fixed(SETUP_CORNER_W), h: fixed(CORNER_H), in: 'topBar', always: true },
  { part: 'rail', x: 'left', y: 'top', w: fixed(RAIL_W), h: fill(), gap: 0 },
  { part: 'toolPanel', x: 'left', y: 'top', w: fixed(PANEL_W), h: fill(), beside: 'rail', gap: 0 },
  { part: 'table', x: 'left', y: 'top', w: fill(), h: fill(), beside: 'toolPanel', gap: 0, room: true, always: true },
  { part: 'loading', x: 'centre', y: 'bottom', w: fixed(LOADING_W), h: fixed(LOADING_H), in: 'table', always: true },
  { part: 'terrainCard', x: 'right', y: 'top', w: fixed(COLUMN), h: fill(), in: 'table', keeps: 'bottom', always: true },
  { part: 'toasts', x: 'right', y: 'top', w: fixed(TOAST_W), h: fixed(TOAST_H), in: 'table', beside: 'terrainCard', overlay: true, always: true },
];

export interface HudFrame {
  readonly slots: ReadonlyMap<HudPart, Rect>;
  readonly rects: ReadonlyMap<HudPart, Rect>;
  readonly order: readonly HudPart[];
  /** The shown panels as they stand: every overlay over the board keeps off them. */
  readonly obstacles: readonly Rect[];
  /** The parts that give their slot up while their hiddenWhile part shows (the dice tray under the open log). */
  readonly covered: ReadonlySet<HudPart>;
}

export const EMPTY_FRAME: HudFrame = { slots: new Map(), rects: new Map(), order: [], obstacles: [], covered: new Set() };

const right = (r: Rect): number => r.x + r.w;
const bottom = (r: Rect): number => r.y + r.h;
const spansX = (a: Rect, b: Rect): boolean => a.x < right(b) && b.x < right(a);
export const overlaps = (a: Rect, b: Rect): boolean => spansX(a, b) && a.y < bottom(b) && b.y < bottom(a);
const gapOf = (r: HudRow): number => r.gap ?? M;

/** HudFrame.Of. */
export function hudFrameOf(w: number, h: number, shown: ReadonlySet<HudPart>, fit: HudFit | null = null, rows: readonly HudRow[] = PLAY): HudFrame {
  const window: Rect = { x: 0, y: 0, w, h };
  const covered = new Set(rows.filter((r) => r.hiddenWhile !== undefined && shown.has(r.hiddenWhile)).map((r) => r.part));
  const visible = rows.filter((r) => (r.always === true || shown.has(r.part)) && !covered.has(r.part));
  const byPart = new Map(rows.map((r) => [r.part, r] as const));
  const slots = new Map<HudPart, Rect>(), rects = new Map<HudPart, Rect>(), placed: HudRow[] = [];

  visible.forEach((original, i) => {
    let row = original;
    const g = gapOf(row);
    let beside: Rect | null = null;
    for (let b = row.beside; b !== undefined; b = byPart.get(b)?.beside) {
      const at = rects.get(b);
      if (at !== undefined) { beside = at; const n = byPart.get(b)!; row = { ...row, x: n.x, y: n.y }; break; }
    }
    const c = row.in !== undefined ? rects.get(row.in) ?? window : window;
    const peers = placed.filter((p) => p.in === row.in && p.room !== true).map((p) => ({ row: p, rect: rects.get(p.part)! }));
    const solid = peers.filter((p) => p.row.overlay !== true);

    let measuredW: number | null = null, measuredH: number | null = null;
    let width = row.w.kind === 'fill' ? 0 : row.w.n;
    if (row.w.kind === 'atLeast' || row.h.kind === 'atLeast') {
      const probe = fit?.(row.part, { x: c.x + g, y: c.y + g, w: row.w.kind === 'fill' ? across(row, c, g, beside, peers).w : width, h: row.h.kind === 'fill' ? 0 : row.h.n }) ?? null;
      if (probe !== null) { measuredW = probe.w; measuredH = probe.h; }
      if (row.w.kind === 'atLeast') width = Math.ceil(Math.max(row.w.n, measuredW ?? 0));
    }
    let x: number;
    if (row.w.kind === 'fill' || row.x === 'centre' || row.x === 'stretch') ({ x, w: width } = across(row, c, g, beside, peers, width));
    else x = side(row, c, g, beside, width);
    const cols: Rect = { x, y: 0, w: width, h: 1 };

    const tops = solid.filter((p) => p.row.y === 'top' && spansX(p.rect, cols)).map((p) => bottom(p.rect) + g);
    const bottoms = solid.filter((p) => p.row.y === 'bottom' && spansX(p.rect, cols)).map((p) => p.rect.y - g);
    const top = Math.max(c.y + g, ...tops), foot = Math.min(bottom(c) - g, ...bottoms);
    const height = row.h.kind === 'fixed' ? row.h.n
      : row.h.kind === 'atLeast' ? Math.ceil(Math.max(row.h.n, measuredH ?? 0))
        : share(row, visible.slice(i + 1).filter((r) => r.in === row.in), foot - top, g);
    const y = row.y === 'top' ? top : row.y === 'bottom' ? foot - height
      : row.y === 'middle' ? Math.floor(c.y + c.h / 2 - height / 2) : Math.floor(c.y + c.h / 3 - height / 2);
    const slot: Rect = { x, y, w: width, h: height };
    slots.set(row.part, slot);

    let rect = slot;
    const fitted = row.h.kind !== 'atLeast' ? fit?.(row.part, slot) ?? null : null;
    if (fitted !== null && fitted.h < slot.h) {
      const mh = Math.max(0, Math.ceil(fitted.h));
      const keeps = row.keeps ?? row.y;
      rect = keeps === 'bottom' ? { ...slot, y: bottom(slot) - mh, h: mh }
        : keeps === 'middle' ? { ...slot, y: Math.floor(c.y + c.h / 2 - mh / 2), h: mh } : { ...slot, h: mh };
    }
    rects.set(row.part, rect);
    placed.push(row);
  });
  const obstacles = placed.filter((r) => r.overlay !== true && r.room !== true && shown.has(r.part)).map((r) => rects.get(r.part)!);
  return { slots, rects, order: placed.map((r) => r.part), obstacles, covered };
}

function side(row: HudRow, c: Rect, g: number, beside: Rect | null, width: number): number {
  if (row.x === 'left') return beside !== null ? right(beside) + g : c.x + g;
  return Math.max(c.x + g, (beside !== null ? beside.x - g : right(c) - g) - width);
}

function across(row: HudRow, c: Rect, g: number, beside: Rect | null, peers: readonly { row: HudRow; rect: Rect }[], width = 0): { x: number; w: number } {
  const max = row.w.kind === 'fill' ? row.w.n : width;
  if (row.x === 'stretch') return { x: c.x + g, w: Math.min(max, c.w - 2 * g) };
  if (row.x === 'left') { const x = beside !== null ? right(beside) + g : c.x + g; return { x, w: Math.min(max, right(c) - g - x) }; }
  if (row.x === 'right') { const r = beside !== null ? beside.x - g : right(c) - g; const w = Math.min(max, r - (c.x + g)); return { x: r - w, w }; }
  const minH = row.h.kind === 'fill' ? 0 : row.h.n;
  const [r0, r1] = row.y === 'top' ? [c.y + g, c.y + g + minH] : row.y === 'bottom' ? [bottom(c) - g - minH, bottom(c) - g]
    : row.y === 'middle' ? [c.y + c.h / 2 - minH / 2, c.y + c.h / 2 + minH / 2] : [c.y + c.h / 3 - minH / 2, c.y + c.h / 3 + minH / 2];
  const centre = c.x + c.w / 2;
  let left = c.x + g, rightEdge = right(c) - g;
  if (row.overlay !== true)
    for (const { rect: p } of peers) {
      if (!(p.y < r1 && r0 < bottom(p))) continue;
      if (p.x + p.w / 2 < centre) left = Math.max(left, right(p) + g); else rightEdge = Math.min(rightEdge, p.x - g);
    }
  const cw = Math.min(max, rightEdge - left);
  return { x: Math.max(left, Math.min(Math.floor(centre - cw / 2), rightEdge - cw)), w: cw };
}

function share(row: HudRow, later: readonly HudRow[], room: number, g: number): number {
  const on = later.filter((r) => r.on === row.part);
  const k = on.filter((r) => r.h.kind === 'fill').length;
  const carved = on.filter((r) => r.h.kind !== 'fill').reduce((s, r) => s + r.h.n + g, 0);
  const portion = k > 0 ? Math.floor((room - k * g) / (k + 1)) : room;
  return Math.max(0, Math.min(row.h.n, portion - carved));
}

/** CursorPanel.Place: beside the pointer, inside the view and off every obstacle. */
export function cursorPanelAt(mouse: { x: number; y: number }, size: { w: number; h: number }, view: Rect, obstacles: readonly Rect[]): { x: number; y: number } {
  const AFTER = 22, BEFORE = 16, EDGE = 4;
  const r0 = mouse.x + AFTER, l0 = mouse.x - BEFORE - size.w, below = mouse.y + AFTER, above = mouse.y - BEFORE - size.h;
  const clear = (x: number, y: number): boolean => {
    const r = { x, y, w: size.w, h: size.h };
    return r.x >= view.x + EDGE && r.y >= view.y + EDGE && right(r) <= right(view) && bottom(r) <= bottom(view) && !obstacles.some((o) => overlaps(r, o));
  };
  for (const [x, y] of [[r0, below], [l0, below], [r0, above], [l0, above]] as const) if (clear(x, y)) return { x, y };
  const first = { x: r0, y: below, w: size.w, h: size.h };
  let best: { x: number; y: number } | null = null, cost = Infinity;
  for (const o of obstacles.filter((o) => overlaps(first, o)))
    for (const [x, y] of [[o.x - EDGE - size.w, below], [right(o) + EDGE, below], [r0, bottom(o) + EDGE], [r0, o.y - EDGE - size.h]] as const) {
      const c = Math.abs(x - r0) + Math.abs(y - below);
      if (clear(x, y) && c < cost) { best = { x, y }; cost = c; }
    }
  if (best !== null) return best;
  const fx = r0 + size.w > right(view) ? l0 : r0, fy = below + size.h > bottom(view) ? above : below;
  return { x: Math.max(view.x + EDGE, fx), y: Math.max(view.y + EDGE, fy) };
}
