// The HUD frame agreement: tests/fixtures/hudFrame.json is written by tests/Tabletop.Client.Tests/HudFrameViewerFixtureTests.cs from Godot's
// HudFrame over the window sizes and states the spec lists. Each case is computed again through hudFrame.ts and must agree.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { cursorPanelAt, hudFrameOf, overlaps, PLAY, SETUP, type HudPart, type Rect } from '../src/lib/play/hudFrame';

type R = [number, number, number, number];
interface Case {
  name: string; w: number; h: number; rows: 'play' | 'setup'; shown: HudPart[]; measured: Record<string, [number, number]>;
  slots: Record<string, R>; rects: Record<string, R>; order: HudPart[]; obstacles: R[]; covered: HudPart[];
}

const fixture = JSON.parse(readFileSync('tests/fixtures/hudFrame.json', 'utf8')) as { cases: Case[] };
const arr = (r: Rect): R => [r.x, r.y, r.w, r.h];
const obj = (m: ReadonlyMap<HudPart, Rect>): Record<string, R> => Object.fromEntries([...m].map(([k, v]) => [k, arr(v)]));

describe('hudFrame agreement', () => {
  it('has the spec\'s sizes and states', () => {
    expect(new Set(fixture.cases.map((c) => `${c.w}x${c.h}`))).toEqual(new Set(['1280x720', '1366x768', '1920x1080', '2560x1440']));
    expect(fixture.cases.some((c) => c.shown.includes('logPanel'))).toBe(true);
    expect(fixture.cases.some((c) => c.shown.includes('replay'))).toBe(true);
    expect(fixture.cases.some((c) => c.rows === 'setup')).toBe(true);
  });

  for (const c of fixture.cases)
    it(`${c.name} at ${c.w}x${c.h}`, () => {
      const f = hudFrameOf(c.w, c.h, new Set(c.shown), (part) => { const m = c.measured[part]; return m === undefined ? null : { w: m[0], h: m[1] }; },
        c.rows === 'play' ? PLAY : SETUP);
      expect(obj(f.slots)).toEqual(c.slots);
      expect(obj(f.rects)).toEqual(c.rects);
      expect(f.order).toEqual(c.order);
      expect(f.obstacles.map(arr)).toEqual(c.obstacles);
      expect([...f.covered].sort()).toEqual([...c.covered].sort());
    });
});

describe('cursorPanelAt (CursorPanel.Place)', () => {
  const view = { x: 0, y: 0, w: 1280, h: 720 };
  it('stands below and right of the pointer, and flips off a panel', () => {
    expect(cursorPanelAt({ x: 600, y: 400 }, { w: 100, h: 50 }, view, [])).toEqual({ x: 622, y: 422 });
    expect(cursorPanelAt({ x: 600, y: 400 }, { w: 100, h: 50 }, view, [{ x: 610, y: 410, w: 50, h: 50 }])).toEqual({ x: 484, y: 422 });
    expect(cursorPanelAt({ x: 1270, y: 700 }, { w: 100, h: 50 }, view, [])).toEqual({ x: 1154, y: 634 });
  });

  it('keeps off the frame\'s obstacles wherever a moved part stands', () => {
    const shown = new Set<HudPart>(['header', 'logButton', 'undoButton', 'decision']);
    const moved = PLAY.map((r) => r.part === 'logButton' ? { ...r, x: 'left' as const, y: 'bottom' as const } : r);
    const f = hudFrameOf(1280, 720, shown, null, moved);
    const log = f.rects.get('logButton')!;
    expect(arr(log)).toEqual([8, 674, 38, 38]);
    expect(arr(f.rects.get('undoButton')!)).toEqual([54, 674, 38, 38]);
    const at = cursorPanelAt({ x: log.x - 18, y: log.y - 18 }, { w: 30, h: 14 }, view, f.obstacles);
    expect(f.obstacles.some((o) => overlaps({ ...at, w: 30, h: 14 }, o))).toBe(false);
  });
});
