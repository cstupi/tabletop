export type Point = { readonly x: number; readonly y: number };
export type Table = { readonly width: number; readonly length: number };
export type Viewport = { readonly width: number; readonly height: number };
/** Pixels per inch, and the screen position of the table's top-left corner (x = 0, y = length). */
export type View = { readonly scale: number; readonly panX: number; readonly panY: number };

export const MARGIN = 12;

export function fit(table: Table, viewport: Viewport): View {
  const usableWidth = Math.max(1, viewport.width - 2 * MARGIN);
  const usableHeight = Math.max(1, viewport.height - 2 * MARGIN);
  const scale = Math.min(usableWidth / table.width, usableHeight / table.length);
  return {
    scale,
    panX: (viewport.width - table.width * scale) / 2,
    panY: (viewport.height - table.length * scale) / 2,
  };
}

/** The only place y is flipped: the table's y grows away from the near edge, the canvas's grows downward. */
export function toScreen(view: View, table: Table, p: Point): Point {
  return { x: view.panX + p.x * view.scale, y: view.panY + (table.length - p.y) * view.scale };
}

export function toTable(view: View, table: Table, p: Point): Point {
  return { x: (p.x - view.panX) / view.scale, y: table.length - (p.y - view.panY) / view.scale };
}

export function zoomAt(view: View, table: Table, screen: Point, factor: number): View {
  const scale = Math.min(400, Math.max(0.25, view.scale * factor));
  const anchor = toTable(view, table, screen);
  return {
    scale,
    panX: screen.x - anchor.x * scale,
    panY: screen.y - (table.length - anchor.y) * scale,
  };
}

export function panBy(view: View, dx: number, dy: number): View {
  return { scale: view.scale, panX: view.panX + dx, panY: view.panY + dy };
}

/** Inches between grid lines: the smallest of 1, 3, 6, 12 that keeps lines at least 12 px apart. */
export function gridStep(view: View): number {
  for (const step of [1, 3, 6, 12]) if (step * view.scale >= 12) return step;
  return 12;
}

/** Tabletop.Geometry.Point2.Rotate: about the origin; yaw 0 faces +y, positive yaw turns counter-clockwise. */
export function rotate(p: Point, yawDegrees: number): Point {
  const radians = (yawDegrees * Math.PI) / 180;
  const s = Math.sin(radians);
  const c = Math.cos(radians);
  return { x: p.x * c - p.y * s, y: p.x * s + p.y * c };
}
