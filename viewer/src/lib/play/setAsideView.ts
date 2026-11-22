// viewer/src/lib/play/setAsideView.ts
// client/src/Core/SetAsideView.cs: which units may be held off the table at deployment, and by which rule (ModelFacts.setAsideRules): the chip on
// the roster row and on the deploy pick's tile, and the deploy pick's Set aside beside a unit. The client names no rule: the name and text are
// the server's, and the server names none once round one starts, so the chip goes. PlacingFocus is the unit card's unit while I place one.

import type { Projection } from '../live/apply';
import type { Command, SetAsideRule } from '../protocol/wire.generated';

/** A unit's set-aside chip: the rule's name as the server sends it, and the tooltip (the rule's text, then how). */
export interface SetAsideChip { readonly name: string; readonly tip: string }

export const SET_ASIDE = 'set-aside';
export const HOW = 'Right-click the unit → Set aside, or Set aside beside it in the deploy pick.';
export const HOW_BACK = 'Set aside. Right-click the unit → Return to deployment.';

const held = (p: Projection, unitId: string): boolean => p.units.get(unitId)?.conditions.has(SET_ASIDE) === true;

export function setAsideChipOf(p: Projection, unitId: string, rules: ReadonlyMap<string, SetAsideRule>): SetAsideChip | null {
  const rule = rules.get(unitId);
  if (rule === undefined) return null;
  const how = held(p, unitId) ? HOW_BACK : HOW;
  const said = firstSentence(rule.text);
  return { name: rule.name, tip: said.length === 0 ? how : `${said}\n${how}` };
}

/** The rule text's first sentence: what choosing it does, short enough for a tooltip line; the unit card's rule chip carries the whole text. */
export function firstSentence(text: string): string {
  const at = text.indexOf('. ');
  return (at < 0 ? text : text.slice(0, at + 1)).trim();
}

/** Set the unit aside (true) or return it to deployment (false): the one command both controls send. */
export const toggle = (unitId: string, aside: boolean): Command =>
  ({ kind: 'set_condition', subject: { kind: 'unit', id: unitId }, condition: SET_ASIDE, present: aside });

/** The deploy pick's Set aside: the roster menu's command for a unit the server names that is not held yet; else null. */
export const setAsideCommand = (p: Projection, unitId: string, maySetAside: ReadonlySet<string>): Command | null =>
  maySetAside.has(unitId) && p.units.has(unitId) && !held(p, unitId) ? toggle(unitId, true) : null;

/** What the deploy pick draws from the facts, as a key (SetAsideView.DrawnKey): the box draws an Ask again when it changes, and only then. */
export const drawnKey = (maySetAside: ReadonlySet<string>, rules: ReadonlyMap<string, SetAsideRule>): string =>
  [...maySetAside].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)).map((u) => `${u}:${rules.get(u)?.name ?? ''}`).join(';');

/** The unit picked holds the card while the selection stands as it was at the pick, or is cleared; a unit selected since takes it. Feed it every render. */
export class PlacingFocus {
  #placing: string | null = null;
  #selectedAtPick: string | null = null;

  placing(placing: string | null, selected: string | null): string | null {
    if (placing !== this.#placing) { this.#placing = placing; this.#selectedAtPick = selected; }
    return placing !== null && (selected === null || selected === this.#selectedAtPick) ? placing : null;
  }
}
