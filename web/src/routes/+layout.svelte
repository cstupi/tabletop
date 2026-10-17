<script lang="ts">
  import '../app.css';
  import { onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { session } from '$lib/session/session.svelte';
  import { loginHref } from '$lib/session/redirect';

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
{#if session.token}
  <header>
    <nav class="site-nav" aria-label="Main">
      <a href="/armies" aria-current={current('/armies')}>Armies</a>
      <a href="/library" aria-current={current('/library')}>Library</a>
      <a class="push" href="/account" aria-current={current('/account')}>Account: {session.account?.name ?? '…'}</a>
      <button onclick={logout}>Log out</button>
    </nav>
  </header>
{/if}
<main id="main" tabindex="-1">
  {#if isPublic || (session.ready && session.token)}{@render children()}{:else}<p role="status">Loading…</p>{/if}
</main>
