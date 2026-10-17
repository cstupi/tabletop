<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, AssetKind, Base, FolderDto } from '$lib/api/types';
  import FolderTree from '$lib/components/FolderTree.svelte';
  import UploadDialog from '$lib/components/UploadDialog.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import { filterAssets } from '$lib/library/folders';
  import { baseLabel } from '$lib/units/bases';
  import { ASSET_QUOTA, formatBytes, libraryUsage } from '$lib/validation';

  let folders = $state<FolderDto[]>([]); let assets = $state<AssetDto[]>([]); let loading = $state(true);
  let kind = $state<AssetKind | ''>(''); let base = $state<Base | null>(null); let uploading = $state(false); let error = $state<string | null>(null);
  let addButton = $state<HTMLButtonElement>();
  const selected = $derived(page.url.searchParams.get('folder'));
  const folderName = $derived(selected ? folders.find((f) => f.id === selected)?.name : undefined);
  const shown = $derived(filterAssets(assets, folders, { folderId: selected, kind: kind || undefined, base: base ?? undefined }));
  const usage = $derived(libraryUsage(assets));
  const nearQuota = $derived(usage.assets >= ASSET_QUOTA.assets * 0.9 || usage.bytes >= ASSET_QUOTA.bytes * 0.9);

  async function loadAll() {
    loading = true;
    try {
      const all: AssetDto[] = [];
      for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after }); all.push(...p.items); if (!p.next) break; after = p.next; }
      [assets, folders] = [all, await api.folders.list()];
    } catch (e) { error = errorMessage(e); } finally { loading = false; }
  }
  $effect(() => { void loadAll(); });
  const actFolder = (fn: () => Promise<unknown>) => fn().then(async () => { folders = await api.folders.list(); error = null; }).catch((e) => (error = errorMessage(e)));
  function closeUpload() { uploading = false; addButton?.focus(); }
</script>

<svelte:head><title>Library · Tabletop</title></svelte:head>

<h1>Library{#if folderName} <small>· {folderName}</small>{/if}</h1>
<div class="layout">
  <FolderTree {folders} selected={selected} onselect={(id) => goto(id ? `/library?folder=${id}` : '/library')}
    oncreate={(p, name) => actFolder(() => api.folders.create({ name, parentId: p }))} onrename={(id, name) => actFolder(() => api.folders.patch(id, { name, parentId: folders.find((f) => f.id === id)?.parentId ?? null }))}
    onmove={(id, parentId) => actFolder(() => api.folders.patch(id, { name: folders.find((f) => f.id === id)!.name, parentId }))} ondelete={(id) => actFolder(() => api.folders.delete(id))}
    oninvalid={(m) => (error = m)} />
  <section aria-label="Assets">
    <div class="bar">
      <button bind:this={addButton} onclick={() => (uploading = true)}>Add STL / GLB / texture</button>
      <label>Kind <select bind:value={kind}><option value="">Any kind</option><option value="miniature">Miniatures</option><option value="terrain">Terrain</option><option value="texture">Textures</option></select></label>
      <label>Made for base <BasePicker bind:value={base} allowNone label="Made for base" /></label>
      <span role="status">{#if !loading}Showing {shown.length} of {assets.length}{/if}</span>
    </div>
    {#if error}<p role="alert">{error}</p>{/if}
    {#if loading}
      <p role="status">Loading…</p>
    {:else if assets.length === 0}
      <p>Your library is empty. Add an STL (or several parts of one kit), a GLB, or a board texture to get started.</p>
    {:else if shown.length === 0}
      <p>Nothing here matches. {#if selected}This folder and its sub-folders hold no matching assets; add assets to it from an asset's page.{:else}Change the kind or base filter.{/if}</p>
    {:else}
      <ul class="grid">
        {#each shown as a (a.id)}
          <li><a href="/library/{a.id}"><strong>{a.name}</strong><br /><small>{a.kind}{#if a.modelledBase} · {baseLabel(a.modelledBase)}{/if}{#if !a.uploaded} · upload incomplete{/if}</small></a></li>
        {/each}
      </ul>
    {/if}
    {#if !loading}
      <p><small>Using {usage.assets} of {ASSET_QUOTA.assets} assets and {formatBytes(usage.bytes)} of {formatBytes(ASSET_QUOTA.bytes)}.{#if nearQuota} <strong>Nearly full: delete assets you no longer use to make room.</strong>{/if}</small></p>
    {/if}
  </section>
</div>
<UploadDialog open={uploading} onclose={closeUpload} onuploaded={() => void loadAll()} />
<style>
  .layout { display: grid; grid-template-columns: 16rem minmax(0, 1fr); gap: 1rem }
  @media (max-width: 40rem) { .layout { grid-template-columns: minmax(0, 1fr) } }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr)); gap: .5rem; list-style: none; padding: 0 }
  .grid a { display: block; padding: .25rem }
  .bar { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap }
</style>
