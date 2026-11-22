<script lang="ts">
  // The two theme swatch tiles of the Godot Settings dialog: each drawn in its own theme's colours.
  import { THEMES, applyTheme, loadTheme, saveTheme, type ThemeId } from '$lib/theme/theme';
  // static/theme-init.js has already set data-theme from the same store.
  let current = $state<ThemeId>(loadTheme());
  function choose(id: ThemeId) { current = id; applyTheme(id); saveTheme(id); }
</script>

<div class="swatches" role="group" aria-label="Theme">
  {#each THEMES as t (t.id)}
    <button type="button" class="swatch {t.id}" aria-pressed={current === t.id} onclick={() => choose(t.id)}>
      <span class="sample" aria-hidden="true"><i></i><i></i><i></i></span>{t.name}
    </button>
  {/each}
</div>
<style>
  .swatches { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); }
  .swatch { flex-direction: column; gap: var(--space-1); min-height: 4rem; padding: var(--space-2); font-size: .9rem; }
  .sample { display: flex; width: 100%; height: 1.1rem; border: 1px solid #0006; }
  .sample i { flex: 1; }
  .scifi .sample i:nth-child(1) { background: #070B10 } .scifi .sample i:nth-child(2) { background: #3FD0E0 } .scifi .sample i:nth-child(3) { background: #8C9CFF }
  .fantasy .sample i:nth-child(1) { background: #21150C } .fantasy .sample i:nth-child(2) { background: #C9A04E } .fantasy .sample i:nth-child(3) { background: #8E2A1E }
</style>
