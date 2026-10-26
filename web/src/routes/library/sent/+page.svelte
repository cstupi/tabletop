<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Guid, OfferDto } from '$lib/api/types';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import { appendPage, day, offerSummary, STATE_LABELS } from '$lib/library/offers';

  const PAGE = 50;
  let offers = $state<OfferDto[]>([]); let next = $state<Guid | undefined>(); let loading = $state(true); let more = $state(false);
  let busy = $state<Guid | null>(null); let error = $state<string | null>(null);

  async function load() {
    try { const p = await api.offers.sent({ limit: PAGE }); [offers, next] = [p.items, p.next]; }
    catch (e) { error = errorMessage(e); } finally { loading = false; }
  }
  async function loadMore() {
    if (!next) return;
    more = true;
    try { const p = await api.offers.sent({ limit: PAGE, after: next }); offers = appendPage(offers, p.items); next = p.next; }
    catch (e) { error = errorMessage(e); } finally { more = false; }
  }
  onMount(() => { void load(); });

  async function withdraw(o: OfferDto) {
    busy = o.id; error = null;
    try {
      await api.offers.withdraw(o.id);
      offers = offers.map((x) => (x.id === o.id ? { ...x, state: 'withdrawn', decidedAt: new Date().toISOString() } : x));
    } catch (e) { error = errorMessage(e); } finally { busy = null; }
  }
</script>

<svelte:head><title>Sent · Library · Tabletop</title></svelte:head>

<h1>Library</h1>
<LibraryTabs current="sent" />
{#if error}<p role="alert">{error}</p>{/if}
{#if loading}
  <p class="loading" role="status">Loading…</p>
{:else if offers.length === 0}
  <div class="empty"><p>You have not offered anything. Select models or a folder in your library and choose Offer.</p></div>
{:else}
  <div class="panel scroll-x">
    <table>
      <thead><tr><th scope="col">To</th><th scope="col">What</th><th scope="col">Sent</th><th scope="col">State</th><th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
      <tbody>
        {#each offers as o (o.id)}
          <tr>
            <td>{o.recipientName}</td>
            <td>{offerSummary(o)}</td>
            <td>{day(o.createdAt)}</td>
            <td class:open={o.state === 'open'}>{STATE_LABELS[o.state]}{#if o.decidedAt && o.state !== 'open'}{' '}<small class="dim">{day(o.decidedAt)}</small>{/if}</td>
            <td>{#if o.state === 'open'}<button class="quiet" disabled={busy === o.id} onclick={() => withdraw(o)}>Withdraw</button>{/if}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
  {#if next}<button disabled={more} onclick={loadMore}>{more ? 'Loading…' : 'More'}</button>{/if}
  <p><small class="dim">Copies a player accepted stay theirs if you withdraw or delete your own.</small></p>
{/if}
<style>
  table { width: 100%; border-collapse: collapse }
  th, td { text-align: left; padding: var(--s2) var(--s3); border-bottom: 1px solid var(--rule); overflow-wrap: anywhere }
  .open { color: var(--accent) }
  .dim { color: var(--dim) }
</style>
