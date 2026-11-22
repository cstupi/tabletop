// viewer/src/lib/play/decisionCard.ts
// client/src/Core/DecisionCard.cs: the decision box as a card (UX redesign part 2 → Decision box): the title band, the option tiles grouped
// by unit, the move's allowance bar, the key caps, and the quiet band for an Ask that is not mine. Words are the server's: a tile's word is
// its label after the colon.

import type { Projection } from '../live/apply';
import type { Ask, AskOption, GameInfo } from '../protocol/wire.generated';
import { unitNamed, type Numbered } from './decisionView';
import { actionFrame } from './headerView';
import { isDisembark, type RulerReadout } from './moveRuler';
import type { Names } from './names';
import { words } from './rosterView';
import { pipsOf } from './unitCard';

/** The card's title band: the Ask's icon key, its title, the unit it is about, and the prompt as a body line when the title is not it. */
export interface CardHead { readonly icon: string; readonly title: string; readonly subject: string | null; readonly prompt: string | null }
/** One option as a tile: the word after the label's colon (the whole label with no unit), its icon key (null: none), its number key (0: none). */
export interface OptionTile { readonly optionId: string; readonly key: number; readonly word: string; readonly icon: string | null; readonly pieces: readonly string[]; readonly label: string }
/** A run of tiles: one unit's options under its name, pips and team band (unit null: options naming no one unit), and the run's note once. */
export interface OptionGroup {
  readonly unitId: string | null; readonly unit: string | null; readonly teamOrdinal: number; readonly pips: string | null;
  readonly tiles: readonly OptionTile[]; readonly note: string | null;
}
/** A move's allowance as a bar: the farthest held model's distance over what the move allows, and the least it must go. */
export interface AllowanceBar {
  readonly used: number; readonly allowance: number; readonly minimum: number | null;
  readonly over: boolean; readonly fraction: number; readonly minimumFraction: number | null; readonly text: string;
}
/** An open Ask waiting on a team, as a quiet band: whose it is and what it does. mine: a team this client holds. */
export interface QuietBand { readonly teamId: string; readonly ordinal: number; readonly team: string; readonly doing: string; readonly mine: boolean }
/** One team's answer to a sealed Ask, as its band and a chip. */
export interface RevealChip { readonly ordinal: number; readonly team: string; readonly choice: string }

/** The questions whose options are one unit's actions. */
export const ACTIVATIONS: ReadonlySet<string> = new Set(['activate', 'act_or_end_phase']);
const DEPLOYS: ReadonlySet<string> = new Set(['deploy', 'deploy_scouts', 'deploy_ambusher', 'deploy_reinforcement']);
const SHORT_TITLE = 36;

export const inches = (d: number): string => `${Number((Math.round(d * 10) / 10).toFixed(1))}"`;
export const keyCap = (key: number): string => (key > 0 ? String(key) : '');

/** The icon of an action word the rulebook uses; null for any other. */
export function actionIcon(word: string | null | undefined): string | null {
  const w = word?.trim().toLowerCase();
  return w === 'hold' || w === 'advance' || w === 'rush' || w === 'charge' || w === 'shoot' ? w : null;
}

/** A label's word after the colon the scheduler prints ("Red unit 1: Hold" is "Hold"); the label itself without one. */
export function word(label: string): string {
  const at = label.indexOf(': ');
  return at < 0 ? label : label.slice(at + 2);
}

export function headOf(ask: Ask, p: Projection, n: Names): CardHead {
  const acting = actionFrame(p)?.context;
  const actor = (): string | null => (acting?.actorUnitId !== undefined && acting.actorUnitId !== null ? n.unit(acting.actorUnitId) : null);
  const head = (icon: string, title: string, subject: string | null = null, prompt: string | null = null): CardHead => ({ icon, title, subject, prompt });
  const l = ask.legal;
  if (l.kind === 'move_region') {
    const lead = l.modelIds.length > 0 ? p.models.get(l.modelIds[0]!) : undefined;
    const unit = lead !== undefined ? n.unit(lead.unitId) : actor();
    if (isDisembark(l)) return head('advance', 'Disembark', unit);
    const action = acting?.action ?? null;
    return head(actionIcon(action) ?? 'advance', action ?? 'Move', unit);
  }
  if (l.kind === 'assignment') return head('shoot', 'Shoot', actor());
  const q = ask.question;
  const icon = ACTIVATIONS.has(q) ? 'activation' : DEPLOYS.has(q) ? 'army'
    : q === 'remove_casualties' ? 'wound' : q === 'charge_target' ? 'charge'
    : q === 'takedown_target' || q === 'strike_back' || q === 'counter' ? 'melee' : q === 'shoot_or_not' ? 'shoot'
    : q === 'cast' || q === 'spell_targets' || q === 'spend_tokens' ? 'cast'
    : q === 'end_round' || q === 'confirm' || q === 'game_over' ? 'ready' : 'activation';
  if (q === 'game_over') return head(icon, 'End or continue?', null, ask.prompt);
  return ask.prompt.length <= SHORT_TITLE && !ask.prompt.includes('\n') ? head(icon, ask.prompt) : head(icon, words(q.replaceAll('_', ' ')), null, ask.prompt);
}

/**
 * The options as tiles. An activation's options are grouped by unit, each run of options naming the same pieces with the same note one
 * group, its note once; any other Ask's options are one group. The keys are the numbered list's, so they continue across groups.
 */
export function groupsOf(ask: Ask, shown: readonly AskOption[], numbered: readonly Numbered[], p: Projection, game: GameInfo, n: Names,
  maxWounds: ReadonlyMap<string, number>): OptionGroup[] {
  const activation = ACTIVATIONS.has(ask.question);
  const keyOf = (c: AskOption) => numbered.find((x) => x.optionId === c.id)?.key ?? 0;
  const tile = (c: AskOption, byUnit: boolean): OptionTile => {
    const w = byUnit ? word(c.label) : c.label;
    const icon = actionIcon(w) ?? (activation && byUnit ? 'activation' : ask.question === 'cast' ? 'cast' : null);
    return { optionId: c.id, key: keyOf(c), word: w, icon, pieces: c.pieces, label: c.label };
  };
  if (!activation) return shown.length === 0 ? [] : [{ unitId: null, unit: null, teamOrdinal: 0, pips: null, tiles: shown.map((c) => tile(c, false)), note: null }];
  const groups: OptionGroup[] = [];
  const same = (a: AskOption, b: AskOption) => (a.note ?? null) === (b.note ?? null) && a.pieces.length === b.pieces.length && a.pieces.every((x, i) => x === b.pieces[i]);
  for (let i = 0; i < shown.length;) {
    let j = i + 1;
    while (j < shown.length && same(shown[j]!, shown[i]!)) j++;
    const run = shown.slice(i, j);
    const unit = unitNamed(run[0]!.pieces, p);
    const team = unit === null ? null : n.teamOfUnit(unit);
    const ordinal = team === null ? 0 : game.teams.find((t) => t.id === team)?.ordinal ?? 0;
    groups.push({ unitId: unit, unit: unit === null ? null : n.unit(unit), teamOrdinal: ordinal, pips: unit === null ? null : unitPips(p, unit, maxWounds),
      tiles: run.map((c) => tile(c, unit !== null)), note: run[0]!.note ?? null });
    i = j;
  }
  return groups;
}

/** The unit's wound pips as the unit card draws them (pipsOf): wounds left over its models not removed, past twelve `left/max`. */
export function unitPips(p: Projection, unitId: string, maxWounds: ReadonlyMap<string, number>): string {
  const models = [...p.models.values()].filter((m) => m.unitId === unitId).map((m) => ({ m, max: maxWounds.get(m.id) ?? 1 }));
  const left = models.filter((x) => !(x.m.location.kind === 'shelf' && x.m.wounds >= x.max)).reduce((s, x) => s + Math.max(0, x.max - x.m.wounds), 0);
  return pipsOf(left, models.reduce((s, x) => s + x.max, 0));
}

/** The bar over the held models' readouts (moveRuler.read): the farthest one's distance. */
export function barOf(readouts: readonly RulerReadout[], allowance: number, minimum: number | null): AllowanceBar {
  const used = readouts.reduce((m, r) => Math.max(m, r.distance), 0);
  const clamp = (x: number) => Math.min(1, Math.max(0, x));
  return {
    used, allowance, minimum, over: used > allowance + 1e-6,
    fraction: allowance <= 0 ? (used > 0 ? 1 : 0) : clamp(used / allowance),
    minimumFraction: minimum !== null && allowance > 0 ? clamp(minimum / allowance) : null,
    text: `${inches(used)} of ${inches(allowance)}`,
  };
}

/** One band per team an open Ask waits on, in Ask order, each team and question once; my teams' first. */
export function quietOf(p: Projection, game: GameInfo, n: Names, mine: readonly string[]): QuietBand[] {
  const seen = new Set<string>();
  const out: QuietBand[] = [];
  for (const a of [...p.openAsks.values()].sort((x, y) => (x.ask.id < y.ask.id ? -1 : x.ask.id > y.ask.id ? 1 : 0)))
    for (const t of a.ask.teams) {
      if (a.answers.has(t) || seen.has(`${t}|${a.ask.question}`)) continue;
      seen.add(`${t}|${a.ask.question}`);
      out.push({ teamId: t, ordinal: game.teams.find((x) => x.id === t)?.ordinal ?? 0, team: n.team(t), doing: doing(a.ask.question), mine: mine.includes(t) });
    }
  return [...out.filter((b) => b.mine), ...out.filter((b) => !b.mine)];
}

/** What a team is doing while its Ask is open, as the waiting band says it. */
export function doing(question: string): string {
  if (ACTIVATIONS.has(question)) return 'activating';
  if (DEPLOYS.has(question)) return 'deploying';
  switch (question) {
    case 'move': return 'moving';
    case 'declare_targets': return 'declaring targets';
    case 'remove_casualties': return 'removing casualties';
    case 'spend_tokens': return 'spending tokens';
    case 'end_round': return 'ending the round';
    case 'cast': return 'casting';
    default: return question.replaceAll('_', ' ');
  }
}
