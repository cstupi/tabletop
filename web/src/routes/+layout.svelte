<script lang="ts">
  import '../themes.css';
  import '../app.css';
  import { onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { session } from '$lib/session/session.svelte';
  import { loginHref } from '$lib/session/redirect';
  import { REPLAYS_URL } from '$lib/replays';
  import Icon from '$lib/components/Icon.svelte';
  import Menu from '$lib/components/Menu.svelte';
  import ThemeSwitch from '$lib/components/ThemeSwitch.svelte';

  let { children } = $props();
  const publicRoutes = ['/login', '/reset-password'];
  const isPublic = $derived(publicRoutes.some((p) => page.url.pathname.startsWith(p)));

  onMount(() => { void session.restore(); return session.startRefreshLoop(); });

  // A private page without a session goes to sign-in, which returns here afterwards.
  $effect(() => {
    if (!session.ready) return;
    if (!session.token && !isPublic) void goto(loginHref(page.url.pathname + page.url.search, session.expired), { replaceState: true });
  });

  async function logout() { await session.logout(); await goto('/login'); }
  const current = (prefix: string) => (page.url.pathname.startsWith(prefix) ? 'page' : undefined);
</script>

<a class="skip" href="#main">Skip to content</a>
<header class="site-header">
  <nav class="site-nav" aria-label="Main">
    <a class="brand" href={session.token ? '/armies' : '/login'}><Icon name="objective" size="1.5em" />Tabletop</a>
    {#if session.token}
      <span class="links">
        <a href="/armies" aria-current={current('/armies')}><Icon name="armies" />Armies</a>
        <a href="/library" aria-current={current('/library')}><Icon name="library" />Library</a>
        <a href="/download" aria-current={current('/download')}><Icon name="download" /><span class="label">Download</span></a>
        <!-- The viewer is a separate static app, not a route here: a full page load. -->
        <a href={REPLAYS_URL} data-sveltekit-reload><Icon name="replay" /><span class="label">Play</span></a>
      </span>
    {/if}
    <span class="push"></span>
    <!-- one menu: the account (its page and Log out) and the theme -->
    <Menu label={session.token ? `Account: ${session.account?.name ?? ''}` : 'Theme'} icon="caret" text={session.token ? (session.account?.name ?? '…') : undefined}>
      {#if session.token}
        <a class="button" href="/account" aria-current={current('/account')}><Icon name="account" />Account</a>
      {/if}
      <ThemeSwitch />
      {#if session.token}<button onclick={logout}><Icon name="logout" />Log out</button>{/if}
    </Menu>
  </nav>
</header>
<main id="main" tabindex="-1">
  {#if isPublic || (session.ready && session.token)}{@render children()}{:else}<p class="loading" role="status">Loading…</p>{/if}
</main>
