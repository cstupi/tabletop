<!-- viewer/src/lib/play/TerrainHoverPanel.svelte -->
<!-- HoverLabels.cs's terrain panel: beside the pointer over the board, the hovered piece's name and height, then one chip per effect in
     the effect's colour (terrainEffects.ts). Kept inside the board and off the HUD's panels (cursorPanelAt over the frame's obstacles). -->
<script lang="ts">
  import HudIcon from './HudIcon.svelte';
  import { cursorPanelAt, type Rect } from './hudFrame';
  import type { TerrainHover } from './terrainHover';

  let { hover, at, obstacles = [] }: { hover: TerrainHover; at: { x: number; y: number }; obstacles?: readonly Rect[] } = $props();
  let width = $state(0), height = $state(0), stageW = $state(0), stageH = $state(0);
  let el: HTMLDivElement | undefined = $state();
  $effect(() => {
    const parent = el?.parentElement;
    if (parent) { stageW = parent.clientWidth; stageH = parent.clientHeight; }
  });
  const place = $derived(cursorPanelAt(at, { w: width, h: height }, { x: 0, y: 0, w: stageW, h: stageH }, obstacles));
</script>

<div class="panel hover" bind:this={el} bind:clientWidth={width} bind:clientHeight={height} style:left="{place.x}px" style:top="{place.y}px" role="tooltip">
  <div class="head"><strong>{hover.name}</strong><span class="dim">{hover.height}</span></div>
  <div class="chips">
    {#each hover.effects as e (e.name)}
      <span class="effect" style:--effect={e.colour}><HudIcon name={e.icon} size={18} />{e.word}</span>
    {/each}
  </div>
</div>

<style>
  .hover { position: absolute; z-index: 3; pointer-events: none; display: flex; flex-direction: column; gap: var(--space-2); margin: 0;
    padding: var(--space-2); max-width: 22rem; background: var(--panel-solid); box-shadow: 0 4px 14px #00000059; }
  .head { display: flex; align-items: baseline; gap: var(--space-2); }
  .head strong { font-family: var(--font-display, inherit); font-size: 1.1rem; }
  .dim { color: var(--dim); font-size: .9rem; }
  .chips { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-2); }
  .effect { display: inline-flex; align-items: center; gap: var(--space-2); padding: var(--space-2); border-radius: 6px; font-size: .95rem;
    border: 1px solid var(--effect); background: color-mix(in srgb, var(--effect) 20%, transparent); color: var(--text); }
  .effect :global(.icon) { color: var(--effect); }
</style>
