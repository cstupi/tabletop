// viewer/src/lib/play/combatLogState.ts
// client/src/Core/CombatLogCache.cs: the combat log of one log state, built at most once and shared by its readers: the log panel
// (entries), the dice tray (latest) and the undo button (undo). Undo plans as if a pending undo's marker had landed, so each press
// steps back one more; with no undo pending it reuses entries, otherwise it builds over the log with that marker.

import type { Projection } from '../live/apply';
import type { GameEvent, GameInfo } from '../protocol/wire.generated';
import { build, latest } from './combatLog';
import { Names } from './names';
import type { LogEntry, RollEntry } from './rollEntry';

export class CombatLogState {
  readonly names: Names;
  readonly #log: readonly GameEvent[];
  readonly #pendingUndoTo: number | null;
  #entries: readonly LogEntry[] | null = null;
  #latest: RollEntry | null = null;
  #latestDone = false;

  constructor(log: readonly GameEvent[], pendingUndoTo: number | null, names: Names) {
    this.#log = log; this.#pendingUndoTo = pendingUndoTo; this.names = names;
  }

  get entries(): readonly LogEntry[] { return this.#entries ??= build(this.#log, this.names); }

  get latest(): RollEntry | null {
    if (!this.#latestDone) { this.#latest = latest(this.entries); this.#latestDone = true; }
    return this.#latest;
  }

  get undo(): null { return null; }   // Task 4 wires undoPlanOf
}

/** Hands every reader the same CombatLogState until the log state changes: another log, another length or last seq, another pending
 * undo, another game record or another projection instance (a prediction, a rebuild after an Undone). */
export class CombatLogCache {
  #key: { log: readonly GameEvent[]; count: number; lastSeq: number; pending: number | null; game: GameInfo; projection: Projection | null } | null = null;
  #state: CombatLogState | null = null;

  of(log: readonly GameEvent[], pendingUndoTo: number | null, game: GameInfo, projection: Projection | null): CombatLogState {
    const lastSeq = log.length === 0 ? 0 : log[log.length - 1]!.seq;
    const k = this.#key;
    if (this.#state !== null && k !== null && k.log === log && k.count === log.length && k.lastSeq === lastSeq
      && k.pending === pendingUndoTo && k.game === game && k.projection === projection) return this.#state;
    this.#key = { log, count: log.length, lastSeq, pending: pendingUndoTo, game, projection };
    return this.#state = new CombatLogState(log, pendingUndoTo, new Names(game, projection));
  }
}
