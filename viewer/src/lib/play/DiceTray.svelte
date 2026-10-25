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
  /* The latest roll, a line of the log's footer: text, not a themed button. */
  .tray { display: flex; flex-wrap: wrap; gap: var(--s1) var(--s2); align-items: center; flex: 1; min-width: 0; min-height: 2rem; padding: 2px var(--s1);
    border: 0; border-radius: 0; clip-path: none; box-shadow: none; background: none; color: var(--text); text-align: left; white-space: normal; font-size: .9rem; }
  .tray:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .title { color: var(--dim); }
  .die { font-size: 1.5rem; line-height: 1; }
  .die.fail { opacity: 0.4; }
  .ok { color: var(--success); }
  .dim { color: var(--dim); }
</style>
