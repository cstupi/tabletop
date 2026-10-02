# Task 6: The timeline — play, pause, step, scrub, and jump from a log line

**Files:**
- Create: `viewer/src/lib/timeline/timeline.ts`, `viewer/src/lib/components/Timeline.svelte`, `viewer/src/lib/components/LogPanel.svelte`
- Test: `viewer/tests/timeline.test.ts`

**Interfaces:**
- Consumes: `RecordedFrame` (Task 2).
- Produces: `type Timeline = { readonly count: number; readonly index: number; readonly playing: boolean; readonly fps: number; readonly follow: boolean }`; `const EMPTY: Timeline`; `type TimelineAction` (the eleven below: `frames`, `play`, `pause`, `toggle`, `tick`, `step`, `scrub`, `jump`, `first`, `last`, `fps`); `function reduce(state: Timeline, action: TimelineAction): Timeline`; `function indexOfSeq(seqs: readonly number[], seq: number): number`; `function frameOf<T>(frames: readonly T[], t: Timeline): T | null`.
- `follow` is what makes one reducer serve both feeds: a recording arrives complete, so `follow` starts `false` and the timeline sits on frame 0; the live feed appends, so `follow` starts `true` and every new frame becomes the current one until the user steps or scrubs, which drops it.

Spec: spec → *`viewer/` is a developer web app* — "a timeline with play, step and scrub. The log sits beside the board and clicking a line jumps to that sequence." Spec → *Testing* — "the timeline reducer".

- [ ] **Step 1: Write `viewer/tests/timeline.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { EMPTY, frameOf, indexOfSeq, reduce, type Timeline } from '../src/lib/timeline/timeline';

const of = (count: number, over: Partial<Timeline> = {}): Timeline => ({ ...EMPTY, count, index: 0, ...over });

describe('the timeline reducer', () => {
  it('starts empty, with no frame to show', () => {
    expect(EMPTY).toEqual({ count: 0, index: -1, playing: false, fps: 8, follow: false });
    expect(frameOf(['a'], EMPTY)).toBeNull();
  });

  it('takes a frame count and lands on the first frame', () => {
    expect(reduce(EMPTY, { type: 'frames', count: 5 })).toMatchObject({ count: 5, index: 0 });
  });

  it('follows the end of a growing feed until the user steps, then stops following', () => {
    let t = reduce({ ...EMPTY, follow: true }, { type: 'frames', count: 3 });
    expect(t.index).toBe(2);
    t = reduce(t, { type: 'frames', count: 4 });
    expect(t.index).toBe(3);
    t = reduce(t, { type: 'step', by: -1 });
    expect(t).toMatchObject({ index: 2, follow: false, playing: false });
    expect(reduce(t, { type: 'frames', count: 9 }).index).toBe(2);
  });

  it('steps and clamps at both ends', () => {
    expect(reduce(of(3), { type: 'step', by: -1 }).index).toBe(0);
    expect(reduce(of(3, { index: 2 }), { type: 'step', by: 1 }).index).toBe(2);
    expect(reduce(of(3), { type: 'step', by: 5 }).index).toBe(2);
    expect(reduce(of(3, { index: 1 }), { type: 'first' }).index).toBe(0);
    expect(reduce(of(3, { index: 1 }), { type: 'last' })).toMatchObject({ index: 2, follow: true });
  });

  it('plays, ticks, and stops itself at the last frame', () => {
    let t = reduce(of(3), { type: 'play' });
    expect(t.playing).toBe(true);
    t = reduce(t, { type: 'tick' });
    expect(t.index).toBe(1);
    t = reduce(t, { type: 'tick' });
    expect(t).toMatchObject({ index: 2, playing: false });
    expect(reduce(t, { type: 'tick' }).index).toBe(2);
    expect(reduce(of(3), { type: 'toggle' }).playing).toBe(true);
    expect(reduce(of(3, { playing: true }), { type: 'toggle' }).playing).toBe(false);
  });

  it('scrubs to a clamped index and pauses', () => {
    expect(reduce(of(5, { playing: true }), { type: 'scrub', index: 3 })).toMatchObject({ index: 3, playing: false, follow: false });
    expect(reduce(of(5), { type: 'scrub', index: 99 }).index).toBe(4);
    expect(reduce(of(5), { type: 'scrub', index: -3 }).index).toBe(0);
  });

  it('clamps the speed', () => {
    expect(reduce(of(2), { type: 'fps', fps: 0 }).fps).toBe(1);
    expect(reduce(of(2), { type: 'fps', fps: 500 }).fps).toBe(60);
  });

  it('jumps to a sequence, and to the frame that precedes one the log does not have', () => {
    const seqs = [1, 2, 5, 9];
    expect(indexOfSeq(seqs, 5)).toBe(2);
    expect(indexOfSeq(seqs, 7)).toBe(2);
    expect(indexOfSeq(seqs, 1)).toBe(0);
    expect(indexOfSeq(seqs, 0)).toBe(0);
    expect(indexOfSeq(seqs, 99)).toBe(3);
    expect(indexOfSeq([], 4)).toBe(-1);
    expect(reduce(of(4, { playing: true }), { type: 'jump', seq: 7, seqs })).toMatchObject({ index: 2, playing: false });
  });

  it('hands back the frame at the index, or null', () => {
    expect(frameOf(['a', 'b', 'c'], of(3, { index: 2 }))).toBe('c');
    expect(frameOf(['a'], of(1, { index: 4 }))).toBeNull();
  });

  it('shrinks with the feed rather than pointing past the end', () => {
    expect(reduce(of(9, { index: 8 }), { type: 'frames', count: 3 }).index).toBe(2);
    expect(reduce(of(9, { index: 8 }), { type: 'frames', count: 0 })).toMatchObject({ count: 0, index: -1 });
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `timeline.ts`**

```ts
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
    case 'play':
      return state.count === 0 ? state : { ...state, playing: true };
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
```

- [ ] **Step 3: Write `Timeline.svelte`**

```svelte
<!-- viewer/src/lib/components/Timeline.svelte -->
<script lang="ts">
  import type { Timeline, TimelineAction } from '../timeline/timeline';

  let { timeline, dispatch, seq, line }:
    { timeline: Timeline; dispatch: (a: TimelineAction) => void; seq: number | null; line: string } = $props();

  $effect(() => {
    if (!timeline.playing) return;
    const handle = setInterval(() => dispatch({ type: 'tick' }), 1000 / timeline.fps);
    return () => clearInterval(handle);
  });
</script>

<svelte:window onkeydown={(e) => {
  if (e.target instanceof HTMLInputElement) return;
  if (e.key === ' ') { e.preventDefault(); dispatch({ type: 'toggle' }); }
  if (e.key === 'ArrowLeft') dispatch({ type: 'step', by: e.shiftKey ? -10 : -1 });
  if (e.key === 'ArrowRight') dispatch({ type: 'step', by: e.shiftKey ? 10 : 1 });
  if (e.key === 'Home') dispatch({ type: 'first' });
  if (e.key === 'End') dispatch({ type: 'last' });
}} />

<div class="timeline">
  <button onclick={() => dispatch({ type: 'first' })} disabled={timeline.count === 0} title="Home">|&lt;</button>
  <button onclick={() => dispatch({ type: 'step', by: -1 })} disabled={timeline.index <= 0}>&lt;</button>
  <button onclick={() => dispatch({ type: 'toggle' })} disabled={timeline.count === 0}>{timeline.playing ? 'Pause' : 'Play'}</button>
  <button onclick={() => dispatch({ type: 'step', by: 1 })} disabled={timeline.index >= timeline.count - 1}>&gt;</button>
  <button onclick={() => dispatch({ type: 'last' })} disabled={timeline.count === 0} title="End">&gt;|</button>
  <input
    type="range" min="0" max={Math.max(0, timeline.count - 1)} value={Math.max(0, timeline.index)}
    disabled={timeline.count === 0}
    oninput={(e) => dispatch({ type: 'scrub', index: Number(e.currentTarget.value) })} />
  <span class="muted">
    frame {timeline.index + 1}/{timeline.count}{seq === null ? '' : ` · seq ${seq}`}{timeline.follow ? ' · following' : ''}
  </span>
  <label class="muted">fps
    <input type="number" min="1" max="60" value={timeline.fps} style="width: 4rem"
      oninput={(e) => dispatch({ type: 'fps', fps: Number(e.currentTarget.value) })} />
  </label>
  <span class="line">{line}</span>
</div>

<style>
  .timeline { display: flex; gap: 0.4rem; align-items: center; padding: 0.4rem 0.6rem; border-top: 1px solid var(--line); }
  input[type='range'] { flex: 1 1 12rem; }
  .line { flex: 2 1 20rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
```

- [ ] **Step 4: Write `LogPanel.svelte`**

```svelte
<!-- viewer/src/lib/components/LogPanel.svelte -->
<script lang="ts">
  import type { RecordedFrame } from '../protocol/recording.generated';

  let { frames, index, onJump }:
    { frames: readonly RecordedFrame[]; index: number; onJump: (seq: number) => void } = $props();

  let list: HTMLDivElement | undefined = $state();

  $effect(() => {
    void index;
    list?.querySelector<HTMLElement>('[data-current="true"]')?.scrollIntoView({ block: 'nearest' });
  });
</script>

<div class="log" bind:this={list}>
  {#each frames as frame (frame.index)}
    <button
      class="row" data-current={frame.index === index}
      class:warned={frame.warning !== undefined}
      onclick={() => onJump(frame.seq)}
      title={frame.kind}>
      <span class="seq muted">{frame.seq}</span>
      <span class="round muted">r{frame.round}</span>
      <span class="text">{frame.line}</span>
    </button>
  {/each}
  {#if frames.length === 0}<p class="muted">no frames</p>{/if}
</div>

<style>
  .log { overflow-y: auto; height: 100%; }
  .row { display: grid; grid-template-columns: 3.5rem 2rem 1fr; gap: 0.4rem; width: 100%;
    text-align: left; background: none; border: 0; border-bottom: 1px solid var(--line);
    border-radius: 0; padding: 0.15rem 0.4rem; }
  .row[data-current='true'] { background: var(--panel); color: #fff; }
  .row.warned .text { color: #ffc44e; }
  .text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
```

- [ ] **Step 5: Run the tests; expect 10 more passed**

Run: `cd viewer && npm test && npm run check`

- [ ] **Step 6: Commit**

```bash
git add viewer/src/lib/timeline viewer/src/lib/components/Timeline.svelte viewer/src/lib/components/LogPanel.svelte viewer/tests/timeline.test.ts
git commit -m "Reduce the timeline over one frame list and jump from a log line"
```
