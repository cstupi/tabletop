// viewer/src/lib/play/sealedReveals.ts
// client/src/Core/SealedReveals.cs: the decision box's reveals: each sealed Ask that closes with answers, as one chip per team (its band and
// its choice), read per batch and queued in order, so a render that coalesces several batches still shows every reveal. A reveal is a note about
// the Ask that closed: it shows only while no card of mine is up, and the next card clears it.

import type { GameEvent } from '../protocol/wire.generated';
import type { RevealChip } from './decisionCard';
import { answer } from './logText';
import type { Names } from './names';

export class SealedReveals {
  readonly #sealed = new Set<string>();
  #pending: RevealChip[][] = [];

  read(events: readonly GameEvent[], n: Names): void {
    for (const { payload } of events) {
      if (payload.kind === 'ask_opened' && payload.ask.sealed) this.#sealed.add(payload.ask.id);
      if (payload.kind === 'ask_closed' && this.#sealed.delete(payload.askId) && payload.answers.length > 0)
        this.#pending.push(payload.answers.map((a) => ({
          ordinal: n.teams.find((t) => t.id === a.teamId)?.ordinal ?? 0, team: n.team(a.teamId),
          choice: a.answer.kind === 'chosen' ? a.answer.optionId : answer(a.answer, n),
        })));
    }
  }

  #shown: RevealChip[][] | null = null;

  /** What a render draws: with a card of mine up, nothing, and every reveal queued so far is dropped; else the reveals queued since the
   *  last render, oldest first, or the ones already shown when none came. */
  shown(card: boolean): RevealChip[][] | null {
    const fresh = this.#pending;
    this.#pending = [];
    if (card) return this.#shown = null;
    if (fresh.length > 0) this.#shown = fresh;
    return this.#shown;
  }
}
