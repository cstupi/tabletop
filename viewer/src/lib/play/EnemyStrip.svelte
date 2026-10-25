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
  .strip { margin: 0; padding: var(--s2) var(--s3); background: var(--panel-solid); border: 1px solid var(--rule); border-left: 3px solid var(--warning); font-size: .9rem; }
  p { margin: 0; overflow-wrap: anywhere; }
  .name { font-weight: 700; }
  .success { color: var(--success); }
  .warning { color: var(--warning); }
</style>
