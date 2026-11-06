import { describe, expect, it } from 'vitest';
import { DRAG_THRESHOLD_PX, IDLE_POINTER, step, type PointerEffect, type PointerInput, type PointerState } from '../src/lib/board/pointer';
import { NO_MODIFIERS, type BoardHit } from '../src/lib/board/interaction';

const hit: BoardHit = { kind: 'model', id: 'm1' };
const pickModel = (at: { x: number; y: number }): BoardHit | null => (at.x < 10 ? hit : null);
const never = () => false;
const always = () => true;

/** Runs inputs through the machine, returning every effect in order and the final state. */
function run(inputs: PointerInput[], takesDrag: Parameters<typeof step>[3] = never, takesBox?: Parameters<typeof step>[4]) {
  let state: PointerState = IDLE_POINTER;
  const effects: PointerEffect[] = [];
  for (const input of inputs) {
    const result = step(state, input, pickModel, takesDrag, takesBox);
    state = result.state;
    effects.push(...result.effects);
  }
  return { state, effects };
}
const down = (x: number, y = 0, button: 0 | 2 = 0): PointerInput => ({ type: 'down', screen: { x: x * 10, y }, at: { x, y }, button, mods: NO_MODIFIERS });
const move = (x: number, y = 0): PointerInput => ({ type: 'move', screen: { x: x * 10, y }, at: { x, y }, mods: NO_MODIFIERS });
const up = (x: number, y = 0): PointerInput => ({ type: 'up', screen: { x: x * 10, y }, at: { x, y }, mods: NO_MODIFIERS });

describe('the pointer state machine', () => {
  it('a still click selects what is under it', () => {
    expect(run([down(1), up(1)]).effects).toMatchObject([{ type: 'select', hit, mods: NO_MODIFIERS }]);
    expect(run([down(20), up(20)]).effects).toMatchObject([{ type: 'select', hit: null, mods: NO_MODIFIERS }]);
  });

  it('a wobble under the threshold is still a click', () => {
    const wobble: PointerInput = { type: 'move', screen: { x: 10 + DRAG_THRESHOLD_PX - 1, y: 0 }, at: { x: 1, y: 0 }, mods: NO_MODIFIERS };
    expect(run([down(1), wobble, up(1)]).effects.map((e) => e.type)).toEqual(['select']);
  });

  it('a press on empty table moved 3 px pans', () => {
    const moved: PointerInput = { type: 'move', screen: { x: 203, y: 0 }, at: { x: 20.3, y: 0 }, mods: NO_MODIFIERS };
    const { effects, state } = run([down(20), moved]);
    expect(effects).toEqual([{ type: 'pan', dx: 3, dy: 0 }]);
    expect(state.kind).toBe('panning');
  });

  it('panning goes on from the last point and ends silently', () => {
    const { effects, state } = run([down(20), move(25), move(26), up(26)]);
    expect(effects).toEqual([{ type: 'pan', dx: 50, dy: 0 }, { type: 'pan', dx: 10, dy: 0 }]);
    expect(state).toEqual(IDLE_POINTER);
  });

  it('a press on a hit that takesDrag starts a drag and streams moves in inches', () => {
    const { effects, state } = run([down(1), move(3), move(4)], always);
    expect(effects).toEqual([
      { type: 'dragStart', at: { x: 1, y: 0 }, mods: NO_MODIFIERS },
      { type: 'dragMove', at: { x: 3, y: 0 }, mods: NO_MODIFIERS },
      { type: 'dragMove', at: { x: 4, y: 0 }, mods: NO_MODIFIERS },
    ]);
    expect(state.kind).toBe('dragging');
  });

  it('takesDrag false pans instead', () => {
    expect(run([down(1), move(5)], never).effects).toEqual([{ type: 'pan', dx: 40, dy: 0 }]);
  });

  it('up ends the drag with the last point', () => {
    const { effects, state } = run([down(1), move(3), up(4)], always);
    expect(effects.at(-1)).toEqual({ type: 'dragEnd', at: { x: 4, y: 0 }, mods: NO_MODIFIERS });
    expect(state).toEqual(IDLE_POINTER);
  });

  it('cancel mid-drag emits dragCancel', () => {
    const { effects, state } = run([down(1), move(3), { type: 'cancel' }], always);
    expect(effects.at(-1)).toEqual({ type: 'dragCancel' });
    expect(state).toEqual(IDLE_POINTER);
    expect(run([down(1), { type: 'cancel' }]).effects).toEqual([]);
  });

  it('a right press emits context and no drag', () => {
    const { effects, state } = run([down(1, 7, 2), move(5), up(5)], always);
    expect(effects[0]).toEqual({ type: 'context', hit, at: { x: 1, y: 7 }, screen: { x: 10, y: 7 } });
    expect(effects.slice(1).every((e) => e.type === 'hover')).toBe(true);
    expect(state).toEqual(IDLE_POINTER);
  });

  it('moving with no press hovers', () => {
    expect(run([move(1), move(20)]).effects).toEqual([
      { type: 'hover', hit, at: { x: 1, y: 0 } }, { type: 'hover', hit: null, at: { x: 20, y: 0 } },
    ]);
  });

  it('a Shift press moved past the threshold boxes from the press, on a model or empty table, when the driver takes boxes', () => {
    const shift = { ...NO_MODIFIERS, shift: true };
    const sdown = (x: number): PointerInput => ({ type: 'down', screen: { x: x * 10, y: 0 }, at: { x, y: 0 }, button: 0, mods: shift });
    for (const from of [1, 20]) {
      const { effects, state } = run([sdown(from), move(from + 2), up(from + 3)], always, always);
      expect(effects).toEqual([
        { type: 'boxMove', from: { x: from, y: 0 }, at: { x: from + 2, y: 0 } },
        { type: 'boxEnd', from: { x: from, y: 0 }, at: { x: from + 3, y: 0 } },
      ]);
      expect(state).toEqual(IDLE_POINTER);
    }
    expect(run([sdown(1), move(3), { type: 'cancel' }], always, always).effects.at(-1)).toEqual({ type: 'boxCancel' });
    expect(run([sdown(1), move(3)], always).effects[0]!.type).toBe('dragStart');      // no box taker: Shift drags as before
    expect(run([down(1), move(3)], always, always).effects[0]!.type).toBe('dragStart');  // no Shift: no box
  });
});
