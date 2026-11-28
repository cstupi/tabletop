// viewer/src/lib/play/markLabels.ts
// client/src/Core/MarkLabels.cs: the short noun phrase a mark's label over the board reads (MarkWords; the full sentence is the decision box's),
// and where the labels stand: a model's labels stacked downward MARK_GAP apart, centred over its anchor with the stack's foot on it, each stack
// placed as one block by placeRingLabels with the coherency gap labels (placeTableLabels), so no two overlap and none leaves the view.

import type { XY } from '../geometry';
import { TEST_WORDS } from './movePaths';
import { placeRingLabels, type Rect } from './ringShape';

/** The one word-list: a phrase found in the reason (any case) and the label it reads as, first match wins. */
const PHRASES: readonly (readonly [string, string])[] = [
  [TEST_WORDS, TEST_WORDS],
  ['out of coherency', 'Out of coherency'],
  ['overlaps a base', 'On a base'],
  ["on another model's base", 'On a base'],
  ['through impassable terrain', 'Through impassable terrain'],
  ['in impassable terrain', 'In impassable terrain'],
  ['path crosses a model', 'Through a model'],
  ['through another model', 'Through a model'],
  ['path crosses ', 'Through a unit'],
  ['would move through ', 'Through a unit'],
  ['leave the table', 'Off the table'],
  ['outside its deployment zone', 'Outside its zone'],
];

export function shortWords(reason: string): string {
  const lower = reason.toLowerCase();
  for (const [found, words] of PHRASES) if (lower.includes(found.toLowerCase())) return words;
  const beyond = /beyond ([0-9]+(?:\.[0-9]+)?)"/.exec(reason);
  if (beyond !== null) return `Beyond ${beyond[1]}"`;
  const within = /within ([0-9]+(?:\.[0-9]+)?)"/.exec(reason);
  if (within !== null) return `Too close ${within[1]}"`;
  return 'Against the rules';
}

/** Space.XS: between two labels of one model. */
export const MARK_GAP = 4;

export interface MarkLabel { readonly modelId: string; readonly anchor: XY; readonly w: number; readonly h: number }
/** A coherency gap's words: the middle of its line on the screen, and its size. */
export interface GapLabel { readonly anchor: XY; readonly w: number; readonly h: number }

export function placeMarkLabels(labels: readonly MarkLabel[], view: Rect, covered: readonly Rect[] = []): Rect[] {
  return placeTableLabels(labels, [], view, covered, null).marks;
}

/** Mark labels and coherency gap labels in one placement: each model's stack and each gap label (MARK_GAP over its line's middle) one block for
 *  placeRingLabels, so none meets another, the covered rectangles or the bar over the acting unit. */
export function placeTableLabels(labels: readonly MarkLabel[], gaps: readonly GapLabel[], view: Rect, covered: readonly Rect[], bar: Rect | null): { marks: Rect[]; gaps: Rect[] } {
  const models = [...new Set(labels.map((l) => l.modelId))];
  const blocks = models.map((id): Rect => {
    const mine = labels.filter((l) => l.modelId === id);
    const w = Math.max(...mine.map((l) => l.w)), h = mine.reduce((s, l) => s + l.h, 0) + MARK_GAP * (mine.length - 1);
    const at = mine[0]!.anchor;
    return { x: at.x - w / 2, y: at.y - h, w, h };
  });
  for (const g of gaps) blocks.push({ x: g.anchor.x - g.w / 2, y: g.anchor.y - MARK_GAP - g.h, w: g.w, h: g.h });
  const placed = placeRingLabels(blocks, view, bar === null ? covered : [...covered, bar]);
  const result: Rect[] = new Array(labels.length);
  models.forEach((id, b) => {
    const block = placed[b]!; let y = block.y; const cx = block.x + block.w / 2;
    labels.forEach((l, i) => {
      if (l.modelId !== id) return;
      result[i] = { x: cx - l.w / 2, y, w: l.w, h: l.h };
      y += l.h + MARK_GAP;
    });
  });
  return { marks: result, gaps: placed.slice(models.length) };
}
