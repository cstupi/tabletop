<!-- viewer/src/lib/replay/ReplayBar.svelte -->
<!-- ReplayBar.cs: the replay's controls at the page's foot (replayBarView.ts). The REPLAY title, labelled icon buttons back by round, turn, action and step
     around Play/Pause and forward by step, action, turn and round; the scrubber over the whole log with a tick at each round's start;
     the speed; the caption, the position's block header. Space plays and pauses, the arrows step, Home and End jump. -->
<script lang="ts">
  import HudIcon from '../play/HudIcon.svelte';
  import { textFocused } from '../play/keys';
  import { BACK, FORWARD, type ReplayTick } from './replayBarView';
  import { SPEEDS } from './replayClock';
  import type { ReplayBarModel, ReplayPlayer } from './replayPlayer';

  let { player, bar, ticks, caption }: { player: ReplayPlayer; bar: ReplayBarModel; ticks: readonly ReplayTick[]; caption: string } = $props();
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
  <div class="title"><HudIcon name="replay" size={18} /><span>Replay</span></div>
  <div class="steps">
    {#each BACK as b (b.unit)}
      <button type="button" class="flat step" title={b.tip} aria-label={b.tip} disabled={bar.position === 0} onclick={() => player.step(b.unit, true)}>
        <HudIcon name={b.icon} size={20} /><span>{b.label}</span>
      </button>
    {/each}
    <button type="button" class="primary play" aria-label={bar.playing ? 'Pause' : 'Play'} title={bar.playing ? 'Pause (Space)' : 'Play the game forward one step at a time (Space)'}
      disabled={bar.count === 0} onclick={() => player.togglePlay()}>
      <HudIcon name={bar.playing ? 'pause' : 'play'} size={20} /><span>{bar.playing ? 'Pause' : 'Play'}</span>
    </button>
    {#each FORWARD as f (f.unit)}
      <button type="button" class="flat step" title={f.tip} aria-label={f.tip} disabled={bar.position >= bar.count} onclick={() => player.step(f.unit, false)}>
        <HudIcon name={f.icon} size={20} /><span>{f.label}</span>
      </button>
    {/each}
  </div>
  <div class="line">
    <label class="scrub" title={`${bar.position} of ${bar.count}`}>
      <span class="visually-hidden">Position in the game</span>
      <input type="range" min="0" max={Math.max(1, bar.count)} step="1" value={bar.position} disabled={bar.count === 0}
        oninput={(e) => player.seek(Number(e.currentTarget.value))} />
      <span class="ticks" aria-hidden="true">
        {#each ticks as t (t.at)}<span class="tick" style:left="{t.at * 100}%"><span>{t.round}</span></span>{/each}
      </span>
    </label>
    <div class="seg speed" role="group" aria-label="Playback speed">
      {#each SPEEDS as x (x)}
        <button type="button" class="small" aria-pressed={bar.speed === x} onclick={() => (player.speed = x)}>{speedText(x)}</button>
      {/each}
    </div>
  </div>
  <p class="caption" aria-live="off" title={caption}>{caption}</p>
</div>

<style>
  .replay-bar { display: flex; flex-direction: column; gap: var(--space-2); padding: var(--space-2) var(--space-3); background: var(--panel-solid); border-top: 1px solid var(--rule); }
  .title { display: flex; align-items: center; gap: var(--space-2); color: var(--accent); font-family: var(--display, inherit); text-transform: uppercase; letter-spacing: .06em; font-size: .9rem; }
  .steps { display: flex; align-items: stretch; justify-content: center; gap: var(--space-1); flex-wrap: wrap; }
  .step, .play { display: flex; flex-direction: column; align-items: center; gap: var(--space-1); min-width: 3.4rem; padding: var(--space-2); }
  .step span, .play span { font-size: .72rem; text-transform: uppercase; letter-spacing: .04em; }
  .flat { background: transparent; border: 1px solid transparent; color: var(--text); }
  .flat:hover:not(:disabled) { border-color: var(--rule); }
  .flat:disabled { color: var(--dim); }
  .play { min-width: 4.6rem; margin: 0 var(--space-2); }
  .line { display: flex; align-items: center; gap: var(--space-3); }
  .scrub { position: relative; flex: 1 1 14rem; display: flex; align-items: center; min-width: 10rem; padding-bottom: var(--space-3); }
  .scrub input { width: 100%; accent-color: var(--accent); }
  .ticks { position: absolute; left: .5rem; right: .5rem; top: 0; bottom: 0; pointer-events: none; }
  .tick { position: absolute; top: 2px; height: 1.1rem; border-left: 2px solid var(--dim); }
  .tick span { position: absolute; top: 1.05rem; left: -.25rem; font-size: .7rem; color: var(--dim); }
  .caption { margin: 0; text-align: center; font-size: .95rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .speed { flex: none; }
  .speed button { min-width: 2.75rem; padding: 0 var(--space-2); }
  .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
