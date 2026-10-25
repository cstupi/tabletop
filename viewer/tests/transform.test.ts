import { describe, expect, it } from 'vitest';
import { MARGIN, fit, fitBox, gridStep, panBy, rotate, toScreen, toTable, zoomAt } from '../src/lib/board/transform';

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

describe('fitBox', () => {
  it('fitBox keeps a beside strip in view', () => {
    const viewport = { width: 800, height: 400 };
    const view = fitBox({ minX: 0, minY: 0, maxX: 100, maxY: 48 }, opr, viewport);
    for (const point of [{ x: 100, y: 24 }, { x: 0, y: 0 }, { x: 100, y: 48 }]) {
      const s = toScreen(view, opr, point);
      expect(s.x).toBeGreaterThanOrEqual(0);
      expect(s.x).toBeLessThanOrEqual(viewport.width);
      expect(s.y).toBeGreaterThanOrEqual(0);
      expect(s.y).toBeLessThanOrEqual(viewport.height);
    }
    expect(view.scale).toBeLessThan(fit(opr, viewport).scale);
    expect(toScreen(view, opr, { x: 100, y: 24 }).x).toBeCloseTo(viewport.width - MARGIN);
  });

  it('with the table as its box it is fit', () => {
    const viewport = { width: 800, height: 600 };
    const a = fitBox({ minX: 0, minY: 0, maxX: 72, maxY: 48 }, opr, viewport);
    const b = fit(opr, viewport);
    expect(a.scale).toBeCloseTo(b.scale);
    expect(a.panX).toBeCloseTo(b.panX);
    expect(a.panY).toBeCloseTo(b.panY);
  });
});
