<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, BlockedDto, FolderDto, Guid, OfferAssetDto, OfferDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import Menu from '$lib/components/Menu.svelte';
  import Thumb from '$lib/components/Thumb.svelte';
  import { folderOptions } from '$lib/library/bulk';
  import { alreadyHeld, appendPage, day, offerSummary } from '$lib/library/offers';
  import { baseLabel } from '$lib/units/bases';
  import { formatBytes } from '$lib/validation';

  const PAGE = 20;
  let offers = $state<OfferDto[]>([]); let next = $state<Guid | undefined>(); let loading = $state(true); let more = $state(false);
  let mine = $state<AssetDto[]>([]); let folders = $state<FolderDto[]>([]); let blocked = $state<BlockedDto[]>([]); let open = $state<number | null>(null);
  let into = $state<Record<Guid, Guid | ''>>({}); let busy = $state<Guid | null>(null);
  let done = $state<string | null>(null); let error = $state<string | null>(null);
  let preview = $state<{ offerId: Guid; asset: OfferAssetDto; glb?: Uint8Array; image?: string; failed?: string } | null>(null);
  const targets = $derived(folderOptions(folders));

  async function load() {
    loading = true; error = null;
    try {
      const all: AssetDto[] = [];
      for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after }); all.push(...p.items); if (!p.next) break; after = p.next; }
      const [page, f, b] = await Promise.all([api.offers.received({ limit: PAGE }), api.folders.list(), api.offers.blocked()]);
      [mine, folders, blocked, offers, next] = [all, f, b, page.items, page.next];
    } catch (e) { error = errorMessage(e); } finally { loading = false; }
  }
  async function loadMore() {
    if (!next) return;
    more = true;
    try { const p = await api.offers.received({ limit: PAGE, after: next }); offers = appendPage(offers, p.items); next = p.next; }
    catch (e) { error = errorMessage(e); } finally { more = false; }
  }
  onMount(() => { void load(); });

  const drop = (id: Guid) => { offers = offers.filter((o) => o.id !== id); if (open) open -= 1; if (preview?.offerId === id) preview = null; };
  const plural = (n: number) => `${n} model${n === 1 ? '' : 's'}`;

  async function show(o: OfferDto, a: OfferAssetDto) {
    if (preview?.asset.id === a.id && preview.offerId === o.id) { preview = null; return; }
    preview = { offerId: o.id, asset: a };
    try {
      const { url } = await api.offers.download(o.id, a.id);
      if (preview?.asset.id !== a.id) return;
      if (a.contentType === 'model/gltf-binary') {
        const r = await fetch(url);
        if (!r.ok) throw new Error(`The file could not be downloaded (${r.status}).`);
        const glb = new Uint8Array(await r.arrayBuffer());
        if (preview?.asset.id === a.id) preview = { ...preview, glb };
      } else preview = { ...preview, image: url };
    } catch (e) { if (preview?.asset.id === a.id) preview = { ...preview, failed: e instanceof Error && !('status' in e) ? e.message : errorMessage(e) }; }
  }
  async function accept(o: OfferDto) {
    busy = o.id; error = null; done = null;
    try {
      const folder = into[o.id] || null;
      const r = await api.offers.accept(o.id, folder);
      mine = [...mine, ...r.copied];
      const label = folder ? targets.find((t) => t.id === folder)?.label : null;
      done = `Added ${plural(r.copied.length)} from ${o.sharerName} to your library${label ? ` (${label})` : ''}.`;
      drop(o.id);
    } catch (e) { error = errorMessage(e); } finally { busy = null; }
  }
  async function decline(o: OfferDto, block: boolean) {
    if (block && !confirm(`Decline every offer from ${o.sharerName} and hide their offers from now on?`)) return;
    busy = o.id; error = null; done = null;
    try {
      await api.offers.decline(o.id, block);
      if (block) { const gone = offers.filter((x) => x.sharerId === o.sharerId).map((x) => x.id); gone.forEach(drop); blocked = await api.offers.blocked(); }
      else drop(o.id);
      done = block ? `Blocked ${o.sharerName}.` : 'Declined.';
    } catch (e) { error = errorMessage(e); } finally { busy = null; }
  }
  async function unblock(b: BlockedDto) {
    error = null;
    try { await api.offers.unblock(b.accountId); await load(); open = (await api.offers.count()).open; done = `Unblocked ${b.name}.`; }
    catch (e) { error = errorMessage(e); }
  }
</script>

<svelte:head><title>Offered to you · Library · Tabletop</title></svelte:head>

<h1>Library</h1>
<LibraryTabs current="offered" bind:open />
{#if done}<p class="notice" role="status">{done}</p>{/if}
{#if error}<p role="alert">{error}</p>{/if}
{#if loading}
  <p class="loading" role="status">Loading…</p>
{:else if offers.length === 0}
  <div class="empty"><Icon name="offer" size="2.5rem" /><p>No offers</p></div>
{:else}
  <ul class="offer-list" aria-label="Offers">
    {#each offers as o (o.id)}
      {@const held = alreadyHeld(o, mine)}
      <li class="panel card">
        <div class="title"><Icon name="player" size="1.4rem" /><h2>{o.sharerName}</h2><small class="dim when">{day(o.createdAt)} · {offerSummary(o)}</small></div>
        <ul class="assets" aria-label="Offered models">
          {#each o.assets as a (a.id)}
            {@const on = preview?.offerId === o.id && preview.asset.id === a.id}
            <li><button class="asset" aria-pressed={on} title="Preview {a.name}" onclick={() => show(o, a)}>
              <Thumb url={a.thumbnail} icon={a.kind} size="100%" />
              <span class="name">{a.name}</span>
              <small class="dim">{#if a.modelledBase}{baseLabel(a.modelledBase)}{' · '}{/if}{formatBytes(a.byteSize)}</small>
              {#if held.has(a.id)}<small class="held"><Icon name="ready" size="1em" />in your library</small>{/if}
            </button></li>
          {/each}
        </ul>
        {#if preview?.offerId === o.id}
          <div class="view">
            {#if preview.failed}<p role="alert">{preview.failed}</p>
            {:else if preview.glb}<MeshPreview glb={preview.glb} scale={preview.asset.scale} orientation={preview.asset.orientation} base={preview.asset.modelledBase ?? null} onerror={(m) => preview && (preview = { ...preview, glb: undefined, failed: `The preview could not show this mesh: ${m}` })} />
            {:else if preview.image}<img src={preview.image} alt={preview.asset.name} />
            {:else}<p class="loading" role="status">Downloading…</p>{/if}
          </div>
        {/if}
        <div class="foot">
          <label class="into"><Icon name="folder" /><span class="visually-hidden">Into folder</span><select bind:value={into[o.id]} disabled={busy === o.id} aria-label="Into folder"><option value="">No folder</option>{#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}</select></label>
          <button class="primary" disabled={busy !== null} onclick={() => accept(o)}><Icon name="ready" />Accept</button>
          <button disabled={busy !== null} onclick={() => decline(o, false)}>Decline</button>
          <span class="end"><Menu label="More for the offer from {o.sharerName}">
            <button class="danger" disabled={busy !== null} onclick={() => decline(o, true)}>Block {o.sharerName}</button>
          </Menu></span>
        </div>
      </li>
    {/each}
  </ul>
  {#if next}<button disabled={more} onclick={loadMore}>{more ? 'Loading…' : 'More'}</button>{/if}
{/if}
{#if blocked.length}
  <section class="panel blocked" aria-labelledby="blocked-heading">
    <h2 id="blocked-heading">Blocked</h2>
    <ul>{#each blocked as b (b.accountId)}<li>{b.name} <button class="quiet" onclick={() => unblock(b)}>Unblock</button></li>{/each}</ul>
  </section>
{/if}
<style>
  .offer-list { list-style: none; padding: 0; display: grid; gap: var(--space-3); margin: 0 0 var(--space-3) }
  .title h2 { flex: 0 1 auto }
  .when { margin-left: auto }
  .assets { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--space-2); grid-template-columns: repeat(auto-fill, minmax(8.5rem, 1fr)) }
  .asset { display: flex; flex-direction: column; align-items: stretch; gap: var(--space-1); width: 100%; height: 100%; padding: var(--space-2); white-space: normal; text-align: left; justify-content: flex-start }
  .asset > :global(.thumb) { aspect-ratio: 1; height: auto }
  .name { overflow-wrap: anywhere; font-weight: 700 }
  .held { display: inline-flex; align-items: center; gap: var(--space-1); color: var(--success) }
  .into { display: inline-flex; align-items: center; gap: var(--space-1) }
  .foot { flex-wrap: wrap }
  .view { min-height: 20rem; background: var(--view-bg); display: grid; overflow: hidden }
  .view > p { margin: auto; padding: var(--space-4) }
  .view img { max-width: 100%; max-height: 24rem; margin: auto }
  .dim { color: var(--dim) }
  .blocked ul { list-style: none; padding: 0; margin: 0 }
</style>
