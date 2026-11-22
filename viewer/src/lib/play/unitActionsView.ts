// viewer/src/lib/play/unitActionsView.ts
// client/src/Core/UnitActionsView.cs: the selected unit's action buttons over it (spec 2026-11-21-actions-over-unit-design.md): the Ask they
// answer, the unit, the point above its tallest model at its models' centroid, and the decision box's tiles for its options, in the box's order.

import type { Projection } from '../live/apply';
import type { GameInfo, Point3 } from '../protocol/wire.generated';
import { mine as myAsk } from './askView';
import { ACTIVATIONS, groupsOf, keyCap, type OptionTile } from './decisionCard';
import { choicesOf, number as numberChoices, type Numbered } from './decisionView';
import { Names } from './names';

export interface UnitActions { readonly askId: string; readonly unitId: string; readonly anchor: Point3; readonly options: readonly OptionTile[] }

/** A button's tooltip: the option's word and its key cap ("Advance  2"); the word alone with no key. */
export const tipOf = (t: OptionTile): string => (t.key > 0 ? `${t.word}  ${keyCap(t.key)}` : t.word);

const NO_WOUNDS: ReadonlyMap<string, number> = new Map();

/**
 * The buttons while my open Ask asks for an activation (activate, act_or_end_phase) and offers the selected unit an option; null while
 * watching or replaying, for another team's Ask or any other Ask, with nothing selected, for a unit the Ask offers nothing or one with no model
 * on the table. The keys are the box's numbering when given (numbered), else the box narrowed to the unit's options.
 */
export function unitActionsOf(p: Projection, game: GameInfo, myTeam: string | null, selected: string | null, watching: boolean,
  numbered?: readonly Numbered[]): UnitActions | null {
  if (watching || selected === null || !p.units.has(selected)) return null;
  const ask = myAsk(p, game, myTeam)?.ask;
  if (ask === undefined || ask.legal.kind !== 'options' || !ACTIVATIONS.has(ask.question)) return null;
  const hits = choicesOf(ask.legal, p, selected);
  if (hits.length === 0) return null;
  const at: Point3[] = [];
  let top = -Infinity;
  for (const m of p.models.values()) {
    if (m.unitId !== selected || m.location.kind !== 'table') continue;
    const q = m.location.placement.position;
    at.push(q);
    top = Math.max(top, q.z + m.slot.height);
  }
  if (at.length === 0) return null;
  const mean = (f: (q: Point3) => number): number => at.reduce((s, q) => s + f(q), 0) / at.length;
  const tiles = groupsOf(ask, hits, numbered ?? numberChoices(hits), p, game, new Names(game, p), NO_WOUNDS).flatMap((g) => g.tiles);
  return { askId: ask.id, unitId: selected, anchor: { x: mean((q) => q.x), y: mean((q) => q.y), z: top }, options: tiles };
}

export interface Rect { readonly x: number; readonly y: number; readonly w: number; readonly h: number }

const overlaps = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

/**
 * UnitActionsPlace.Rect: where the buttons' row stands on screen: centred over the point with its bottom on it, inside the view less the
 * margin, and off every covered rectangle (grown by the margin): the nearest clear place beside, above or below one it would sit under.
 */
export function placeRect(centreX: number, bottom: number, w: number, h: number, view: Rect, covered: readonly Rect[], margin: number): Rect {
  const clamp = (v: number, lo: number, hi: number): number => Math.min(Math.max(v, lo), Math.max(lo, hi));
  const inside = (x: number, y: number): Rect => ({
    x: clamp(x, view.x + margin, view.x + view.w - margin - w), y: clamp(y, view.y + margin, view.y + view.h - margin - h), w, h,
  });
  const grown = covered.map((c) => ({ x: c.x - margin, y: c.y - margin, w: c.w + 2 * margin, h: c.h + 2 * margin }));
  const clear = (r: Rect): boolean => !grown.some((g) => overlaps(g, r));
  const want = inside(centreX - w / 2, bottom - h);
  if (clear(want)) return want;
  let best: Rect | null = null, cost = Infinity;
  for (const g of grown.filter((x) => overlaps(x, want)))
    for (const c of [inside(g.x - w, want.y), inside(g.x + g.w, want.y), inside(want.x, g.y - h), inside(want.x, g.y + g.h)]) {
      const d = Math.abs(c.x - want.x) + Math.abs(c.y - want.y);
      if (clear(c) && d < cost) { best = c; cost = d; }
    }
  return best ?? want;
}
