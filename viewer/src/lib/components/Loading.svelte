<!-- viewer/src/lib/components/Loading.svelte -->
<!-- A wait: one line and, with a total, a bar under it. Blocking: a panel centred in its container. `strip`: a small panel over the
     top of a board that does not block it. `inline`: the line in the flow of a page or dialog. Nothing shows for the first
     SHOW_AFTER_MS. -->
<script lang="ts">
  import { revealAfter, SHOW_AFTER_MS } from './loading';

  let { text, done = 0, total, strip = false, inline = false, delay = SHOW_AFTER_MS }:
    { text: string; done?: number; total?: number | undefined; strip?: boolean; inline?: boolean; delay?: number } = $props();

  let shown = $state(false);
  $effect(() => revealAfter(delay, () => { shown = true; }));
</script>

{#if shown}
  <div class="wait" class:strip class:inline class:block={!strip && !inline} role="status">
    <div class="box" class:panel={!inline}>
      <p class="loading">{text}</p>
      {#if total !== undefined && total > 0}<progress max={total} value={Math.min(done, total)}></progress>{/if}
    </div>
  </div>
{/if}

<style>
  .block { display: grid; place-items: center; height: 100%; min-height: 8rem; padding: var(--space-3); box-sizing: border-box; }
  .block .box { width: min(20rem, 100%); padding: var(--space-3) var(--space-4); margin: 0; }
  .strip { position: absolute; z-index: 3; top: var(--space-2); left: 50%; transform: translateX(-50%); pointer-events: none;
    max-width: calc(100% - 2 * var(--space-3)); }
  .strip .box { width: 15rem; padding: var(--space-2) var(--space-3); margin: 0; }
  .strip .loading { font-size: .9rem; }
  .loading { padding: 0; margin: 0; white-space: nowrap; }
  .inline .loading { padding: var(--space-3) 0; }
  progress { display: block; height: .25rem; margin-top: var(--space-2); }
  .inline progress { max-width: 20rem; margin-top: 0; }
</style>
