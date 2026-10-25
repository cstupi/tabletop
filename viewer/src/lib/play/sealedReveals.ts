// viewer/src/lib/play/sealedReveals.ts
// client/src/Core/SealedReveals.cs: the decision box's "Revealed: …" lines: each sealed Ask that closes with answers, read per batch and
// queued in order, so a render that coalesces several batches still shows every reveal, whatever Ask is open by then.

import type { GameEvent } from '../protocol/wire.generated';
import { answer } from './logText';
import type { Names } from './names';

export class SealedReveals {
  readonly #sealed = new Set<string>();
  #pending: string[] = [];

  read(events: readonly GameEvent[], n: Names): void {
    for (const { payload } of events) {
      if (payload.kind === 'ask_opened' && payload.ask.sealed) this.#sealed.add(payload.ask.id);
      if (payload.kind === 'ask_closed' && this.#sealed.delete(payload.askId) && payload.answers.length > 0)
        this.#pending.push(`Revealed: ${payload.answers.map((a) => `${n.team(a.teamId)} ${answer(a.answer, n)}`).join(', ')}`);
    }
  }

  /** The reveals queued since the last call, oldest first; the queue is empty afterwards. */
  drain(): string[] {
    const taken = this.#pending;
    this.#pending = [];
    return taken;
  }
}
