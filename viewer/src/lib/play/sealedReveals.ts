// viewer/src/lib/play/sealedReveals.ts
// client/src/Core/SealedReveals.cs: the decision box's reveals: each sealed Ask that closes with answers, as one chip per team (its band and
// its choice), read per batch and queued in order, so a render that coalesces several batches still shows every reveal, whatever Ask is open by then.

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

  /** The reveals queued since the last call, oldest first; the queue is empty afterwards. */
  drain(): RevealChip[][] {
    const taken = this.#pending;
    this.#pending = [];
    return taken;
  }
}
