// viewer/src/lib/play/decisionView.ts
// client/src/Core/DecisionView.cs: what the decision box shows (in-game UX spec → Decision box): the first open Ask addressed to my team,
// its numbered entries (keys 1–9, then 0 for entries past nine), and the hint line; with nothing addressed to me, no Ask. The waiting line
// is headerView's `waiting`, which the box shows then.

import type { Projection } from '../live/apply';
import type { Ask, AskOption, GameInfo, MoveRegion, Options } from '../protocol/wire.generated';
import { mine as myAsk, confirmLabel, type AskMode } from './askView';
import { DEPLOY_QUESTIONS } from './deployView';
import type { Names } from './names';

const KEYED = 9;

export interface Numbered { readonly key: number; readonly label: string; readonly optionId: string }   // key 0: past nine
export interface DecisionModel {
  readonly ask: Ask | null; readonly mode: AskMode | null; readonly title: string; readonly numbered: readonly Numbered[];
  readonly hint: string; readonly autoPass: boolean;
}
/** One line of a choice list: an option's button, or (option null) the note under a unit's buttons. */
export interface ChoiceRow { readonly option: AskOption | null; readonly note: string | null }

export function decisionOf(p: Projection, game: GameInfo, n: Names, myTeam: string | null, muted: ReadonlySet<string>): DecisionModel {
  const mine = myAsk(p, game, myTeam, muted);
  if (mine === null) return { ask: null, mode: null, title: '', numbered: [], hint: '', autoPass: false };
  const ask = mine.ask;
  const numbered = numberedOf(ask, n);
  const title = ask.legal.kind === 'allocation' ? `Remove ${ask.legal.amount}` : ask.prompt;
  return { ask, mode: mine.mode, title, numbered, hint: hintOf(p, ask, numbered), autoPass: mine.autoPass };
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
 * The pieces a table click answers with: those exactly one choice names. A piece several choices name (activate: one option per unit and
 * action) cannot answer; a click on it selects the unit and narrows the box (choicesOf).
 */
export function clickAnswers(options: Options): Set<string> {
  const counts = new Map<string, number>();
  for (const c of options.choices) for (const id of new Set(c.pieces)) counts.set(id, (counts.get(id) ?? 0) + 1);
  return new Set([...counts].filter(([, count]) => count === 1).map(([id]) => id));
}

/** The choices that name a unit, by its id or one of its models' ids, in their order. */
export function choicesOf(options: Options, p: Projection, unitId: string): AskOption[] {
  return options.choices.filter((c) => c.pieces.some((id) => id === unitId || p.models.get(id)?.unitId === unitId));
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

/** The note on a unit's choices in my open Options Ask (activate: why its actions are limited), for the unit card; null when no Ask is open for my team or none of the unit's choices carries one. */
export function noteOf(p: Projection, game: GameInfo, myTeam: string | null, muted: ReadonlySet<string>, unitId: string): string | null {
  const l = myAsk(p, game, myTeam, muted)?.ask.legal;
  if (l === undefined || l.kind !== 'options') return null;
  return choicesOf(l, p, unitId).map((c) => c.note).find((x) => x !== undefined && x !== null) ?? null;
}

/**
 * The stop button of a move that may stop (MoveRegion.stoppable, p.13 Caster "at any point before attacking"): the window after the stop
 * offers a cast when a moving model holds spell tokens, else whatever else it offers. Null when the move cannot stop.
 */
export function stopLabel(p: Projection, m: MoveRegion): string | null {
  if (!m.stoppable) return null;
  return m.modelIds.some((id) => (p.models.get(id)?.spellTokens ?? 0) > 0) ? 'Cast a spell' : 'Stop here to use a rule';
}

function hintOf(p: Projection, ask: Ask, numbered: readonly Numbered[]): string {
  const keyed = numbered.filter((x) => x.key > 0).length;
  const keyText = keyed === 1 ? '1' : `1–${keyed}`;
  const l = ask.legal;
  switch (l.kind) {
    case 'options':
      if (keyed === 0) return '';
      return `${keyText}: pick${l.choices.some((c) => c.pieces.length > 0) ? ' · click a glowing unit' : ''}`;
    case 'pieces':
      if (DEPLOY_QUESTIONS.includes(ask.question)) return `${keyText}: pick a unit to deploy`;
      return `Click a glowing ${l.eligible.length > 0 && p.models.has(l.eligible[0]!) ? 'model' : 'unit'}${l.max > 1 ? ' · Enter: done' : ''}`;
    case 'move_region': {
      const stop = stopLabel(p, l);
      return `Drag to move · Shift: one model · 1–5 while holding: rows · Q/E: turn · Enter: commit · ${stop !== null ? `C: ${stop[0]!.toLowerCase()}${stop.slice(1)} · ` : ''}Esc: reset`;
    }
    case 'assignment': return 'Click a target · Click a chip: that group alone · Enter: fire · Esc: clear';
    case 'allocation': return 'Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm';
    case 'token_spend': return 'Set the tokens per caster · Enter: spend';
    case 'confirmation': return `Enter: ${confirmLabel(ask)}`;
    default: return '';
  }
}
