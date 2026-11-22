// viewer/src/lib/play/unitActionsView.ts
// client/src/Core/UnitActionsView.cs: the bar over the acting unit (spec 2026-11-21-actions-over-unit-design.md): the Ask it answers, the unit,
// the point above its tallest model at its models' centroid, the decision box's tiles for the Ask's options in the box's order, the card's foot
// actions with their numbers (cardActions), and while I pick shooting targets the enemy units I may target, each with its point.

import type { Projection } from '../live/apply';
import type { AskOption, GameInfo, Point3 } from '../protocol/wire.generated';
import { mine as myAsk } from './askView';
import { actionsOf, numberActions, type CardAction } from './cardActions';
import { ACTIVATIONS, groupsOf, keyCap, type OptionTile } from './decisionCard';
import { choicesOf, decisionOf, number as numberChoices, type Numbered } from './decisionView';
import { actionFrame } from './headerView';
import { Names } from './names';
import { teamOf } from './sheetFocus';
import { TargetAssignment } from './targetAssignment';

/** A unit I may shoot, and the point above its tallest model where its target icon stands. */
export interface UnitTarget { readonly unitId: string; readonly anchor: Point3 }

export interface UnitActions {
  readonly askId: string; readonly unitId: string; readonly anchor: Point3; readonly options: readonly OptionTile[];
  readonly actions: readonly CardAction[]; readonly targets: readonly UnitTarget[];
}

/** A button's tooltip: the option's word and its key cap ("Advance  2"); the word alone with no key. */
export const tipOf = (t: OptionTile): string => (t.key > 0 ? `${t.word}  ${keyCap(t.key)}` : t.word);

const NO_WOUNDS: ReadonlyMap<string, number> = new Map();

/**
 * The bar for my open Ask, over its acting unit: for an activation (activate, act_or_end_phase) the selected unit, with its options; for any
 * other Ask the unit of mine acting now (the action frame's), else the selected unit of mine the Ask names, with all the Ask's options. Then
 * the card's foot actions, numbered after the tiles. Null while watching or replaying, for another team's Ask, with no such unit or one with
 * no model on the table. The keys are the box's numbering when given (numbered), else the box's own (an activation's narrowed to the unit's).
 */
export function unitActionsOf(p: Projection, game: GameInfo, myTeam: string | null, selected: string | null, watching: boolean,
  numbered?: readonly Numbered[]): UnitActions | null {
  if (watching) return null;
  const view = myAsk(p, game, myTeam);
  if (view === null) return null;
  const ask = view.ask;
  const mineUnit = (u: string): boolean => p.units.has(u) && teamOf(p, game, u)?.id === myTeam;
  let unit: string;
  let shown: readonly AskOption[];
  let keys: readonly Numbered[];
  if (ACTIVATIONS.has(ask.question)) {
    if (selected === null || !p.units.has(selected) || ask.legal.kind !== 'options') return null;
    shown = choicesOf(ask.legal, p, selected);
    if (shown.length === 0) return null;
    unit = selected;
    keys = numbered ?? numberChoices(shown);
  } else {
    const named = (u: string): boolean => view.highlights.some((id) => id === u || p.models.get(id)?.unitId === u);
    const actor = actionFrame(p)?.context.actorUnitId ?? null;
    if (actor !== null && mineUnit(actor)) unit = actor;
    else if (selected !== null && mineUnit(selected) && named(selected)) unit = selected;
    else return null;
    shown = ask.legal.kind === 'options' ? ask.legal.choices : [];
    keys = numbered ?? decisionOf(p, game, new Names(game, p), myTeam).numbered;
  }
  const anchor = anchorOf(p, unit);
  if (anchor === null) return null;
  const tiles = groupsOf(ask, shown, keys, p, game, new Names(game, p), NO_WOUNDS).flatMap((g) => g.tiles);
  const actions = numberActions(actionsOf(ask, view.mode, p), keys.length);
  const targets = ask.legal.kind === 'assignment' ? targetsOf(p, new TargetAssignment(ask.legal).candidates) : [];
  return { askId: ask.id, unitId: unit, anchor, options: tiles, actions, targets };
}

/** The point above a unit's tallest model at its models' centroid; null with none on the table. */
export function anchorOf(p: Projection, unit: string): Point3 | null {
  const at: Point3[] = [];
  let top = -Infinity;
  for (const m of p.models.values()) {
    if (m.unitId !== unit || m.location.kind !== 'table') continue;
    const q = m.location.placement.position;
    at.push(q);
    top = Math.max(top, q.z + m.slot.height);
  }
  if (at.length === 0) return null;
  const mean = (f: (q: Point3) => number): number => at.reduce((s, q) => s + f(q), 0) / at.length;
  return { x: mean((q) => q.x), y: mean((q) => q.y), z: top };
}

/** The target icons: each unit I may shoot now (TargetAssignment.candidates) with a model on the table, at its point. */
export function targetsOf(p: Projection, candidates: readonly string[]): UnitTarget[] {
  const out: UnitTarget[] = [];
  for (const u of candidates) { const at = anchorOf(p, u); if (at !== null) out.push({ unitId: u, anchor: at }); }
  return out;
}

export interface Rect { readonly x: number; readonly y: number; readonly w: number; readonly h: number }

const overlaps = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

/** UnitLabel: the unit's name label on screen, its bottom edge and height; the row keeps above it whether it shows or not. */
export interface UnitLabel { readonly bottom: number; readonly height: number }

/**
 * UnitActionsPlace.Rect: where the buttons' row stands on screen: centred over the point, its bottom the margin above the models' top or the
 * name label's top, whichever is higher; inside the view less the margin, and off every covered rectangle (grown by the margin): the nearest
 * clear place beside, above or below one it would sit under.
 */
export function placeRect(centreX: number, top: number, w: number, h: number, view: Rect, covered: readonly Rect[], margin: number,
  label: UnitLabel | null = null): Rect {
  const bottom = Math.min(top, label === null ? top : label.bottom - label.height) - margin;
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

/**
 * UnitActionsHold: where the bar stands from frame to frame. It holds still while held (the pointer over it, a drag or a press in progress), so
 * a click aimed at it after a drag lands where it was aimed, and otherwise eases to where placeRect wants it, never jumping. The first place after
 * it was hidden, or for another unit, is taken at once.
 */
export class UnitActionsHold {
  /** The ease's rate per second: the gap closes by 1 - e^(-rate * dt) a frame. */
  static readonly RATE = 12;
  private at: Rect | null = null;
  private unit = '';

  step(unit: string, want: Rect, held: boolean, dt: number): Rect {
    const at = this.at;
    if (at === null || unit !== this.unit) { this.unit = unit; return (this.at = want); }
    if (held) return (this.at = { ...at, w: want.w, h: want.h });
    const k = 1 - Math.exp(-UnitActionsHold.RATE * Math.max(0, dt));
    const ease = (from: number, to: number): number => (Math.abs(to - from) < 0.5 ? to : from + (to - from) * k);
    return (this.at = { ...want, x: ease(at.x, want.x), y: ease(at.y, want.y) });
  }

  /** The bar was hidden: its next place is taken at once. */
  reset(): void { this.at = null; }
}
