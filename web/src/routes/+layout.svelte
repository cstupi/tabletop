<script lang="ts">
  import '../app.css';
  import { onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { session } from '$lib/session/session.svelte';

  let { children } = $props();
  const publicRoutes = ['/login', '/reset-password'];
  const isPublic = $derived(publicRoutes.some((p) => page.url.pathname.startsWith(p)));

  onMount(() => { void session.restore(); return session.startRefreshLoop(); });

  $effect(() => {
    if (!session.ready) return;
    if (!session.token && !isPublic) void goto(`/login`);
  });

  async function logout() { await session.logout(); await goto('/login'); }
</script>

{#if session.token}
  <header>
    <nav>
      <a href="/armies" aria-current={page.url.pathname.startsWith('/armies') ? 'page' : undefined}>Armies</a>
      <a href="/library" aria-current={page.url.pathname.startsWith('/library') ? 'page' : undefined}>Library</a>
      <a href="/account">{session.account?.name ?? 'Account'}</a>
      <button onclick={logout}>Log out</button>
    </nav>
  </header>
{/if}
<main>
  {#if isPublic || (session.ready && session.token)}{@render children()}{:else}<p>Loading…</p>{/if}
</main>
