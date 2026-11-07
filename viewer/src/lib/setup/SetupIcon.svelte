<!-- viewer/src/lib/setup/SetupIcon.svelte -->
<!-- The setup panel's icons, the paths of Godot's client/content/themes/*/icons/<name>.svg, drawn in currentColor. -->
<script lang="ts">
  /** terrain, objective, table, zone, layout, remove, dice, add, or a terrain group's key (TERRAIN_KINDS). */
  let { name, size = 20 }: { name: string; size?: number } = $props();
</script>

<svg class="icon" width={size} height={size} viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"
  stroke-linejoin="round" aria-hidden="true">
  {#if name === 'terrain'}
    <path d="M3 26h26" /><path d="M5 26l7-11 5 7 3-4 7 8" /><path d="M23 13V8" /><path d="M20 10l3-6 3 6z" />
  {:else if name === 'objective'}
    <circle cx="16" cy="16" r="12" /><circle cx="16" cy="16" r="6" /><circle cx="16" cy="16" r="1.5" />
  {:else if name === 'table'}
    <path d="M3 11 H29 V15 H3 Z" /><path d="M7 15 V27 M25 15 V27" />
  {:else if name === 'zone'}
    <rect x="4" y="6" width="24" height="20" rx="1.5" /><rect x="4" y="6" width="24" height="5" fill="currentColor" fill-opacity="0.6" />
    <rect x="4" y="21" width="24" height="5" fill="currentColor" fill-opacity="0.6" />
  {:else if name === 'layout'}
    <rect x="9" y="4" width="18" height="14" rx="1.5" /><path d="M5 9v15a2 2 0 0 0 2 2h16" /><path d="M13 9h10M13 13h6" />
  {:else if name === 'remove'}
    <path d="M6 9 H26" /><path d="M13 9 V5 H19 V9" /><path d="M9 9 L10 27 H22 L23 9" /><path d="M14 13 V23 M18 13 V23" />
  {:else if name === 'dice'}
    <rect x="5" y="5" width="22" height="22" rx="4" />
    {#each [[11, 11], [21, 11], [16, 16], [11, 21], [21, 21]] as [cx, cy] (`${cx},${cy}`)}<circle {cx} {cy} r="1.4" fill="currentColor" />{/each}
  {:else if name === 'add'}
    <circle cx="16" cy="16" r="12" /><path d="M16 10v12M10 16h12" />
  {:else if name === 'blocks-sight'}
    <path d="M3 16 C8 8 24 8 29 16 C24 24 8 24 3 16 Z" /><path d="M16 12 A4 4 0 1 1 15.99 12 Z" /><path d="M5 28 L27 4" />
  {:else if name === 'impassable'}
    <path d="M16 4 A12 12 0 1 1 15.99 4 Z" /><path d="M7.5 7.5 L24.5 24.5" />
  {:else if name === 'dangerous'}
    <path d="M3 27 H29" /><path d="M5 27 L9 11 L13 27" /><path d="M12 27 L16 5 L20 27" /><path d="M19 27 L23 13 L27 27" />
  {:else if name === 'cover'}
    <path d="M16 4 L26 8 V16 C26 22 21 26 16 28 C11 26 6 22 6 16 V8 Z" /><path d="M16 9 V23" />
  {:else if name === 'difficult'}
    <path d="M3 11 Q7 7 11 11 T19 11 T27 11" /><path d="M3 18 Q7 14 11 18 T19 18 T27 18" /><path d="M3 25 Q7 21 11 25 T19 25 T27 25" />
  {:else}
    <path d="M3 23 H29" /><path d="M7 23 L9 18 M13 23 L14 17 M19 23 L21 18 M25 23 L26 19" />
  {/if}
</svg>

<style>
  .icon { flex: none; display: block; }
</style>
