<!-- viewer/src/lib/play/DiceTray.svelte -->
<!-- DiceTray.cs (UX redesign part 2 → Dice tray): the latest roll: the dice icon and its title, then its faces large in the outcome's colour
     (a pass in success, a fail in failure, an unjudged die plain) and the result in its outcome's colour; the collapsed line is the tooltip. Clicking it opens the log there. The
     tray does not tumble its faces: the viewer has no Animate setting. -->
<script lang="ts">
  import { pip } from './combatLog';
  import { trayLines } from './logRows';
  import { face } from './rollEntry';
  import type { RollEntry } from './rollEntry';
  import HudIcon from './HudIcon.svelte';

  let { roll, onClick }: { roll: RollEntry | null; onClick: (rollId: string) => void } = $props();

  const lines = $derived(trayLines(roll));
</script>

{#if lines !== null && roll !== null}
  <button type="button" class="tray" tabindex="-1" title={lines.tooltip} onmousedown={(e) => e.preventDefault()} onclick={() => onClick(roll.record.id)}>
    <span class="title"><HudIcon name="dice" size={16} />{lines.title}</span>
    {#each lines.faces as d, i (i)}
      <span class="die" class:pass={d.judged && d.success} class:fail={d.judged && !d.success}>{pip(d.raw)}{face(roll, d) === d.raw ? '' : `→${face(roll, d)}`}</span>
    {/each}
    {#if lines.result !== null}<span class:ok={lines.success} class:bad={!lines.success}>{lines.result}</span>{/if}
  </button>
{/if}

<style>
  /* The latest roll, a line of the log's footer: text, not a themed button. */
  .tray { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-2); align-items: center; flex: 1; min-width: 0; min-height: 2rem; padding: var(--space-1) var(--space-1);
    border: 0; border-radius: 0; clip-path: none; border-image: none; box-shadow: none; background: none; color: var(--text); text-align: left; white-space: normal; font-size: .9rem; }
  .tray:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .title { flex-basis: 100%; display: inline-flex; align-items: center; gap: var(--space-1); color: var(--text); }
  .title :global(.hud-icon) { color: var(--accent); }
  .die { font-size: 2.1rem; line-height: 1; }
  .die.pass { color: var(--success); }
  .die.fail { color: var(--failure); opacity: .7; }
  .ok { color: var(--success); }
  .bad { color: var(--failure); }
</style>
