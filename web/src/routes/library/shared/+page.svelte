<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, AssetKind, FolderDto, Guid, SharedAssetDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import { folderOptions } from '$lib/library/bulk';
  import { clear, click, keepOnly, NONE, selectAll, type Selection } from '$lib/library/selection';
  import { alreadyHeld, appendPage } from '$lib/library/shared';
  import { baseLabel } from '$lib/units/bases';

  const PAGE = 60;
  let items = $state<SharedAssetDto[]>([]); let next = $state<Guid | undefined>(); let loading = $state(true); let more = $state(false);
  let q = $state(''); let kind = $state<AssetKind | ''>(''); let applied = $state({ q: '', kind: '' as AssetKind | '' });
  let mine = $state<AssetDto[]>([]); let folders = $state<FolderDto[]>([]); let into = $state<Guid | ''>('');
  let selection = $state<Selection>(NONE); let busy = $state(false); let done = $state<string | null>(null); let error = $state<string | null>(null);
  const order = $derived(items.map((i) => i.id));
  const count = $derived(selection.ids.size);
  const held = $derived(alreadyHeld(items, mine));
  const targets = $derived(folderOptions(folders));
  $effect(() => { const n = keepOnly(selection, order); if (n !== selection) selection = n; });

  async function loadMine() {
    try {
      const all: AssetDto[] = [];
      for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after }); all.push(...p.items); if (!p.next) break; after = p.next; }
      [mine, folders] = [all, await api.folders.list()];
    } catch (e) { error = errorMessage(e); }
  }
  let token = 0;
  async function search() {
    const t = ++token; loading = true; error = null;
    const wanted = { q: q.trim(), kind };
    applied = wanted;
    try {
      const p = await api.shared.list({ limit: PAGE, q: wanted.q || undefined, kind: wanted.kind || undefined }); if (t !== token) return;
      items = p.items; next = p.next;
    } catch (e) { if (t === token) error = errorMessage(e); } finally { if (t === token) loading = false; }
  }
  async function loadMore() {
    if (!next) return;
    more = true;
    try { const p = await api.shared.list({ limit: PAGE, after: next, q: applied.q || undefined, kind: applied.kind || undefined }); items = appendPage(items, p.items); next = p.next; }
    catch (e) { error = errorMessage(e); } finally { more = false; }
  }
  // Once, on mount: neither load is tracked, so typing in the search box does not reload until Search or Enter.
  onMount(() => { void loadMine(); void search(); });

  const plural = (n: number) => `${n} model${n === 1 ? '' : 's'}`;
  async function copy() {
    const ids = [...selection.ids];
    if (ids.length === 0) return;
    busy = true; error = null; done = null;
    try {
      const r = await api.shared.copy(ids, into || null);
      mine = [...mine, ...r.copied];
      const label = into ? targets.find((t) => t.id === into)?.label : null;
      done = `Copied ${plural(r.copied.length)} to your library${label ? ` (${label})` : ''}.`;
      if (r.notFound.length) error = `${plural(r.notFound.length)} no longer shared.`;
      selection = clear();
    } catch (e) { error = errorMessage(e); } finally { busy = false; }
  }
</script>

<svelte:head><title>Shared models · Library · Tabletop</title></svelte:head>

<h1>Library</h1>
<LibraryTabs current="shared" />
<section class="panel" aria-label="Shared models">
  <form class="bar" onsubmit={(e) => { e.preventDefault(); void search(); }}>
    <label>Search <input type="search" bind:value={q} maxlength="64" placeholder="Name, player or folder" /></label>
    <label>Kind <select bind:value={kind} onchange={() => void search()}><option value="">Any kind</option><option value="miniature">Miniatures</option><option value="terrain">Terrain</option><option value="texture">Textures</option></select></label>
    <button type="submit">Search</button>
  </form>
  {#if items.length > 0}
    <div class="selbar" role="toolbar" aria-label="Selection">
      {#if count > 0}<strong role="status">{count} selected</strong>{:else}<button class="quiet" onclick={() => (selection = selectAll(selection, order))}>Select all {items.length}</button>{/if}
      <label>Into folder <select bind:value={into} disabled={busy}><option value="">No folder</option>{#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}</select></label>
      <button class="primary" disabled={busy || count === 0} onclick={copy}><Icon name="plus" />Copy to library</button>
      {#if count > 0}<button class="quiet" disabled={busy} onclick={() => (selection = clear())}>Clear</button>{/if}
    </div>
  {/if}
  {#if done}<p class="notice" role="status">{done}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if loading}
    <p class="loading" role="status">Loading…</p>
  {:else if items.length === 0}
    <div class="empty"><p>{applied.q || applied.kind ? 'No shared models match.' : 'No one has shared a model yet.'}</p></div>
  {:else}
    <ul class="grid" aria-label="Shared models">
      {#each items as a (a.id)}
        {@const on = selection.ids.has(a.id)}
        <li class="cell" class:on>
          <input type="checkbox" class="check" checked={on} aria-label="Select {a.name}" disabled={busy}
            onclick={(e) => { selection = click(selection, order, a.id, { range: e.shiftKey }); e.currentTarget.checked = selection.ids.has(a.id); }} />
          <a class="tile" href="/library/shared/{a.id}">
            <Icon name={a.kind} size="1.75rem" />
            <span><strong>{a.name}</strong><br /><small>{a.kind}{#if a.modelledBase}{' · '}{baseLabel(a.modelledBase)}{/if}{' · '}by {a.ownerName}</small>
              {#if a.folders.length}<br /><small class="dim">{a.folders.join(', ')}</small>{/if}
              {#if held.has(a.id)}<br /><small class="dim">In your library</small>{/if}</span>
          </a>
        </li>
      {/each}
    </ul>
    {#if next}<button disabled={more} onclick={loadMore}>{more ? 'Loading…' : 'More'}</button>{/if}
  {/if}
</section>
<style>
  .bar { display: flex; gap: var(--s2) var(--s3); align-items: center; flex-wrap: wrap; padding-bottom: var(--s3); border-bottom: 1px solid var(--rule) }
  .bar label, .selbar label { display: inline-flex; align-items: center; gap: var(--s2); flex-wrap: wrap }
  .selbar { display: flex; gap: var(--s2) var(--s3); align-items: center; flex-wrap: wrap; padding: var(--s2) 0; min-height: 2.75rem }
  .grid { list-style: none; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: var(--s3); margin: var(--s4) 0 }
  .cell { position: relative }
  .check { position: absolute; top: var(--s2); right: var(--s2); z-index: 1; width: 1.35rem; height: 1.35rem; margin: 0; cursor: pointer }
  .tile { display: flex; gap: var(--s3); align-items: flex-start; height: 100%; padding: var(--s3); padding-right: calc(var(--s3) + 1.6rem); color: var(--text); text-decoration: none; border: 1px solid var(--rule); background: var(--panel-solid); overflow-wrap: anywhere }
  .tile :global(.icon) { color: var(--accent) }
  .tile:hover { border-color: var(--accent); color: var(--text) }
  .on .tile { border-color: var(--accent); box-shadow: inset 0 0 0 2px var(--accent); background: var(--rule) }
  .dim { color: var(--dim) }
  :global([data-theme='fantasy']) .tile { border-radius: 4px; background: #F1E6CA; border-color: #4A2F1B55 }
  :global([data-theme='fantasy']) .on .tile { border-color: var(--wood); box-shadow: inset 0 0 0 2px var(--wood); background: #E6D3A8 }
  :global([data-theme='scifi']) .tile { clip-path: polygon(8px 0, 100% 0, 100% calc(100% - 8px), calc(100% - 8px) 100%, 0 100%, 0 8px) }
</style>
