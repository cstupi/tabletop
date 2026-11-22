import { describe, expect, it } from 'vitest';
import type { Held } from '../src/lib/play/modelDrag';
import { labelOverlay, landingOverlay, landingRings, pathsOverlay, rulerLines, rulerOverlay, violationLabels, allowanceOverlay } from '../src/lib/play/moveOverlays';
import { footprintColour, lineColour } from '../src/lib/play/movePaths';
import { read } from '../src/lib/play/moveRuler';
import { Violations } from '../src/lib/play/violations';

const O = { x: 0, y: 0 };

describe('move overlays', () => {
  it('rulerLines read RulerNode\'s text and turn red out of range', () => {
    const one = (x: number) => rulerLines([{ from: O, to: { x, y: 0 }, readout: read(O, { x, y: 0 }, 7.5) }])[0];
    expect(one(6)).toMatchObject({ text: '6.0"  (1.5" left)', outOfRange: false });
    expect(one(7.5)).toMatchObject({ text: '7.5"  (0.0" left)', outOfRange: false });
    expect(one(9)).toMatchObject({ text: '9.0"  (0.0" left)', outOfRange: true });
  });

  it('a unit drag reads once, on the model that moved farthest; every model keeps its line', () => {
    const lines = rulerLines([4, 6.5, 5, 6, 6.2].map((d, i) => {
      const from = { x: 2 * i, y: 0 }, to = { x: 2 * i, y: d };
      return { from, to, readout: read(from, to, 6) };
    }));
    expect(lines).toHaveLength(5);
    expect(lines.map((l) => l.text)).toEqual(['', '6.5"  (0.0" left)', '', '', '']);
    expect(lines.map((l) => l.outOfRange)).toEqual([false, true, false, false, true]);
  });

  it('a landing ring shows only while drawn and landing differ', () => {
    const held = {
      ids: ['a', 'b', 'c'], grabs: new Map(), origins: new Map(), yaws: new Map(), rows: 0,
      ends: new Map([
        ['a', { id: 'a', drawn: { x: 5, y: 5, z: 0.5 }, lands: { x: 5, y: 5, z: 0 } }],       // lifted over something
        ['b', { id: 'b', drawn: { x: 8, y: 8, z: 0 }, lands: { x: 8, y: 8, z: 0 } }],         // lands where it is drawn
        ['c', { id: 'c', drawn: { x: 9, y: 9, z: 0 }, lands: { x: 10, y: 9, z: 0 } }],        // slid aside
      ]),
    } as unknown as Held;
    expect(landingRings(held, (id) => (id === 'a' ? 0.5 : 1))).toEqual([
      { centre: { x: 5, y: 5 }, radius: 0.5 }, { centre: { x: 10, y: 9 }, radius: 1 },
    ]);
  });

  it('violation labels name each reason once over its first model, held first', () => {
    const v = new Violations();
    v.measure(['a', 'b', 'c'], new Map([['a', 'overlaps a base'], ['b', 'overlaps a base'], ['c', 'path crosses a model']]));
    const centres: Record<string, { x: number; y: number }> = { a: { x: 1, y: 1 }, b: { x: 2, y: 2 }, c: { x: 3, y: 3 } };
    expect(violationLabels(v, ['a', 'b', 'c'], (id) => centres[id] ?? null)).toEqual([
      { modelId: 'a', at: { x: 1, y: 1 }, text: 'On a base' }, { modelId: 'c', at: { x: 3, y: 3 }, text: 'Through a model' },
    ]);
    expect(violationLabels(v, ['b', 'c', 'a'], (id) => centres[id] ?? null)[0]).toEqual({ modelId: 'b', at: { x: 2, y: 2 }, text: 'On a base' });
    expect(violationLabels(v, ['a', 'c'], (id) => (id === 'a' ? null : centres[id]!))).toEqual([{ modelId: 'c', at: { x: 3, y: 3 }, text: 'Through a model' }]);
  });

  it('the overlays draw in table inches through the projection', () => {
    const calls: string[] = [];
    const ctx = new Proxy({}, { get: (_t, k) => (typeof k === 'string' && ['arc', 'moveTo', 'lineTo', 'fillText'].includes(k) ? (...a: unknown[]) => calls.push(`${k}:${a.join(',')}`) : () => {}), set: () => true }) as unknown as CanvasRenderingContext2D;
    const project = (p: { x: number; y: number }) => ({ x: p.x * 10, y: p.y * 10 });
    allowanceOverlay([{ centre: { x: 1, y: 2 }, radius: 3 }], '#fff')(ctx, project, 10);
    rulerOverlay([{ from: O, to: { x: 4, y: 0 }, text: '4.0"', outOfRange: false }])(ctx, project, 10);
    landingOverlay([{ centre: { x: 1, y: 1 }, radius: 1 }])(ctx, project, 10);
    expect(calls).toContain('arc:10,20,30,0,6.283185307179586');
    expect(calls).toContain('lineTo:40,0');
    expect(calls).toContain('fillText:4.0",20,-6');
  });

  it("a model's labels stack a step apart over it, and two models' labels never meet", () => {
    const texts: { text: string; x: number; y: number }[] = [];
    const ctx = new Proxy({}, {
      get: (_t, k) => (k === 'fillText' ? (text: string, x: number, y: number) => texts.push({ text, x, y }) : k === 'measureText' ? (t: string) => ({ width: t.length * 7 }) : () => {}),
      set: () => true,
    }) as unknown as CanvasRenderingContext2D;
    labelOverlay([
      { modelId: 'a', at: { x: 20, y: 20 }, text: 'Out of coherency' }, { modelId: 'a', at: { x: 20, y: 20 }, text: 'Dangerous terrain test' },
      { modelId: 'b', at: { x: 21, y: 20 }, text: 'On a base' },
    ], '#fff')(ctx, (p) => ({ x: p.x * 10, y: p.y * 10 }), 10);
    const [first, second, other] = texts;
    expect(first!.x).toBe(second!.x);
    expect(second!.y - first!.y).toBe(13 + 4);
    expect(Math.abs(other!.y - first!.y) >= 13 || Math.abs(other!.y - second!.y) >= 13).toBe(true);
    expect([first, second].every((t) => Math.abs(other!.y - t!.y) >= 13 || Math.abs(other!.x - t!.x) * 2 >= (t!.text.length + other!.text.length) * 7)).toBe(true);
  });

  it('a path draws its route and each footprint, a dangerous one in the dangerous colour', () => {
    const strokes: string[] = []; let style = '';
    const ctx = new Proxy({}, {
      get: (_t, k) => (k === 'stroke' ? () => strokes.push(style) : () => {}),
      set: (_t, k, v) => { if (k === 'strokeStyle') style = String(v); return true; },
    }) as unknown as CanvasRenderingContext2D;
    const start = { body: { base: { shape: 'circle' as const, a: 1, b: 1 }, height: 1 }, placement: { position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 } } };
    pathsOverlay([{ modelId: 'm', start, route: [O, { x: 2, y: 0 }], footprints: [{ at: O, dangerous: false }, { at: { x: 2, y: 0 }, dangerous: true }], dangerous: ['v'] }],
      '#3366e6')((ctx), (p) => p, 10);
    expect(strokes).toEqual([lineColour('#3366e6'), footprintColour({ at: O, dangerous: false }, '#3366e6'), footprintColour({ at: O, dangerous: true }, '#3366e6')]);
  });
});
