// viewer/src/lib/play/combatLog.ts
// client/src/Core/CombatLog.cs: the play log (combat-log spec → The log): an action header per action frame with its lines beneath it
// (a cast inside it a nested block), one RollEntry per RollRecord with its rerolls and edits on its line, a change line per thing a
// step did to the table, and the few lines outside actions (the round, deployment, objectives, menu edits, undos, the end).
// Questions, answers, empty steps and frame bookkeeping are not drawn. Copies the log's words and names no rule.

import { effectiveLog } from '../live/apply';
import type { EventPayload, GameEvent, SequenceContext, SequenceStarted } from '../protocol/wire.generated';
import { Aftermath, Draft, changeLines, deployed } from './combatLogWords';
import { describe, roundChange } from './logText';
import type { Names } from './names';
import { type LineEntry, type LogEntry, type RollEntry, rollEntryOf } from './rollEntry';
import { chain as rollChain } from './rollEntry';
import type { LogFilter } from './settings';

export { pip } from './rollEntry';

/** An action frame, or a casting frame nested in one (nested), and its entries, from its sequence_started to its finish or abandonment,
 * or an undo to before its start. A live block closes at a live finish; an undone block at its own (undone) finish. Header:
 * `R{round} · {Team} · {Unit} · {Action}[ → {verb }{targets}][ · moved {d}"]`; nested: `Cast {spell} → {targets}`. Entries hold
 * RollEntry, LineEntry and nested ActivationBlock, in log order. chainParts: its live rolls and changes, nested ones included. */
export interface ActivationBlock {
  readonly type: 'block'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean;
  readonly header: string; readonly actor: string | null; readonly targets: readonly string[]; readonly entries: readonly LogEntry[];
  readonly chainParts: readonly string[]; readonly nested: boolean; readonly spell: string | null; readonly spellCost: number | null;
}

/** ChainParts joined with ` → `. */
export const blockChain = (b: ActivationBlock): string => b.chainParts.join(' → ');

const NO_CONTEXT: SequenceContext = { targetUnitIds: [], weaponIds: [] };

const STATE_CHANGES: ReadonlySet<string> = new Set([
  'models_moved', 'wounds_set', 'condition_set', 'action_recorded', 'action_record_cleared', 'grant_added', 'grant_removed',
  'spell_tokens_set', 'wounds_this_activation_set', 'score_set', 'marker_placed', 'marker_moved', 'marker_removed', 'objective_control_set',
]);

/** The events the log keeps and does not draw. */
const dropped = (p: EventPayload): boolean =>
  p.kind === 'ask_opened' || p.kind === 'ask_answered' || p.kind === 'ask_closed' || p.kind === 'turn_started' || p.kind === 'turn_ended'
  || (p.kind === 'step_completed' && p.rolls.length === 0 && p.changes.length === 0);

export function build(log: readonly GameEvent[], n: Names): LogEntry[] {
  const live = new Set(effectiveLog(log).map((e) => e.seq));
  const isDead = (e: GameEvent) => !live.has(e.seq) && e.payload.kind !== 'undone';
  const frames = new Map<string, SequenceStarted>();
  for (const e of log) if (e.payload.kind === 'sequence_started') frames.set(e.payload.frameId, e.payload);
  const rolled = new Set<string>();
  for (const e of log) if (e.payload.kind === 'step_completed') for (const r of e.payload.rolls) rolled.add(r.id);
  const amendments = new Map<string, { event: GameEvent; dead: boolean }[]>();
  for (const e of log) {
    const p = e.payload;
    if (p.kind !== 'dice_rerolled' && p.kind !== 'dice_edited') continue;
    const list = amendments.get(p.rollId) ?? [];
    list.push({ event: e, dead: isDead(e) });
    amendments.set(p.rollId, list);
  }
  const after = Aftermath.of(log, frames, isDead, n, amendments);

  const top: LogEntry[] = [];
  const drafts: Draft[] = [];
  const open = new Map<string, GameEvent>();
  let round = 0;
  const started: { seq: number; header: string }[] = [];
  const undos: { seq: number; toSeq: number }[] = [];
  const into = (): LogEntry[] => (drafts.length > 0 ? drafts[drafts.length - 1]!.entries : top);
  /** Every open block started at or after seq, innermost first. */
  const closeFrom = (seq: number): void => {
    while (drafts.length > 0 && drafts[drafts.length - 1]!.start.seq >= seq) {
      const d = drafts[drafts.length - 1]!;
      const block = d.close(isDead(d.start));
      drafts.pop();
      into().push(block);
    }
    for (const [id, ev] of [...open]) if (ev.seq >= seq) open.delete(id);
  };

  for (const e of log) {
    const dead = isDead(e);
    const p = e.payload;
    if (p.kind === 'round_started' && !dead) round = p.round;
    if (p.kind === 'undone') closeFrom(p.toSeq + 1);
    for (const d of drafts) d.see(e, dead);
    const here = into();
    const line = (text: string): LineEntry => ({ type: 'line', seq: e.seq, event: e, undone: dead, text });
    const chain = (part: string): void => { if (!dead) for (const d of drafts) d.chain.push(part); };

    if (p.kind === 'sequence_started') {
      open.set(p.frameId, e);
      const action = p.context.action;
      if (action !== undefined && drafts.length === 0) {
        const d = new Draft(e, p, isDead(e), n, round, action);
        drafts.push(d);
        started.push({ seq: e.seq, header: d.baseHeader });
      } else if (p.sequence === 'casting') drafts.push(new Draft(e, p, isDead(e), n, round, 'Cast', true));
    } else if (p.kind === 'sequence_finished' || p.kind === 'sequence_abandoned') {
      const start = open.get(p.frameId);
      if (start !== undefined && (!dead || isDead(start))) closeFrom(start.seq);
    } else if ((p.kind === 'dice_rerolled' || p.kind === 'dice_edited') && rolled.has(p.rollId)) {
      // drawn on the roll's own line
    } else if (p.kind === 'step_completed') {
      const frameStart = frames.get(p.frameId);
      for (const roll of p.rolls) {
        const base = rollEntryOf(e, dead, p, roll, frameStart?.context ?? NO_CONTEXT, frameStart?.sequence ?? null, (amendments.get(roll.id) ?? []).map((x) => ({ event: x.event, undone: x.dead })), n);
        const entry: RollEntry = { ...base, consequence: after.consequence(roll.id), dieLabels: after.labels.get(roll.id) ?? null };
        here.push(entry);
        chain(rollChain(entry));
      }
      if (!after.absorbed.has(e.seq))
        for (const c of changeLines(p.changes, n, drafts.length === 0, drafts.length > 0 ? drafts[drafts.length - 1]!.actor : null)) { here.push(line(c)); chain(c); }
    } else if (p.kind === 'round_started') here.push(line(`Round ${p.round}`));
    else if (p.kind === 'round_ended') here.push(...p.changes.map((c) => line(roundChange(c, n))));
    else if (p.kind === 'undone') {
      // The first action the undo took back that no earlier undo had, else the action it stepped back into.
      const header = firstAfter(top, drafts, p.toSeq, undos) ?? [...started].reverse().find((s) => s.seq <= p.toSeq)?.header ?? null;
      here.push(line(header === null ? `Undone to #${p.toSeq}` : `Undone to: ${header}`));
      undos.push({ seq: e.seq, toSeq: p.toSeq });
    } else if (p.kind === 'models_moved' && e.actor != null && open.size === 0 && p.moves.every((x) => x.from.kind === 'shelf' && x.to.kind === 'table'))
      here.push(...deployed(p, n).map(line));
    else if (dropped(p)) { /* not drawn */ }
    else if (p.kind === 'action_recorded' && e.actor == null) { /* the engine's record as the action starts: the block's header says it */ }
    else if (STATE_CHANGES.has(p.kind) && e.actor != null) here.push(line(`${describe(e, n)} · by hand`));
    else here.push(line(describe(e, n)));
  }
  closeFrom(Number.MIN_SAFE_INTEGER);
  return top;
}

export function filter(entries: readonly LogEntry[], f: LogFilter, mine: (unitId: string) => boolean, unitId: string | null): LogEntry[] {
  switch (f) {
    case 'rolls': return entries.map(rollsOnly).filter((e): e is LogEntry => e !== null);
    case 'mine': return entries.filter((e) => involves(e, mine));
    case 'unit': return unitId !== null ? entries.filter((e) => involves(e, (u) => u === unitId)) : [];
    default: return entries as LogEntry[];
  }
}

/** The last roll still standing in entries already built, nested blocks included: what the dice tray shows. */
export function latest(entries: readonly LogEntry[]): RollEntry | null {
  const rolls = flatten(entries).filter((e): e is RollEntry => e.type === 'roll' && !e.undone);
  return rolls.length === 0 ? null : rolls[rolls.length - 1]!;
}

/** Every entry in log order, each block's entries after it. */
export function flatten(entries: Iterable<LogEntry>): LogEntry[] {
  const out: LogEntry[] = [];
  for (const e of entries) {
    out.push(e);
    if (e.type === 'block') out.push(...flatten(e.entries));
  }
  return out;
}

/** Rolls, and the blocks holding one at any depth with only those rolls and blocks. */
function rollsOnly(e: LogEntry): LogEntry | null {
  if (e.type === 'roll') return e;
  if (e.type !== 'block') return null;
  const kept = e.entries.map(rollsOnly).filter((x): x is LogEntry => x !== null);
  return kept.length > 0 ? { ...e, entries: kept } : null;
}

function involves(e: LogEntry, unit: (id: string) => boolean): boolean {
  if (e.type === 'block') return (e.actor !== null && unit(e.actor)) || e.targets.some(unit);
  if (e.type === 'roll') return (e.context.actorUnitId !== undefined && unit(e.context.actorUnitId)) || e.context.targetUnitIds.some(unit);
  return false;
}

/** The header of the first block, closed by now, that starts after seq and that no earlier undo already took back: what an undo to
 * seq took back. */
function firstAfter(top: readonly LogEntry[], drafts: readonly Draft[], seq: number, undos: readonly { seq: number; toSeq: number }[]): string | null {
  let best: ActivationBlock | null = null;
  for (const b of flatten([...top, ...drafts.flatMap((d) => d.entries)])) {
    if (b.type !== 'block' || b.seq <= seq || undos.some((u) => u.toSeq < b.seq && b.seq < u.seq)) continue;
    if (best === null || b.seq < best.seq) best = b;
  }
  return best?.header ?? null;
}
