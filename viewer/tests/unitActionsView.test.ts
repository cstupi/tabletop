// UnitActionsViewTests' placement cases, through the port (the view itself agrees with Godot in unitActions.agreement.test.ts).
import { expect, it } from 'vitest';
import { placeRect, tipOf } from '../src/lib/play/unitActionsView';

const view = { x: 0, y: 0, w: 1280, h: 720 };

it('the row sits centred a step above the models inside the view', () => {
  expect(placeRect(640, 348, 120, 40, view, [], 8)).toEqual({ x: 580, y: 300, w: 120, h: 40 });
  expect(placeRect(10, 20, 120, 40, view, [], 8)).toEqual({ x: 8, y: 8, w: 120, h: 40 });
  expect(placeRect(1270, 900, 120, 40, view, [], 8)).toEqual({ x: 1152, y: 672, w: 120, h: 40 });
});

it('the row stands a step above the unit name label', () => {
  expect(placeRect(640, 348, 120, 40, view, [], 8, { bottom: 330, height: 50 })).toEqual({ x: 580, y: 232, w: 120, h: 40 });
  expect(placeRect(640, 348, 120, 40, view, [], 8, { bottom: 400, height: 20 })).toEqual({ x: 580, y: 300, w: 120, h: 40 });
  expect(placeRect(640, 120, 120, 40, view, [], 8, { bottom: 90, height: 60 })).toEqual({ x: 580, y: 8, w: 120, h: 40 });
});

it('the row keeps off covered panels', () => {
  expect(placeRect(900, 508, 120, 40, view, [{ x: 840, y: 400, w: 432, h: 312 }], 8)).toEqual({ x: 840, y: 352, w: 120, h: 40 });
  expect(placeRect(640, 68, 120, 40, view, [{ x: 260, y: 0, w: 760, h: 96 }], 8)).toEqual({ x: 580, y: 104, w: 120, h: 40 });
});

it('the tooltip is the word and its key cap', () => {
  const tile = { optionId: 'a', key: 2, word: 'Advance', icon: 'advance', pieces: [], label: 'Squad: Advance' };
  expect(tipOf(tile)).toBe('Advance  2');
  expect(tipOf({ ...tile, key: 0 })).toBe('Advance');
});
