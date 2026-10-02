// viewer/src/lib/feed/feed.ts
import type { RecordedBoard, RecordedFrame, RecordingMeta } from '../protocol/recording.generated';

export type FeedStatus = 'idle' | 'loading' | 'open' | 'closed' | 'error';

/** What every feed produces: the recording schema and nothing else, so the renderer cannot tell them apart. */
export type FeedState = {
  readonly label: string;
  readonly status: FeedStatus;
  readonly detail: string | null;
  readonly meta: RecordingMeta | null;
  readonly board: RecordedBoard | null;
  readonly frames: readonly RecordedFrame[];
};

export const IDLE: FeedState = { label: '', status: 'idle', detail: null, meta: null, board: null, frames: [] };

/**
 * The seam interactive debugging would use: pausing a run and answering an open Ask from the browser
 * (spec → Interactive debugging is a door, not a room). Nothing implements it. Both feeds return null,
 * and building it "makes the viewer a second thin client, which is a larger surface than a renderer".
 */
export type ControlChannel = {
  answer(askId: string, answer: unknown): void;
  resume(): void;
};

export interface Feed {
  readonly kind: 'recording' | 'live';
  readonly control: ControlChannel | null;
  /**
   * Returns the unsubscribe function, and a caller must hold it: `close()` is not a substitute,
   * because a live feed's socket closes asynchronously and its listener can fire after another feed
   * has replaced it. Swapping and tearing down go through `attachFeed` and `detachFeed` below, which
   * own that order; Task 11's test drives them.
   */
  subscribe(listener: (state: FeedState) => void): () => void;
  close(): void;
}

/** One subscriber list, one current state. Both feeds extend it; nothing else needs a store. */
export class Emitter {
  #state: FeedState;
  readonly #listeners = new Set<(state: FeedState) => void>();

  constructor(initial: FeedState) {
    this.#state = initial;
  }

  get state(): FeedState {
    return this.#state;
  }

  subscribe(listener: (state: FeedState) => void): () => void {
    this.#listeners.add(listener);
    listener(this.#state);
    return () => this.#listeners.delete(listener);
  }

  emit(next: Partial<FeedState>): void {
    this.#state = { ...this.#state, ...next };
    for (const listener of this.#listeners) listener(this.#state);
  }
}

/** Let a feed go: its listener first, then the feed itself. Both halves, always in this order. */
export function detachFeed(previous: Feed | null, unsubscribe: (() => void) | null): void {
  unsubscribe?.();
  previous?.close();
}

/**
 * Swap one feed for another and hand back the new unsubscribe function, which the caller must hold.
 * The order is the whole point and lives here rather than in a component: a live feed's socket closes
 * asynchronously, so a listener left attached across the swap can fire afterwards and overwrite the
 * new feed's state with the old feed's frames.
 */
export function attachFeed(
  previous: Feed | null,
  unsubscribe: (() => void) | null,
  next: Feed,
  listener: (state: FeedState) => void,
): () => void {
  detachFeed(previous, unsubscribe);
  return next.subscribe(listener);
}
