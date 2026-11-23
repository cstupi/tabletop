import { describe, expect, it } from 'vitest';
import { draw } from '../src/lib/board/draw';
import type { ModelShape, Scene } from '../src/lib/board/frame';
import { BOARD_DEFAULTS } from '../src/lib/board/style';

type Call = { readonly op: string; readonly args: readonly unknown[]; readonly strokeStyle: unknown };

/** A canvas context that records each call with the stroke colour current at the time. */
function recorder(): { ctx: CanvasRenderingContext2D; calls: Call[] } {
  const calls: Call[] = [];
  const state: Record<string, unknown> = {};
  const ctx = new Proxy(state, {
    get: (target, key: string) => key in target ? target[key]
      : key === 'measureText' ? () => ({ width: 0 })
      : (...args: unknown[]) => { calls.push({ op: key, args, strokeStyle: target.strokeStyle }); },
    set: (target, key: string, value) => { target[key] = value; return true; },
  }) as unknown as CanvasRenderingContext2D;
  return { ctx, calls };
}

const model = (id: string, x: number, activated: boolean): ModelShape => ({
  id, unitId: 'u1', unitName: 'Rifles', label: id, teamId: 't1', teamOrdinal: 1,
  centre: { x, y: 5 }, shape: { kind: 'circle', centre: { x, y: 5 }, radius: 0.5 },
  facing: null, wounds: 0, spellTokens: 0, conditions: [], elevated: false, activated, z: 0,
  circle: { kind: 'circle', centre: { x, y: 5 }, radius: 0.5 },
});

const scene = (models: ModelShape[]): Scene => ({ table: { width: 72, length: 48 }, terrain: [], zones: [], markers: [], models, offTable: [] });
const view = { scale: 20, panX: 0, panY: 0 };
const options = { selected: new Set<string>(), models: 'bases' as const, showGrid: false, showZones: false, showLabels: false };

describe('the activated band', () => {
  it('draws every band before any base, so a band never covers a neighbour in base contact', () => {
    const { ctx, calls } = recorder();
    draw(ctx, scene([model('a', 5, true), model('b', 6, true)]), view, { width: 800, height: 600 }, options);
    const bands = calls.flatMap((c, i) => (c.op === 'stroke' && c.strokeStyle === BOARD_DEFAULTS.activated ? [i] : []));
    const fills = calls.flatMap((c, i) => (c.op === 'fill' ? [i] : []));
    expect(bands).toHaveLength(2);
    expect(Math.max(...bands)).toBeLessThan(Math.min(...fills));
  });

  it('pushes the pick ring and the Ask glow beyond the band, so each stays readable', () => {
    const radii = (activated: boolean, picked: boolean, glowing: boolean): number[] => {
      const { ctx, calls } = recorder();
      draw(ctx, scene([model('a', 5, activated)]), view, { width: 800, height: 600 },
        { ...options, selected: new Set(picked ? ['a'] : []), glow: glowing ? () => 'hovered' as const : undefined });
      return calls.filter((c) => c.op === 'arc').map((c) => c.args[2] as number).slice(picked && glowing ? -2 : -1);   // the glow and pick rings are the last arcs
    };
    const base = 0.5 * view.scale, band = 0.2 * view.scale;
    const [picked] = radii(true, true, false);
    expect(picked! - 1).toBeGreaterThanOrEqual(base + band);          // the 2 px pick ring's inner edge clears the band
    const [glow, pickedOverGlow] = radii(true, true, true);
    expect(glow! - 1.5).toBeGreaterThanOrEqual(base + band);          // the 3 px glow's inner edge clears the band
    expect(pickedOverGlow!).toBeGreaterThan(glow!);
    expect(radii(false, true, false)[0]).toBe(base + 2.5);            // no band: the pick ring where it was
  });
});

describe('rings round an oval base', () => {
  // A 60 × 35 mm oval as the frame builds it: 64 points about (x, 5), the long axis along y.
  const oval = (id: string, x: number): ModelShape => {
    const points = Array.from({ length: 64 }, (_, i) => { const t = (2 * Math.PI * i) / 64; return { x: x + 0.689 * Math.cos(t), y: 5 + 1.181 * Math.sin(t) }; });
    return { ...model(id, x, false), shape: { kind: 'polygon', points }, circle: { kind: 'circle', centre: { x, y: 5 }, radius: 1.181 } };
  };

  it('drawing bases, the pick ring and the glow follow the oval, so ovals abreast 1.58" apart never ring each other', () => {
    const { ctx, calls } = recorder();
    draw(ctx, scene([oval('a', 5), oval('b', 6.578)]), view, { width: 800, height: 600 },
      { ...options, selected: new Set(['a', 'b']), glow: () => 'hovered' as const });
    expect(calls.filter((c) => c.op === 'arc')).toHaveLength(0);
    // Every ring point of model a lies left of the gap between the two, at most the glow's and the pick ring's 10 px past the base's half width.
    const xs = calls.filter((c) => c.op === 'lineTo' || c.op === 'moveTo').map((c) => c.args[0] as number);
    const halfway = ((5 + 6.578) / 2) * view.scale;
    expect(xs.filter((x) => x < halfway).every((x) => x <= (5 + 0.689) * view.scale + 10)).toBe(true);
  });

  it('drawing circles, the rings stay round the circle', () => {
    const { ctx, calls } = recorder();
    draw(ctx, scene([oval('a', 5)]), view, { width: 800, height: 600 }, { ...options, models: 'circles', selected: new Set(['a']) });
    expect(calls.filter((c) => c.op === 'arc').at(-1)!.args[2]).toBe(1.181 * view.scale + 2.5);
  });
});
