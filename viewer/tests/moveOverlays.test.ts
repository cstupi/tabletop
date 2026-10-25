import { describe, expect, it } from 'vitest';
import type { Held } from '../src/lib/play/modelDrag';
import { labelOverlay, landingOverlay, landingRings, rulerLines, rulerOverlay, violationLabels, allowanceOverlay } from '../src/lib/play/moveOverlays';
import { read } from '../src/lib/play/moveRuler';
import { Violations } from '../src/lib/play/violations';

const O = { x: 0, y: 0 };

describe('move overlays', () => {
  it('rulerLines read RulerNode\'s text and turn red out of range', () => {
    const lines = rulerLines([
      { from: O, to: { x: 6, y: 0 }, readout: read(O, { x: 6, y: 0 }, 7.5) },
      { from: O, to: { x: 7.5, y: 0 }, readout: read(O, { x: 7.5, y: 0 }, 7.5) },
      { from: O, to: { x: 9, y: 0 }, readout: read(O, { x: 9, y: 0 }, 7.5) },
    ]);
    expect(lines[0]).toMatchObject({ text: '6.0"  (1.5" left)', outOfRange: false });
    expect(lines[1]).toMatchObject({ text: '7.5"  (0.0" left)', outOfRange: false });
    expect(lines[2]).toMatchObject({ text: '9.0"  (0.0" left)', outOfRange: true });
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
      { at: { x: 1, y: 1 }, text: 'overlaps a base' }, { at: { x: 3, y: 3 }, text: 'path crosses a model' },
    ]);
    expect(violationLabels(v, ['b', 'c', 'a'], (id) => centres[id] ?? null)[0]).toEqual({ at: { x: 2, y: 2 }, text: 'overlaps a base' });
    expect(violationLabels(v, ['a', 'c'], (id) => (id === 'a' ? null : centres[id]!))).toEqual([{ at: { x: 3, y: 3 }, text: 'path crosses a model' }]);
  });

  it('the overlays draw in table inches through the projection', () => {
    const calls: string[] = [];
    const ctx = new Proxy({}, { get: (_t, k) => (typeof k === 'string' && ['arc', 'moveTo', 'lineTo', 'fillText'].includes(k) ? (...a: unknown[]) => calls.push(`${k}:${a.join(',')}`) : () => {}), set: () => true }) as unknown as CanvasRenderingContext2D;
    const project = (p: { x: number; y: number }) => ({ x: p.x * 10, y: p.y * 10 });
    allowanceOverlay([{ centre: { x: 1, y: 2 }, radius: 3 }], '#fff')(ctx, project, 10);
    rulerOverlay([{ from: O, to: { x: 4, y: 0 }, text: '4.0"', outOfRange: false }])(ctx, project, 10);
    landingOverlay([{ centre: { x: 1, y: 1 }, radius: 1 }])(ctx, project, 10);
    labelOverlay([{ at: { x: 2, y: 2 }, text: 'x' }], '#fff')(ctx, project, 10);
    expect(calls).toContain('arc:10,20,30,0,6.283185307179586');
    expect(calls).toContain('lineTo:40,0');
    expect(calls).toContain('fillText:4.0",20,-6');
    expect(calls).toContain('fillText:x,20,4');
  });
});
