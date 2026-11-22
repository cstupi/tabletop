<script lang="ts">
  // The Library's views: the account's own assets, offers made to it (with their count), offers it made, and players' Shared folders.
  import { api } from '$lib/api/client';
  let { current, open = $bindable(null) }: { current: 'mine' | 'offered' | 'sent' | 'shared'; open?: number | null } = $props();
  $effect(() => { api.offers.count().then((c) => (open = c.open)).catch(() => {}); });
</script>

<nav class="tabs" aria-label="Library views">
  <a href="/library" aria-current={current === 'mine' ? 'page' : undefined}>My library</a>
  <a href="/library/offered" aria-current={current === 'offered' ? 'page' : undefined}>Offered to you{#if open}{' '}<span class="badge" aria-label="{open} open">{open}</span>{/if}</a>
  <a href="/library/sent" aria-current={current === 'sent' ? 'page' : undefined}>Sent</a>
  <a href="/library/shared" aria-current={current === 'shared' ? 'page' : undefined}>Shared by players</a>
</nav>
<style>
  .tabs { display: flex; gap: var(--space-3); margin: 0 0 var(--space-3); border-bottom: 1px solid var(--rule); flex-wrap: wrap }
  .tabs a { padding: var(--space-2) 0; color: var(--dim); text-decoration: none; border-bottom: 2px solid transparent; margin-bottom: -1px }
  .tabs a:hover { color: var(--text) }
  .tabs a[aria-current='page'] { color: var(--text); border-bottom-color: var(--accent) }
  .badge { display: inline-block; min-width: 1.4em; padding: var(--space-2); border-radius: 0.7em; background: var(--accent); color: var(--panel-solid); font-size: 0.8em; text-align: center; line-height: 1.4em }
</style>
