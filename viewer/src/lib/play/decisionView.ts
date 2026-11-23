// viewer/src/lib/play/decisionView.ts
// client/src/Core/DecisionView.cs: what the decision box shows (UX redesign part 2 → Decision box): the first open Ask addressed to my
// team and its numbered entries (keys 1–9, then 0 for the tenth; none past ten); with nothing addressed to me, no Ask, and the box draws
// decisionCard's quiet bands. The keys are caps on the buttons; there is no hint line.

import type { Projection } from '../live/apply';
import type { Ask, AskOption, GameInfo, MoveRegion, Options } from '../protocol/wire.generated';
import { mine as myAsk, type AskMode } from './askView';
import { DEPLOY_QUESTIONS } from './deployView';
import type { Names } from './names';
import { teamOf } from './sheetFocus';

const KEYED = 10;

export interface Numbered { readonly key: number; readonly label: string; readonly optionId: string }   // key 0: past nine
export interface DecisionModel {
  readonly ask: Ask | null; readonly mode: AskMode | null; readonly title: string; readonly numbered: readonly Numbered[];
}
/** One line of a choice list: an option's button, or (option null) the note under a unit's buttons. */
export interface ChoiceRow { readonly option: AskOption | null; readonly note: string | null }

export function decisionOf(p: Projection, game: GameInfo, n: Names, myTeam: string | null): DecisionModel {
  const mine = myAsk(p, game, myTeam);
  if (mine === null) return { ask: null, mode: null, title: '', numbered: [] };
  const ask = mine.ask;
  const numbered = numberedOf(ask, n);
  const title = ask.legal.kind === 'allocation' ? `Remove ${ask.legal.amount}` : ask.prompt;
  return { ask, mode: mine.mode, title, numbered };
}

function numberedOf(ask: Ask, n: Names): Numbered[] {
  const l = ask.legal;
  if (l.kind === 'options') return keys(l.choices.map((c) => ({ label: c.label, id: c.id })));
  if (l.kind === 'pieces' && DEPLOY_QUESTIONS.includes(ask.question)) return keys(l.eligible.map((id) => ({ label: n.unit(id), id })));
  return [];
}

const keys = (entries: readonly { label: string; id: string }[]): Numbered[] =>
  entries.map((e, i) => ({ key: i < KEYED ? i + 1 : 0, label: e.label, optionId: e.id }));

/** Choices numbered from 1 in their order: the box narrowed to a clicked unit's options. */
export function number(choices: readonly AskOption[]): Numbered[] { return keys(choices.map((c) => ({ label: c.label, id: c.id }))); }

/**
 * The pieces a table click answers with: the other team's pieces (a target pick) that exactly one choice names. A piece several choices name
 * cannot answer; a click on it selects the unit and narrows the box (choicesOf). A unit of a team the Ask addresses (my own unit, the acting
 * unit, a Shaken unit's Idle) never answers by a click: the click selects it, and the action is the bar over the unit, the box or a key.
 */
export function clickAnswers(options: Options, ask: Ask, p: Projection, game: GameInfo): Set<string> {
  const counts = new Map<string, number>();
  for (const c of options.choices) for (const id of new Set(c.pieces)) counts.set(id, (counts.get(id) ?? 0) + 1);
  return new Set([...counts].filter(([id, count]) => count === 1 && othersPiece(ask, p, game, id)).map(([id]) => id));
}

/** A unit or model whose team the Ask does not address; false for anything else. */
export function othersPiece(ask: Ask, p: Projection, game: GameInfo, id: string): boolean {
  const unit = p.units.has(id) ? id : p.models.get(id)?.unitId;
  const team = unit === undefined ? null : teamOf(p, game, unit);
  return team !== null && !ask.teams.includes(team.id);
}

/** The choices that name a unit, by its id or one of its models' ids, in their order. */
export function choicesOf(options: Options, p: Projection, unitId: string): AskOption[] {
  return options.choices.filter((c) => c.pieces.some((id) => id === unitId || p.models.get(id)?.unitId === unitId));
}

/** DecisionView.Narrowed: what an activation's card first shows, the selected unit's choices when it has any, else null for every unit's.
 * An Ask drawn again (an undo reopens it, a refusal) keeps to the selection. */
export function narrowed(options: Options, p: Projection, selected: string | null): AskOption[] | null {
  if (selected === null) return null;
  const hits = choicesOf(options, p, selected);
  return hits.length > 0 ? hits : null;
}

/**
 * The one unit an option's pieces name, by its id or its models' ids: the unit whose hover label shows while the pointer is on the option's
 * button. Null when the pieces name no unit, several, or anything that is not a unit or model.
 */
export function unitNamed(pieces: readonly string[], p: Projection): string | null {
  let unit: string | null = null;
  for (const id of pieces) {
    const of = p.units.has(id) ? id : p.models.get(id)?.unitId ?? null;
    if (of === null || (unit !== null && unit !== of)) return null;
    unit = of;
  }
  return unit;
}

/**
 * The choices as the box lists them: each option, and after a run of options naming the same pieces with the same AskOption.note
 * (activate: one unit's actions), that note once. The text is the server's, shown as sent.
 */
export function rows(shown: readonly AskOption[]): ChoiceRow[] {
  const out: ChoiceRow[] = [];
  for (let i = 0; i < shown.length; i++) {
    const cur = shown[i]!;
    out.push({ option: cur, note: null });
    const next = shown[i + 1];
    const last = next === undefined || (next.note ?? null) !== (cur.note ?? null) || !sameIds(next.pieces, cur.pieces);
    if (last && cur.note !== undefined && cur.note !== null) out.push({ option: null, note: cur.note });
  }
  return out;
}

const sameIds = (a: readonly string[], b: readonly string[]): boolean => a.length === b.length && a.every((x, i) => x === b[i]);

/**
 * The stop button of a move that may stop (MoveRegion.stoppable, p.13 Caster "at any point before attacking"): the window after the stop
 * offers a cast when a moving model holds spell tokens, else whatever else it offers. Null when the move cannot stop.
 */
export function stopLabel(p: Projection, m: MoveRegion): string | null {
  if (!m.stoppable) return null;
  return m.modelIds.some((id) => (p.models.get(id)?.spellTokens ?? 0) > 0) ? 'Cast here' : 'Stop here';
}
