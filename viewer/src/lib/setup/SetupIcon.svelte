<!-- viewer/src/lib/setup/SetupIcon.svelte -->
<!-- The setup panel's icons, the paths of Godot's client/content/themes/*/icons/<name>.svg, drawn in currentColor. -->
<script lang="ts">
  import HudIcon from '../play/HudIcon.svelte';
  /** terrain, objective, table, zone, layout, remove, dice, add, or a terrain group's key (TERRAIN_KINDS), drawn by HudIcon as the play Hud draws it. */
  let { name, size = 20 }: { name: string; size?: number } = $props();
  const OWN = new Set(['terrain', 'objective', 'table', 'zone', 'layout', 'remove', 'dice', 'add']);
</script>

{#if !OWN.has(name)}
<HudIcon {name} {size} />
{:else}

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
  {/if}
</svg>
{/if}

<style>
  .icon { flex: none; display: block; }
</style>
