<!-- viewer/src/lib/play/DecisionIcon.svelte -->
<!-- The decision card's icons, the paths of Godot's client/content/themes/*/icons/<name>.svg, drawn in currentColor. -->
<script lang="ts">
  let { name, size = 20 }: { name: string; size?: number } = $props();
</script>

<svg class="icon" width={size} height={size} viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"
  stroke-linejoin="round" aria-hidden="true">
  {#if name === 'activation'}
    <circle cx="16" cy="16" r="12.5" /><path d="M13 10 L22 16 L13 22 Z" />
  {:else if name === 'hold'}
    <path d="M11 8 V24 M21 8 V24" />
  {:else if name === 'advance'}
    <path d="M5 16 H25 M18 9 L25 16 L18 23" />
  {:else if name === 'rush'}
    <path d="M6 8 L14 16 L6 24 M16 8 L24 16 L16 24" />
  {:else if name === 'charge'}
    <path d="M5 5 L21 21 M20 16 L16 20 M22 22 L27 27" /><path d="M27 5 L11 21 M12 16 L16 20 M10 22 L5 27" />
  {:else if name === 'shoot'}
    <circle cx="16" cy="16" r="10" /><circle cx="16" cy="16" r="4.5" /><path d="M16 2 V8 M16 24 V30 M2 16 H8 M24 16 H30" />
  {:else if name === 'melee'}
    <path d="M7 25 L23 9 L26 6 L25 10 L23 9 M10 18 L14 22 M7 25 L5 27" />
  {:else if name === 'cast'}
    <path d="M16 4 L18.5 13.5 L28 16 L18.5 18.5 L16 28 L13.5 18.5 L4 16 L13.5 13.5 Z" />
  {:else if name === 'wound'}
    <path d="M16 4 C13 9 8 14 8 19.5 A8 8 0 0 0 24 19.5 C24 14 19 9 16 4 Z" />
  {:else if name === 'key'}
    <rect x="4" y="9" width="24" height="14" rx="2" /><path d="M8.5 13.5 H9.5 M13.5 13.5 H14.5 M18.5 13.5 H19.5 M23 13.5 H23.5 M10 18.5 H22" />
  {:else if name === 'army'}
    <path d="M16 3l11 4v8c0 7-4.8 11.6-11 14C9.8 26.6 5 22 5 15V7z" /><path d="M16 9v14M10 15h12" />
  {:else if name === 'skip'}
    <path d="M24 7 V25" /><path d="M7 7 L20 16 L7 25 Z" fill="currentColor" fill-opacity="0.85" />
  {:else if name === 'remove'}
    <path d="M6 9 H26 M13 9 V5 H19 V9 M9 9 L10 27 H22 L23 9 M14 13 V23 M18 13 V23" />
  {:else if name === 'refresh'}
    <path d="M26 16a10 10 0 1 1-3-7.1" /><path d="M26 4v6h-6" />
  {:else if name === 'team'}
    <path d="M8 29V4" /><path d="M8 5h17l-4 6 4 6H8" fill="currentColor" fill-opacity="0.85" />
  {:else}
    <circle cx="16" cy="16" r="12.5" /><path d="M10 16.5l4 4 8-9" />
  {/if}
</svg>

<style>
  .icon { flex: none; display: block; }
</style>
