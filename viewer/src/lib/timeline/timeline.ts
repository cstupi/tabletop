// viewer/src/lib/timeline/timeline.ts

export type Timeline = {
  readonly count: number;
  /** -1 when there is no frame to show. */
  readonly index: number;
  readonly playing: boolean;
  readonly fps: number;
  /** Stick to the last frame as the feed grows. True for the live feed until the user steps or scrubs. */
  readonly follow: boolean;
};

export const EMPTY: Timeline = { count: 0, index: -1, playing: false, fps: 8, follow: false };

export type TimelineAction =
  | { readonly type: 'frames'; readonly count: number }
  | { readonly type: 'play' }
  | { readonly type: 'pause' }
  | { readonly type: 'toggle' }
  | { readonly type: 'tick' }
  | { readonly type: 'step'; readonly by: number }
  | { readonly type: 'scrub'; readonly index: number }
  | { readonly type: 'jump'; readonly seq: number; readonly seqs: readonly number[] }
  | { readonly type: 'first' }
  | { readonly type: 'last' }
  | { readonly type: 'fps'; readonly fps: number };

const clamp = (index: number, count: number): number => (count === 0 ? -1 : Math.min(count - 1, Math.max(0, index)));

export function reduce(state: Timeline, action: TimelineAction): Timeline {
  switch (action.type) {
    case 'frames': {
      const count = Math.max(0, action.count);
      const index = state.follow ? count - 1 : state.index;
      return { ...state, count, index: clamp(index, count) };
    }
    // Playing lets go of the end (a live feed opens following it, where a tick would stop at once); from the last frame it starts again
    // from the first.
    case 'play':
      return state.count === 0 ? state
        : { ...state, playing: true, follow: false, index: state.index >= state.count - 1 ? 0 : state.index };
    case 'pause':
      return { ...state, playing: false };
    case 'toggle':
      return reduce(state, { type: state.playing ? 'pause' : 'play' });
    case 'tick': {
      if (!state.playing) return state;
      const index = clamp(state.index + 1, state.count);
      return { ...state, index, playing: index < state.count - 1 };
    }
    case 'step':
      return { ...state, playing: false, follow: false, index: clamp(state.index + action.by, state.count) };
    case 'scrub':
      return { ...state, playing: false, follow: false, index: clamp(action.index, state.count) };
    case 'jump': {
      const found = indexOfSeq(action.seqs, action.seq);
      return { ...state, playing: false, follow: false, index: clamp(found, state.count) };
    }
    case 'first':
      return { ...state, playing: false, follow: false, index: clamp(0, state.count) };
    case 'last':
      return { ...state, playing: false, follow: true, index: clamp(state.count - 1, state.count) };
    case 'fps':
      return { ...state, fps: Math.min(60, Math.max(1, Math.round(action.fps))) };
  }
}

/** The frame at that event sequence, or the last frame before it. -1 when there are no frames. */
export function indexOfSeq(seqs: readonly number[], seq: number): number {
  if (seqs.length === 0) return -1;
  let answer = 0;
  for (let i = 0; i < seqs.length; i++) if (seqs[i]! <= seq) answer = i;
  return answer;
}

export function frameOf<T>(frames: readonly T[], t: Timeline): T | null {
  return t.index >= 0 && t.index < frames.length ? frames[t.index]! : null;
}
