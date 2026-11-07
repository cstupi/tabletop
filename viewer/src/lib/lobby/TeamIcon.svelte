<!-- viewer/src/lib/lobby/TeamIcon.svelte -->
<!-- The team columns' icons, the paths of Godot's client/content/themes/*/icons/<name>.svg, drawn in currentColor. -->
<script lang="ts">
  let { name, size = 20 }: { name: 'team' | 'player' | 'army' | 'watch' | 'warning' | 'ready'; size?: number } = $props();
</script>

<svg class="icon" width={size} height={size} viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"
  stroke-linejoin="round" aria-hidden="true">
  {#if name === 'team'}
    <path d="M8 29V4" /><path d="M8 5h17l-4 6 4 6H8" fill="currentColor" fill-opacity="0.85" />
  {:else if name === 'player'}
    <circle cx="16" cy="10" r="5.5" /><path d="M5.5 28c0-6 4.7-10 10.5-10s10.5 4 10.5 10" />
  {:else if name === 'army'}
    <path d="M16 3l11 4v8c0 7-4.8 11.6-11 14C9.8 26.6 5 22 5 15V7z" /><path d="M16 9v14M10 15h12" />
  {:else if name === 'watch'}
    <path d="M2.5 16S7.5 7 16 7s13.5 9 13.5 9-5 9-13.5 9S2.5 16 2.5 16z" /><circle cx="16" cy="16" r="4.5" />
  {:else if name === 'warning'}
    <path d="M16 4L29 27H3z" /><path d="M16 12v7" /><circle cx="16" cy="23" r="0.6" fill="currentColor" />
  {:else}
    <circle cx="16" cy="16" r="12.5" /><path d="M10 16.5l4 4 8-9" />
  {/if}
</svg>

<style>
  .icon { flex: none; display: block; }
</style>
