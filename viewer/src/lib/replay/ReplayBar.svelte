<!-- viewer/src/lib/replay/ReplayBar.svelte -->
<!-- ReplayBar.cs: the replay's controls at the page's foot. Back and forward by round, turn, action and step around Play/Pause; the
     scrubber over the whole log with the position; the speed. Space plays and pauses, the arrows step, Home and End jump. -->
<script lang="ts">
  import { textFocused } from '../play/keys';
  import { SPEEDS } from './replayClock';
  import type { ReplayBarModel, ReplayPlayer } from './replayPlayer';
  import type { ReplayUnit } from './replayTimeline';

  let { player, bar }: { player: ReplayPlayer; bar: ReplayBarModel } = $props();

  const BACK: readonly { unit: ReplayUnit; text: string; label: string }[] = [
    { unit: 'round', text: '«R', label: 'Back to the start of this round, or the round before' },
    { unit: 'turn', text: '«T', label: 'Back to the start of this turn, or the turn before' },
    { unit: 'action', text: '‹A', label: 'Back one action' },
    { unit: 'step', text: '‹', label: 'Back one step: a player\'s input and what the rules did with it' },
  ];
  const FORWARD: readonly { unit: ReplayUnit; text: string; label: string }[] = [
    { unit: 'step', text: '›', label: 'Forward one step: a player\'s input and what the rules did with it' },
    { unit: 'action', text: 'A›', label: 'Forward one action' },
    { unit: 'turn', text: 'T»', label: 'Forward to the next turn' },
    { unit: 'round', text: 'R»', label: 'Forward to the next round' },
  ];
  const speedText = (x: number): string => `${x}×`;

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || textFocused(e.target) || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key === ' ' && !(e.target instanceof HTMLButtonElement)) { player.togglePlay(); e.preventDefault(); }
    else if (e.key === 'ArrowLeft') { player.step(e.shiftKey ? 'action' : 'step', true); e.preventDefault(); }
    else if (e.key === 'ArrowRight') { player.step(e.shiftKey ? 'action' : 'step', false); e.preventDefault(); }
    else if (e.key === 'Home') { player.pause(); player.seek(0); e.preventDefault(); }
    else if (e.key === 'End') { player.pause(); player.seek(bar.count); e.preventDefault(); }
  }
</script>

<svelte:window onkeydown={onKeydown} />

<div class="replay-bar" role="toolbar" aria-label="Replay controls">
  <div class="steps">
    {#each BACK as b (b.unit)}
      <button type="button" class="small step" title={b.label} aria-label={b.label} disabled={bar.position === 0} onclick={() => player.step(b.unit, true)}>{b.text}</button>
    {/each}
    <button type="button" class="primary play" aria-label={bar.playing ? 'Pause' : 'Play'} title={bar.playing ? 'Pause (Space)' : 'Play the game forward one step at a time (Space)'}
      disabled={bar.count === 0} onclick={() => player.togglePlay()}>
      {#if bar.playing}
        <svg viewBox="0 0 16 16" aria-hidden="true"><rect x="3" y="2.5" width="3.5" height="11" /><rect x="9.5" y="2.5" width="3.5" height="11" /></svg>Pause
      {:else}
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 2.5 L13.5 8 L4 13.5 Z" /></svg>Play
      {/if}
    </button>
    {#each FORWARD as f (f.unit)}
      <button type="button" class="small step" title={f.label} aria-label={f.label} disabled={bar.position >= bar.count} onclick={() => player.step(f.unit, false)}>{f.text}</button>
    {/each}
  </div>
  <label class="scrub">
    <span class="visually-hidden">Position in the game</span>
    <input type="range" min="0" max={Math.max(1, bar.count)} step="1" value={bar.position} disabled={bar.count === 0}
      oninput={(e) => player.seek(Number(e.currentTarget.value))} />
  </label>
  <span class="caption" aria-live="off">{bar.caption}</span>
  <div class="seg speed" role="group" aria-label="Playback speed">
    {#each SPEEDS as x (x)}
      <button type="button" class="small" aria-pressed={bar.speed === x} onclick={() => (player.speed = x)}>{speedText(x)}</button>
    {/each}
  </div>
</div>

<style>
  .replay-bar {
    display: flex; align-items: center; gap: var(--s3) var(--s4); flex-wrap: wrap;
    padding: var(--s2) var(--s4); background: var(--panel-solid); border-top: 1px solid var(--rule);
  }
  .steps { display: flex; align-items: center; gap: var(--s1); flex: none; }
  .step { min-width: 2.4rem; padding: 0 var(--s2); font-family: var(--font-num, inherit); }
  .play { min-width: 6.5rem; margin: 0 var(--s2); }
  .play svg { width: 0.9rem; height: 0.9rem; fill: currentColor; }
  .scrub { flex: 1 1 14rem; display: flex; align-items: center; min-width: 10rem; }
  .scrub input { width: 100%; accent-color: var(--accent); }
  .caption { flex: none; min-width: 11rem; font-size: .9rem; color: var(--dim); font-family: var(--font-num, inherit); font-variant-numeric: tabular-nums; white-space: nowrap; }
  .speed { flex: none; }
  .speed button { min-width: 2.75rem; padding: 0 var(--s2); }
  .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
