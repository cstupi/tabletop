// viewer/src/lib/play/undo.ts
// client/src/Core/UndoPlan.cs and Session.UndoToAsync's prediction.
// The Undo button and Ctrl+Z (client UX spec → Undo): undo the last action in the game, whoever took it. An action is a live event with
// an actor (an engine event has none) that is neither an undone marker nor an ask_closed: the close carries the actor of the answer that
// caused it, and is part of that answer. The engine events after it go with it. The target is the live seq immediately before the
// action, 0 when none: the server accepts only a live seq. lostRolls: each live roll, as the combat log's line, once, when it or a live
// reroll or hand edit of it is after the target, ordered by the earliest such seq.

import type { ConnectionState, SendOutcome } from '../connection/gameConnection';
import { effectiveLog, projectTo, type Projection } from '../live/apply';
import type { Command, GameEvent } from '../protocol/wire.generated';
import { build, flatten, pip } from './combatLog';
import { describe } from './logText';
import type { Names } from './names';
import { collapsed, type LogEntry } from './rollEntry';

export interface UndoPlan { readonly toSeq: number; readonly action: GameEvent; readonly tooltip: string; readonly lostRolls: readonly string[] }

export const NOTHING_TO_UNDO = 'Nothing to undo';

/** With the combat log already built over this log (CombatLogState), so the plan does not build it again. */
export function undoPlanOf(log: readonly GameEvent[], n: Names, entries?: readonly LogEntry[]): UndoPlan | null {
  const live = effectiveLog(log);
  let at = -1;
  for (let i = live.length - 1; i >= 0 && at < 0; i--) {
    const e = live[i]!;
    if (e.actor != null && e.payload.kind !== 'undone' && e.payload.kind !== 'ask_closed') at = i;
  }
  if (at < 0) return null;
  const to = at === 0 ? 0 : live[at - 1]!.seq;
  return { toSeq: to, action: live[at]!, tooltip: 'Undo: ' + describe(live[at]!, n), lostRolls: lost(entries ?? build(log, n), to) };
}

function lost(entries: readonly LogEntry[], to: number): string[] {
  const out: { seq: number; text: string }[] = [];
  for (const e of flatten(entries)) {
    if (e.undone) continue;
    if (e.type === 'roll') {
      const seqs = [e.seq, ...e.amendments.filter((a) => !a.undone).map((a) => a.seq)].filter((s) => s > to);
      if (seqs.length > 0) out.push({ seq: Math.min(...seqs), text: collapsed(e, pip) });   // the line already ends with its live amendments
    } else if (e.type === 'line' && (e.event.payload.kind === 'dice_rerolled' || e.event.payload.kind === 'dice_edited') && e.seq > to)
      out.push({ seq: e.seq, text: e.text });
  }
  return out.map((x, i) => ({ ...x, i })).sort((a, b) => a.seq - b.seq || a.i - b.i).map((x) => x.text);
}

/** Undo is local first: the prediction shows until the server's undone or refused arrives. */
export class UndoPrediction {
  #predicted: Projection | null = null;
  #pending: number | null = null;
  #command: string | null = null;
  readonly #listeners = new Set<() => void>();

  /** The projection to show: projectTo(seq) while an undo is pending, else the connection's. */
  projection(state: ConnectionState): Projection | null { return this.#predicted ?? state.projection; }

  get pendingUndoTo(): number | null { return this.#pending; }

  /** Session.UndoToAsync: predict, send Undo(seq); a refusal or a failed send drops the prediction and rethrows the failure. A
   * watcher, whose state holds no board, predicts nothing and sends nothing (null). */
  async undoTo(state: ConnectionState, seq: number, send: (c: Command) => Promise<SendOutcome>): Promise<SendOutcome | null> {
    if (state.board === null || state.game === null || state.projection === null) return null;
    this.#predicted = projectTo(state.board, state.game, state.log, seq);
    this.#pending = seq;
    this.#command = null;
    this.#changed();
    let outcome: SendOutcome;
    try { outcome = await send({ kind: 'undo', toSeq: seq }); } catch (e) {
      this.#clear();
      this.#changed();
      throw e;
    }
    if (outcome.refused !== null) this.#clear(); else this.#command = outcome.id;
    this.#changed();
    return outcome;
  }

  /** Each events batch: an undone whose toSeq is the pending one ends the prediction (ClearPrediction). */
  events(batch: readonly GameEvent[]): void {
    const to = this.#pending;
    if (to !== null && batch.some((e) => e.payload.kind === 'undone' && e.payload.toSeq === to)) { this.#clear(); this.#changed(); }
  }

  /** A Refused for the pending undo's message id, even one that arrives after the send settled. */
  refused(messageId: string): void {
    if (this.#command !== null && messageId === this.#command) { this.#clear(); this.#changed(); }
  }

  /** The connection started over: nothing predicted survives it. */
  reset(): void { if (this.#pending !== null) { this.#clear(); this.#changed(); } }

  subscribe(listener: () => void): () => void {
    this.#listeners.add(listener);
    return () => { this.#listeners.delete(listener); };
  }

  #clear(): void { this.#predicted = null; this.#pending = null; this.#command = null; }
  #changed(): void { for (const l of [...this.#listeners]) l(); }
}
