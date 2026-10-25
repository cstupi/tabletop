// viewer/src/lib/board/labelPlacement.ts
// Where an overlay's text goes on the canvas: wrapped to a readable width, at most a few lines, and moved sideways so the whole text stays
// inside the board's canvas. Screen pixels in and out.

import type { Point } from './transform';

/** Pixels kept clear at the canvas's left and right edges. */
export const LABEL_MARGIN = 6;
/** The widest line a label is wrapped to. */
export const LABEL_MAX_WIDTH = 360;
/** Lines past this end the last one with an ellipsis. */
export const LABEL_MAX_LINES = 3;

export type Measure = (text: string) => number;
export interface PlacedLine { readonly text: string; readonly x: number; readonly y: number }

const ELLIPSIS = '…';

/** The longest prefix of `text` that fits `width` with an ellipsis after it. */
function cut(text: string, measure: Measure, width: number): string {
  let end = text.length;
  while (end > 0 && measure(text.slice(0, end).trimEnd() + ELLIPSIS) > width) end--;
  return text.slice(0, end).trimEnd() + ELLIPSIS;
}

/** Words wrapped to `width`; a word wider than a line alone is cut; more than `maxLines` lines end the last with an ellipsis. */
export function wrapText(text: string, measure: Measure, width: number, maxLines = LABEL_MAX_LINES): string[] {
  const words = text.split(/\s+/).filter((w) => w.length > 0);
  const lines: string[] = [];
  let line = '';
  for (const word of words) {
    const next = line === '' ? word : `${line} ${word}`;
    if (measure(next) <= width) { line = next; continue; }
    if (line !== '') lines.push(line);
    line = measure(word) <= width ? word : cut(word, measure, width);
  }
  if (line !== '') lines.push(line);
  if (lines.length <= maxLines) return lines;
  const kept = lines.slice(0, maxLines);
  kept[maxLines - 1] = cut(`${kept[maxLines - 1]!} ${lines[maxLines]!}`, measure, width);
  return kept;
}

/**
 * A label anchored at `at`, centred on its x but moved so its widest line stays `LABEL_MARGIN` inside a canvas `viewportWidth` wide.
 * `grow: 'up'` puts the last line on the anchor and the others above it (text over a model); `'down'` puts the first line on the anchor and
 * the rest below. Each line's x is its centre, for `textAlign = 'center'`.
 */
export function placeLabel(
  text: string, at: Point, measure: Measure, viewportWidth: number,
  options: { readonly lineHeight: number; readonly grow: 'up' | 'down'; readonly maxWidth?: number },
): PlacedLine[] {
  const room = Math.max(1, viewportWidth - 2 * LABEL_MARGIN);
  const lines = wrapText(text, measure, Math.min(options.maxWidth ?? LABEL_MAX_WIDTH, room));
  if (lines.length === 0) return [];
  const half = Math.max(...lines.map(measure)) / 2;
  const x = half * 2 >= room ? viewportWidth / 2 : Math.min(Math.max(at.x, LABEL_MARGIN + half), viewportWidth - LABEL_MARGIN - half);
  const first = options.grow === 'up' ? at.y - (lines.length - 1) * options.lineHeight : at.y;
  return lines.map((line, i) => ({ text: line, x, y: first + i * options.lineHeight }));
}

/** The canvas's width in the pixels overlays draw in (the board scales its context by the device pixel ratio). */
export function viewportWidthOf(ctx: CanvasRenderingContext2D): number {
  const canvas = (ctx as { canvas?: { width?: unknown } }).canvas;
  const width = typeof canvas?.width === 'number' ? canvas.width : Number.POSITIVE_INFINITY;
  const scale = typeof ctx.getTransform === 'function' ? ctx.getTransform()?.a : undefined;
  return width / (typeof scale === 'number' && scale > 0 ? scale : 1);
}

/** The context's measure in its current font; a context that measures nothing counts 7 px a character. */
export function measureOf(ctx: CanvasRenderingContext2D): Measure {
  return (text) => {
    const w = (ctx.measureText?.(text) as TextMetrics | undefined)?.width;
    return typeof w === 'number' ? w : text.length * 7;
  };
}

/** placeLabel in the context's font and canvas, then each line drawn: a halo stroke first when `halo` is set. */
export function drawLabel(
  ctx: CanvasRenderingContext2D, text: string, at: Point, fill: string,
  options: { readonly lineHeight: number; readonly grow: 'up' | 'down'; readonly halo?: string; readonly maxWidth?: number },
): void {
  for (const line of placeLabel(text, at, measureOf(ctx), viewportWidthOf(ctx), options)) {
    if (options.halo !== undefined) { ctx.strokeStyle = options.halo; ctx.strokeText(line.text, line.x, line.y); }
    ctx.fillStyle = fill;
    ctx.fillText(line.text, line.x, line.y);
  }
}
