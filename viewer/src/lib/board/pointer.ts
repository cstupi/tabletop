// viewer/src/lib/board/pointer.ts
// The press, drag and pan state machine Board.svelte runs, DOM-free so the decisions are tested.

import type { BoardHit, Modifiers } from './interaction';
import type { Point } from './transform';

export type PointerInput =
  | { readonly type: 'down'; readonly screen: Point; readonly at: Point; readonly button: 0 | 2; readonly mods: Modifiers }
  | { readonly type: 'move'; readonly screen: Point; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'up'; readonly screen: Point; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'cancel' };

export type PointerEffect =
  | { readonly type: 'hover'; readonly hit: BoardHit | null; readonly at: Point }
  | { readonly type: 'select'; readonly hit: BoardHit | null; readonly mods: Modifiers; readonly at: Point }
  | { readonly type: 'dragStart' | 'dragMove' | 'dragEnd'; readonly at: Point; readonly mods: Modifiers }
  | { readonly type: 'dragCancel' }
  | { readonly type: 'boxMove' | 'boxEnd'; readonly from: Point; readonly at: Point }
  | { readonly type: 'boxCancel' }
  | { readonly type: 'pan'; readonly dx: number; readonly dy: number }
  | { readonly type: 'context'; readonly hit: BoardHit | null; readonly at: Point; readonly screen: Point };

export type PointerState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'pressed'; readonly screen: Point; readonly at: Point; readonly hit: BoardHit | null; readonly mods: Modifiers }
  | { readonly kind: 'dragging'; readonly last: Point }
  | { readonly kind: 'boxing'; readonly from: Point }
  | { readonly kind: 'panning'; readonly last: Point };

export const DRAG_THRESHOLD_PX = 3;
export const IDLE_POINTER: PointerState = { kind: 'idle' };

export function step(
  state: PointerState, input: PointerInput, pick: (at: Point) => BoardHit | null,
  takesDrag: (hit: BoardHit, at: Point, mods: Modifiers) => boolean,
  takesBox: (at: Point, mods: Modifiers) => boolean = () => false,
): { state: PointerState; effects: PointerEffect[] } {
  const stay = { state, effects: [] as PointerEffect[] };
  if (input.type === 'cancel') {
    return { state: IDLE_POINTER, effects: state.kind === 'dragging' ? [{ type: 'dragCancel' }] : state.kind === 'boxing' ? [{ type: 'boxCancel' }] : [] };
  }
  switch (state.kind) {
    case 'idle':
      if (input.type === 'move') return { state, effects: [{ type: 'hover', hit: pick(input.at), at: input.at }] };
      if (input.type === 'down') {
        const hit = pick(input.at);
        if (input.button === 2) return { state, effects: [{ type: 'context', hit, at: input.at, screen: input.screen }] };
        return { state: { kind: 'pressed', screen: input.screen, at: input.at, hit, mods: input.mods }, effects: [] };
      }
      return stay;
    case 'pressed': {
      if (input.type === 'up') return { state: IDLE_POINTER, effects: [{ type: 'select', hit: state.hit, mods: input.mods, at: input.at }] };
      if (input.type !== 'move') return stay;
      const dx = input.screen.x - state.screen.x;
      const dy = input.screen.y - state.screen.y;
      if (Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return stay;
      // Shift+drag from anywhere draws a box when the board's driver takes it (play's model picks); else Shift drags as before.
      if (state.mods.shift && takesBox(state.at, state.mods)) {
        return { state: { kind: 'boxing', from: state.at }, effects: [{ type: 'boxMove', from: state.at, at: input.at }] };
      }
      if (state.hit !== null && takesDrag(state.hit, state.at, state.mods)) {
        return {
          state: { kind: 'dragging', last: input.at },
          effects: [{ type: 'dragStart', at: state.at, mods: state.mods }, { type: 'dragMove', at: input.at, mods: input.mods }],
        };
      }
      return { state: { kind: 'panning', last: input.screen }, effects: [{ type: 'pan', dx, dy }] };
    }
    case 'dragging':
      if (input.type === 'move') return { state: { kind: 'dragging', last: input.at }, effects: [{ type: 'dragMove', at: input.at, mods: input.mods }] };
      if (input.type === 'up') return { state: IDLE_POINTER, effects: [{ type: 'dragEnd', at: input.at, mods: input.mods }] };
      return stay;
    case 'boxing':
      if (input.type === 'move') return { state, effects: [{ type: 'boxMove', from: state.from, at: input.at }] };
      if (input.type === 'up') return { state: IDLE_POINTER, effects: [{ type: 'boxEnd', from: state.from, at: input.at }] };
      return stay;
    case 'panning':
      if (input.type === 'move') {
        return { state: { kind: 'panning', last: input.screen }, effects: [{ type: 'pan', dx: input.screen.x - state.last.x, dy: input.screen.y - state.last.y }] };
      }
      if (input.type === 'up') return { state: IDLE_POINTER, effects: [] };
      return stay;
  }
}
