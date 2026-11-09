// viewer/src/lib/board/canvas.ts
// The canvas strokes draw.ts is built from: a shape's path in screen space, a line, a label with a halo, a shape's centre.

import type { Shape } from './frame';
import { type Point, type View, toScreen } from './transform';

type Table = { readonly width: number; readonly length: number };

export const LABEL_FONT = '600 12px system-ui, -apple-system, "Segoe UI", sans-serif';
export const SMALL_FONT = '600 10px system-ui, -apple-system, "Segoe UI", sans-serif';

export function line(ctx: CanvasRenderingContext2D, a: Point, b: Point): void {
  ctx.beginPath();
  ctx.moveTo(a.x, a.y);
  ctx.lineTo(b.x, b.y);
  ctx.stroke();
}

export function path(ctx: CanvasRenderingContext2D, shape: Shape, view: View, table: Table): void {
  ctx.beginPath();
  if (shape.kind === 'circle') {
    const p = toScreen(view, table, shape.centre);
    ctx.arc(p.x, p.y, shape.radius * view.scale, 0, 2 * Math.PI);
    return;
  }
  shape.points.forEach((point, i) => {
    const p = toScreen(view, table, point);
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  });
  ctx.closePath();
}

/** A band `width` px wide around the outside of `shape`, square-cornered on a polygon's corners: the shape's own area stays clear. */
export function ringOutside(ctx: CanvasRenderingContext2D, shape: Shape, view: View, table: Table, width: number): void {
  ctx.save();
  path(ctx, shape, view, table);
  ctx.rect(-1e6, -1e6, 2e6, 2e6);
  ctx.clip('evenodd');
  path(ctx, shape, view, table);
  ctx.lineWidth = 2 * width;
  ctx.lineJoin = 'miter';
  ctx.stroke();
  ctx.restore();
}

export function circle(ctx: CanvasRenderingContext2D, at: Point, radius: number): void {
  ctx.beginPath();
  ctx.arc(at.x, at.y, radius, 0, 2 * Math.PI);
}

/** Text centred at `at`, in `colour` over a soft halo of `halo`, so it reads over any fill. */
export function haloText(
  ctx: CanvasRenderingContext2D, text: string, at: Point, colour: string, halo: string, font = LABEL_FONT,
): void {
  ctx.font = font;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  ctx.lineWidth = 3.5;
  ctx.strokeStyle = halo;
  ctx.strokeText(text, at.x, at.y);
  ctx.fillStyle = colour;
  ctx.fillText(text, at.x, at.y);
}

export function shapeCentre(shape: Shape): Point {
  if (shape.kind === 'circle') return shape.centre;
  const n = shape.points.length;
  return {
    x: shape.points.reduce((s, p) => s + p.x, 0) / n,
    y: shape.points.reduce((s, p) => s + p.y, 0) / n,
  };
}
