<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Guid, OfferDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import Thumb from '$lib/components/Thumb.svelte';
  import { appendPage, day, offerSummary, STATE_LABELS, stateChip } from '$lib/library/offers';

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
  <div class="empty"><Icon name="offer" size="2.5rem" /><p>No offers</p></div>
{:else}
  <ul class="cards" aria-label="Sent offers">
    {#each offers as o (o.id)}
      <li class="panel card">
        <div class="title"><Icon name="player" size="1.4rem" /><h2>{o.recipientName}</h2><span class={stateChip(o.state)}>{STATE_LABELS[o.state]}</span></div>
        <p class="facts">{offerSummary(o)}</p>
        {#if o.assets.length}
          <ul class="thumbs" aria-label="Offered models">
            {#each o.assets.slice(0, 6) as a (a.id)}<li title={a.name}><Thumb url={a.thumbnail} icon={a.kind} size="3.25rem" alt={a.name} /></li>{/each}
            {#if o.assets.length > 6}<li class="more num">+{o.assets.length - 6}</li>{/if}
          </ul>
        {/if}
        <div class="foot">
          <small class="dim">{day(o.createdAt)}{#if o.decidedAt && o.state !== 'open'}{' → '}{day(o.decidedAt)}{/if}</small>
          {#if o.state === 'open'}<span class="end"><button disabled={busy === o.id} onclick={() => withdraw(o)}>Withdraw</button></span>{/if}
        </div>
      </li>
    {/each}
  </ul>
  {#if next}<button disabled={more} onclick={loadMore}>{more ? 'Loading…' : 'More'}</button>{/if}
{/if}
<style>
  .title h2 { flex: 1 }
  .thumbs { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: var(--space-1) }
  .more { align-self: center; color: var(--dim); padding: 0 var(--space-2) }
  .dim { color: var(--dim) }
</style>
