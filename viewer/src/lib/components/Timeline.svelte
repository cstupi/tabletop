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
