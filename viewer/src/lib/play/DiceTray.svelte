<!-- viewer/src/lib/play/DiceTray.svelte -->
<!-- DiceTray.cs: the latest roll as the combat log's collapsed entry: title, faces as pips (bright on a success, faded on a failure, an
     unjudged die plain), the result in success or dim colour; the collapsed line is the tooltip. Clicking it opens the log there. The
     tray does not tumble its faces: the viewer has no Animate setting. -->
<script lang="ts">
  import { pip } from './combatLog';
  import { trayLines } from './logRows';
  import type { RollEntry } from './rollEntry';

  let { roll, onClick }: { roll: RollEntry | null; onClick: (rollId: string) => void } = $props();

  const lines = $derived(trayLines(roll));
</script>

{#if lines !== null && roll !== null}
  <button type="button" class="tray" tabindex="-1" title={lines.tooltip} onmousedown={(e) => e.preventDefault()} onclick={() => onClick(roll.record.id)}>
    <span class="title">{lines.title}</span>
    {#each lines.faces as d, i (i)}
      <span class="die" class:fail={d.judged && !d.success}>{pip(d.raw)}</span>
    {/each}
    {#if lines.result !== null}<span class:ok={lines.success} class:dim={!lines.success}>{lines.result}</span>{/if}
  </button>
{/if}

<style>
  .tray { display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center; width: 100%; text-align: left; padding: 0.3rem 0.5rem; }
  .die { font-size: 1.6rem; line-height: 1; }
  .die.fail { opacity: 0.45; }
  .ok { color: var(--success, #6ddf7c); }
  .dim { color: var(--muted); }
</style>
