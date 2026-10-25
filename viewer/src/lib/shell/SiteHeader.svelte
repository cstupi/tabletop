<!-- viewer/src/lib/shell/SiteHeader.svelte -->
<!-- The website's header (web/src/routes/+layout.svelte), so Play reads as a page of the site: its links go back to the site. -->
<script lang="ts">
  import Icon from '../../../../web/src/lib/components/Icon.svelte';
  import { THEMES, chooseTheme, loadTheme, type ThemeId } from './theme';

  let { account }: { account: string | null } = $props();
  let theme: ThemeId = $state(loadTheme());
  function choose(id: ThemeId) { theme = id; chooseTheme(id); }
</script>

<header class="site-header">
  <nav class="site-nav" aria-label="Main">
    <a class="brand" href="/armies"><Icon name="objective" size="1.5em" />Tabletop</a>
    <span class="links">
      <a href="/armies"><Icon name="armies" />Armies</a>
      <a href="/library"><Icon name="library" />Library</a>
      <a href="/download"><Icon name="download" /><span class="label">Download</span></a>
      <a href="#/games" aria-current="page"><Icon name="replay" /><span class="label">Play</span></a>
      <a href="/account"><Icon name="account" /><span class="visually-hidden">Account: </span><span class="account-name">{account ?? '…'}</span></a>
    </span>
    <span class="push"></span>
    <div class="seg theme-switch" role="group" aria-label="Theme">
      {#each THEMES as t (t.id)}
        <button type="button" aria-pressed={theme === t.id} onclick={() => choose(t.id)}>{t.name}</button>
      {/each}
    </div>
  </nav>
</header>

<style>
  /* The site's nav is centred at the page width; the play area runs edge to edge, so the header does too. */
  .site-nav { max-width: none; }
</style>
