import { describe, expect, it } from 'vitest';
import type { Segment, XY } from '../src/lib/geometry';
import { LABEL_CANDIDATES, labelUnder, loopArea, placeRingLabels, ringAnchor, ringLook, ringLoops, ringShapeOf, type Rect } from '../src/lib/play/ringShape';

/** A circle of radius r about c as n segments, counter-clockwise, as RangeOutline draws a lone round base. */
function circle(c: XY, r: number, n = 96): Segment[] {
  const at = (k: number): XY => ({ x: c.x + r * Math.cos(2 * Math.PI * k / n), y: c.y + r * Math.sin(2 * Math.PI * k / n) });
  return Array.from({ length: n }, (_, k) => ({ a: at(k), b: at(k + 1 === n ? 0 : k + 1) }));
}

describe('RingShape', () => {
  it('one ring is one counter-clockwise loop', () => {
    const loops = ringLoops(circle({ x: 10, y: 10 }, 6.5));
    expect(loops).toHaveLength(1);
    expect(loops[0]).toHaveLength(96);
    expect(loopArea(loops[0]!)).toBeCloseTo(Math.PI * 6.5 * 6.5, 0);
  });

  it('segments out of order still close, and two rings apart are two loops', () => {
    expect(ringLoops([...circle({ x: 10, y: 10 }, 6.5)].reverse())).toHaveLength(1);
    expect(ringLoops([...circle({ x: 10, y: 10 }, 6.5), ...circle({ x: 40, y: 10 }, 6.5)])).toHaveLength(2);
  });

  it('label candidates are spread evenly along the loops', () => {
    const s = ringShapeOf(circle({ x: 10, y: 10 }, 6.5));
    expect(s.candidates).toHaveLength(LABEL_CANDIDATES);
    for (const q of s.candidates) expect(Math.hypot(q.x - 10, q.y - 10)).toBeCloseTo(6.5, 1);
  });

  it('a kind sets its look', () => {
    expect(ringLook('charge').fillAlpha).toBeGreaterThan(ringLook('shoot').fillAlpha);
    expect([ringLook('charge').icon, ringLook('shoot').icon, ringLook('range').icon]).toEqual(['charge', 'shoot', 'shoot']);
  });
});

describe('RingLabels', () => {
  const view: Rect = { x: 0, y: 0, w: 1280, h: 720 };
  const overlaps = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

  it('the anchor is the lowest point on the screen; a ring off the screen anchors at its nearest point pulled inside', () => {
    expect(ringAnchor([{ x: 100, y: 200 }, { x: 300, y: 600 }, { x: 500, y: 400 }, { x: 700, y: 900 }], view)).toEqual({ x: 300, y: 600 });
    expect(ringAnchor([{ x: 640, y: 800 }, { x: 640, y: 1000 }], view, 8)).toEqual({ x: 640, y: 712 });
    expect(ringAnchor([], view)).toBeNull();
  });

  it('two labels at one anchor are placed apart, the lower keeping its place and the other above it', () => {
    const a = labelUnder({ x: 640, y: 400 }, 120, 30), b = labelUnder({ x: 650, y: 405 }, 100, 30);
    const [p, q] = placeRingLabels([a, b], view);
    expect(q).toEqual(b);
    expect(overlaps(p!, q!)).toBe(false);
    expect(p!.y + p!.h).toBeLessThanOrEqual(q!.y);
  });

  it('the label lowest on the screen keeps its place and the ones above stack up in order, whatever order they come in', () => {
    const inner = labelUnder({ x: 640, y: 445 }, 80, 40), outer = labelUnder({ x: 640, y: 470 }, 80, 40);
    for (const order of [[inner, outer], [outer, inner]]) {
      const placed = placeRingLabels(order, view);
      const [i, o] = order[0] === inner ? [placed[0]!, placed[1]!] : [placed[1]!, placed[0]!];
      expect(o).toEqual(outer);
      expect(i.y + i.h).toBeLessThanOrEqual(o.y);
    }
  });

  it('labels at the top go down, and one past the edge comes inside', () => {
    const placed = placeRingLabels([{ x: 600, y: 0, w: 100, h: 30 }, { x: 610, y: 5, w: 100, h: 30 }, { x: 1250, y: -20, w: 100, h: 30 }], view);
    for (const p of placed) expect(p.y >= 0 && p.y + p.h <= view.y + view.h).toBe(true);
    expect(placed[2]).toEqual({ x: 1180, y: 0, w: 100, h: 30 });
    for (let i = 0; i < placed.length; i++) for (let j = i + 1; j < placed.length; j++) expect(overlaps(placed[i]!, placed[j]!)).toBe(false);
  });
});
