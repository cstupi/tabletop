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
  .row { display: grid; grid-template-columns: 3.5rem 2rem 1fr; gap: var(--space-2); width: 100%;
    text-align: left; background: none; border: 0; border-bottom: 1px solid var(--line);
    border-radius: 0; padding: var(--space-1) var(--space-2); }
  .row[data-current='true'] { background: var(--panel); color: #fff; }
  .row.warned .text { color: #ffc44e; }
  .text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
