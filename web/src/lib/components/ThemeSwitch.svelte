<script lang="ts">
  import { THEMES, applyTheme, loadTheme, saveTheme, type ThemeId } from '$lib/theme/theme';
  // static/theme-init.js has already set data-theme from the same store.
  let current = $state<ThemeId>(loadTheme());
  function choose(id: ThemeId) { current = id; applyTheme(id); saveTheme(id); }
</script>

<div class="seg theme-switch" role="group" aria-label="Theme">
  {#each THEMES as t (t.id)}
    <button type="button" aria-pressed={current === t.id} onclick={() => choose(t.id)}>{t.name}</button>
  {/each}
</div>
