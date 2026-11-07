<!-- viewer/src/lib/play/HudIcon.svelte -->
<!-- One of Godot's theme icons (iconPaths.ts) in currentColor, at a size. -->
<script lang="ts">
  import { ICON_PATHS } from './iconPaths';

  let { name, size = 20 }: { name: string; size?: number } = $props();
  const paths = $derived(ICON_PATHS[name] ?? { stroke: [], fill: [] });
</script>

<svg class="icon" width={size} height={size} viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"
  stroke-linejoin="round" aria-hidden="true">
  {#each paths.stroke as d (d)}<path {d} />{/each}
  {#each paths.fill as d (d)}<path {d} fill="currentColor" stroke-width="1.5" />{/each}
</svg>

<style>
  .icon { flex: none; display: block; }
</style>
