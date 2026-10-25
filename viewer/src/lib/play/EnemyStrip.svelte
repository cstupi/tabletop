<!-- viewer/src/lib/play/EnemyStrip.svelte -->
<!-- The strip above the unit card: the hovered enemy's name, Defense and models alive, the distance, and each of my weapons' range
     within or beyond it. No claim about shooting. -->
<script lang="ts">
  import { rangeLine, stripLine } from './cardText';
  import type { EnemyStripModel } from './enemyStrip';

  let { strip }: { strip: EnemyStripModel } = $props();
</script>

<section class="strip" aria-label="Hovered enemy">
  <p class="name">{strip.name}</p>
  <p>{stripLine(strip)}</p>
  {#each strip.ranges as r (r.weapon)}
    {@const line = rangeLine(r)}
    <p class={line.tone}>{line.text}</p>
  {/each}
</section>

<style>
  .strip { padding: 0.35rem 0.5rem; border: 1px solid var(--line); border-radius: 4px; background: var(--panel); }
  p { margin: 0; overflow-wrap: anywhere; }
  .name { font-weight: 600; }
  .success { color: var(--success, #6ddf7c); }
  .warning { color: var(--warning, #ffc44e); }
</style>
