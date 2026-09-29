<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import type { AssetDto, AssetKind, Base, FolderDto } from '$lib/api/types';
  import FolderTree from '$lib/components/FolderTree.svelte';
  import UploadDialog from '$lib/components/UploadDialog.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import { filterAssets } from '$lib/library/folders';
  import { baseLabel } from '$lib/units/bases';

  let folders = $state<FolderDto[]>([]); let assets = $state<AssetDto[]>([]); let loading = $state(true);
  let kind = $state<AssetKind | ''>(''); let base = $state<Base | null>(null); let uploading = $state(false); let error = $state<string | null>(null);
  const selected = $derived(page.url.searchParams.get('folder'));
  const shown = $derived(filterAssets(assets, folders, { folderId: selected, kind: kind || undefined, base: base ?? undefined }));

  async function loadAll() {
    loading = true;
    try {
      const all: AssetDto[] = [];
      for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after }); all.push(...p.items); if (!p.next) break; after = p.next; }
      [assets, folders] = [all, await api.folders.list()];
    } catch (e) { error = e instanceof Error ? e.message : String(e); } finally { loading = false; }
  }
  $effect(() => { void loadAll(); });
  const act = (fn: () => Promise<unknown>) => fn().then(loadAll).catch((e) => (error = e instanceof Error ? e.message : String(e)));
</script>

<h1>Library</h1>
<div class="layout">
  <FolderTree {folders} selected={selected} onselect={(id) => goto(id ? `/library?folder=${id}` : '/library')}
    oncreate={(p, name) => act(() => api.folders.create({ name, parentId: p }))} onrename={(id, name) => act(() => api.folders.patch(id, { name, parentId: folders.find((f) => f.id === id)?.parentId ?? null }))}
    onmove={(id, parentId) => act(() => api.folders.patch(id, { name: folders.find((f) => f.id === id)!.name, parentId }))} ondelete={(id) => act(() => api.folders.delete(id))} />
  <section>
    <div class="bar">
      <button onclick={() => (uploading = true)}>Add STL / GLB / texture</button>
      <select bind:value={kind}><option value="">Any kind</option><option value="miniature">Miniatures</option><option value="terrain">Terrain</option><option value="texture">Textures</option></select>
      <label>Made for base <BasePicker bind:value={base} allowNone /></label>
      <span>{shown.length} of {assets.length}</span>
    </div>
    {#if loading}<p>Loading…</p>{/if}
    <ul class="grid">
      {#each shown as a (a.id)}
        <li><a href="/library/{a.id}"><strong>{a.name}</strong><br /><small>{a.kind}{#if a.modelledBase} · {baseLabel(a.modelledBase)}{/if}{#if !a.uploaded} · upload incomplete{/if}</small></a></li>
      {/each}
    </ul>
  </section>
</div>
<UploadDialog open={uploading} onclose={() => (uploading = false)} onuploaded={() => void loadAll()} />
{#if error}<p role="alert">{error}</p>{/if}
<style>.layout { display: grid; grid-template-columns: 16rem 1fr; gap: 1rem } .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr)); gap: .5rem; list-style: none; padding: 0 } .bar { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap }</style>
