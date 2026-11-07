// viewer/src/lib/replay/replayTimeline.ts
// client/src/Core/ReplayTimeline.cs (ReplayTimeline, ReplayUnit), member for member: a replay of a game's log. Position counts the log's
// events applied, 0 (the board as setup left it) to count (the whole log); the projection is the log's first `position` events as they
// happened, undos included. The source is the live log, which a running game's events keep extending: forward applies the next events,
// backward rebuilds from the start.

import { Applier, type Projection } from '../live/apply';
import type { Board, GameEvent, GameInfo } from '../protocol/wire.generated';

/** What a replay steps by: one player input and what the rules did with it, one action, one turn, one round. */
export type ReplayUnit = 'step' | 'action' | 'turn' | 'round';

export class ReplayTimeline {
  readonly #board: Board;
  readonly #game: GameInfo;
  readonly #source: () => readonly GameEvent[];
  #at: Applier;
  readonly #stops = new Map<ReplayUnit, { count: number; stops: readonly number[] }>();

  /** `source` is read on every call, so a log that grows (a new array per batch) extends the timeline. */
  constructor(board: Board, game: GameInfo, source: () => readonly GameEvent[]) {
    this.#board = board;
    this.#game = game;
    this.#source = source;
    this.#at = new Applier(board, game);
  }

  get count(): number { return this.#source().length; }
  /** The whole log the replay runs over. */
  get source(): readonly GameEvent[] { return this.#source(); }
  get position(): number { return this.#at.log.length; }
  get atEnd(): boolean { return this.position >= this.count; }
  get projection(): Projection { return this.#at.projection; }
  /** The events shown: the log's first `position` events. The combat log is built over it. */
  get shown(): readonly GameEvent[] { return this.#at.log; }

  /** Moves to `position`, clamped to the log. Forward: the events applied, in order. Backward: null, the projection rebuilt from the board. */
  seek(position: number): readonly GameEvent[] | null {
    const source = this.#source();
    const to = Math.min(Math.max(0, Math.round(position)), source.length);
    if (to < this.position) {
      this.#at = new Applier(this.#board, this.#game);
      for (let i = 0; i < to; i++) this.#at.apply(source[i]!);
      return null;
    }
    const events: GameEvent[] = [];
    for (let i = this.position; i < to; i++) { this.#at.apply(source[i]!); events.push(source[i]!); }
    return events;
  }

  next(unit: ReplayUnit): number { return this.#stopsOf(unit).find((s) => s > this.position) ?? this.count; }

  previous(unit: ReplayUnit): number {
    const stops = this.#stopsOf(unit);
    for (let i = stops.length - 1; i >= 0; i--) if (stops[i]! < this.position) return stops[i]!;
    return 0;
  }

  /** "Round 2 · 140 of 2210": the round the position is in (none before the first) and the position. */
  get caption(): string {
    const round = this.projection.scheduler.round;
    return `${round > 0 ? `Round ${round} · ` : 'Before round 1 · '}${this.position} of ${this.count}`;
  }

  #stopsOf(unit: ReplayUnit): readonly number[] {
    const source = this.#source();
    const cached = this.#stops.get(unit);
    if (cached !== undefined && cached.count === source.length) return cached.stops;
    const stops = replayStops(source, unit);
    this.#stops.set(unit, { count: source.length, stops });
    return stops;
  }
}

/**
 * The positions a unit stops at, ascending, from 0 to the log's length. Step: before each player input (an event a participant's command
 * wrote, after one the engine or another participant wrote), so a step shows an input and everything the rules did with it. Action: just
 * after each action frame ends (a sequence with an action, none open, finished or abandoned), so the board and the caption show the same
 * action done. Turn and round: just after each turn_started and round_started, so the header already names it.
 */
export function replayStops(log: readonly GameEvent[], unit: ReplayUnit): number[] {
  const stops = [0];
  let action: { frame: string; seq: number } | null = null;
  for (let i = 0; i < log.length; i++) {
    const e = log[i]!;
    const p = e.payload;
    let stop: number | null = null;
    if (unit === 'step') stop = e.actor !== undefined && (i === 0 || log[i - 1]!.actor !== e.actor) ? i : null;
    else if (unit === 'turn') stop = p.kind === 'turn_started' ? i + 1 : null;
    else if (unit === 'round') stop = p.kind === 'round_started' ? i + 1 : null;
    else if (p.kind === 'sequence_started' && p.context.action !== undefined && action === null) action = { frame: p.frameId, seq: e.seq };
    else if ((p.kind === 'sequence_finished' || p.kind === 'sequence_abandoned') && action?.frame === p.frameId) { action = null; stop = i + 1; }
    else if (p.kind === 'undone' && action !== null && action.seq > p.toSeq) action = null;
    if (stop !== null && stop > stops[stops.length - 1]!) stops.push(stop);
  }
  if (stops[stops.length - 1]! < log.length) stops.push(log.length);
  return stops;
}
