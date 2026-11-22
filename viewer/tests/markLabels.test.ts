// Godot's MarkWords and MarkLabels (client/src/Core/MarkLabels.cs, tests/Tabletop.Client.Tests/MarkLabelsTests.cs): the short words a label over
// the board reads, and a model's labels stacked a step apart, none overlapping.
import { describe, expect, it } from 'vitest';
import { MARK_GAP, placeMarkLabels, shortWords } from '../src/lib/play/markLabels';
import type { Rect } from '../src/lib/play/ringShape';

describe('mark words', () => {
  it.each([
    ['overlaps a base', 'On a base'],
    ["Flayer Cavalry 1 would be placed on another model's base: models may never be placed in physically impossible locations", 'On a base'],
    ['path crosses a model', 'Through a model'],
    ['Flayer Cavalry would move through another model: models may never move through other models or units', 'Through a model'],
    ['ends in impassable terrain', 'In impassable terrain'],
    ['Orc 2 would end its move in impassable terrain', 'In impassable terrain'],
    ['Orc 2 would move through impassable terrain', 'Through impassable terrain'],
    ['within 1" of Orc Warriors', 'Too close 1"'],
    ['placed within 9" of Orc Warriors', 'Too close 9"'],
    ['Orc 2 would end within 1" of Specialists: models must stay 1" away', 'Too close 1"'],
    ['Flayer Cavalry would end out of coherency: all models in a unit must stay within 1" of at least one other model and within 9" of all other models.', 'Out of coherency'],
    ['Orc 2 would leave the table: models may never move outside of the battlefield', 'Off the table'],
    ['Orc 2 would be placed outside its deployment zone: units deploy fully within it', 'Outside its zone'],
    ['Scout 1 would be placed beyond 12" of its zone', 'Beyond 12"'],
    ['Dangerous terrain test', 'Dangerous terrain test'],
    ['something the client has no word for', 'Against the rules'],
  ])('%s reads %s', (reason, words) => { expect(shortWords(reason)).toBe(words); });
});

const view: Rect = { x: 0, y: 0, w: 1280, h: 720 };
const overlap = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

describe('mark labels', () => {
  it("a model's labels stack a step apart over it", () => {
    const [a, b] = placeMarkLabels([{ modelId: 'a', anchor: { x: 400, y: 300 }, w: 120, h: 20 }, { modelId: 'a', anchor: { x: 400, y: 300 }, w: 160, h: 20 }], view);
    expect(b!.y - (a!.y + a!.h)).toBeCloseTo(MARK_GAP, 3);
    expect(a!.x + a!.w / 2).toBeCloseTo(400, 3);
    expect(b!.x + b!.w / 2).toBeCloseTo(400, 3);
    expect(b!.y + b!.h).toBeLessThanOrEqual(300);
  });

  it('labels of two models never overlap', () => {
    const placed = placeMarkLabels([
      { modelId: 'a', anchor: { x: 400, y: 300 }, w: 120, h: 20 },
      { modelId: 'b', anchor: { x: 410, y: 305 }, w: 160, h: 20 },
      { modelId: 'b', anchor: { x: 410, y: 305 }, w: 100, h: 20 },
    ], view);
    for (let i = 0; i < placed.length; i++) for (let j = i + 1; j < placed.length; j++) expect(overlap(placed[i]!, placed[j]!)).toBe(false);
  });

  it('a label keeps off a covered rectangle', () => {
    const panel: Rect = { x: 300, y: 200, w: 300, h: 120 };
    const placed = placeMarkLabels([{ modelId: 'a', anchor: { x: 400, y: 300 }, w: 120, h: 20 }, { modelId: 'a', anchor: { x: 400, y: 300 }, w: 120, h: 20 }], view, [panel]);
    for (const r of placed) expect(overlap(r, panel)).toBe(false);
  });
});
