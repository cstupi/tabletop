import { describe, expect, it } from 'vitest';
import { MARGIN, fit, gridStep, panBy, rotate, toScreen, toTable, zoomAt } from '../src/lib/board/transform';

const opr = { width: 72, length: 48 };

describe('the view transform', () => {
  it('fits the table into the viewport with a margin and centres it', () => {
    const view = fit(opr, { width: 720 + 2 * MARGIN, height: 480 + 2 * MARGIN });
    expect(view.scale).toBeCloseTo(10);
    expect(view.panX).toBeCloseTo(MARGIN);
    expect(view.panY).toBeCloseTo(MARGIN);
  });

  it('flips y exactly once, so the table origin is the bottom-left corner on screen', () => {
    const view = fit(opr, { width: 720 + 2 * MARGIN, height: 480 + 2 * MARGIN });
    expect(toScreen(view, opr, { x: 0, y: 0 })).toEqual({ x: MARGIN, y: MARGIN + 480 });
    expect(toScreen(view, opr, { x: 72, y: 48 })).toEqual({ x: MARGIN + 720, y: MARGIN });
  });

  it('round-trips a point through screen space', () => {
    const view = panBy(zoomAt(fit(opr, { width: 800, height: 600 }), opr, { x: 400, y: 300 }, 2.5), 17, -9);
    const back = toTable(view, opr, toScreen(view, opr, { x: 13.5, y: 41.25 }));
    expect(back.x).toBeCloseTo(13.5);
    expect(back.y).toBeCloseTo(41.25);
  });

  it('zooms about the pointer, leaving the table point under it fixed', () => {
    const view = fit(opr, { width: 800, height: 600 });
    const cursor = { x: 300, y: 200 };
    const before = toTable(view, opr, cursor);
    const after = toTable(zoomAt(view, opr, cursor, 3), opr, cursor);
    expect(after.x).toBeCloseTo(before.x);
    expect(after.y).toBeCloseTo(before.y);
  });

  it('coarsens the grid as the table shrinks so the lines never crowd', () => {
    expect(gridStep({ scale: 20, panX: 0, panY: 0 })).toBe(1);
    expect(gridStep({ scale: 6, panX: 0, panY: 0 })).toBe(3);
    expect(gridStep({ scale: 3, panX: 0, panY: 0 })).toBe(6);
    expect(gridStep({ scale: 0.5, panX: 0, panY: 0 })).toBe(12);
  });

  it('rotates counter-clockwise from +y, as Point2.Rotate does', () => {
    const turned = rotate({ x: 0, y: 1 }, 90);
    expect(turned.x).toBeCloseTo(-1);
    expect(turned.y).toBeCloseTo(0);
  });
});
