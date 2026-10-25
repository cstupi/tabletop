<script lang="ts">
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, FolderDto, Guid, SharedAssetDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import { folderOptions } from '$lib/library/bulk';
  import { baseLabel } from '$lib/units/bases';
  import { formatBytes } from '$lib/validation';

  const id = $derived(page.params.id!);
  let item = $state<SharedAssetDto | null>(null); let glb = $state<Uint8Array | null>(null); let folders = $state<FolderDto[]>([]);
  let into = $state<Guid | ''>(''); let copied = $state<AssetDto | null>(null);
  let loadFailed = $state(false); let downloading = $state(false); let busy = $state(false); let error = $state<string | null>(null);
  const targets = $derived(folderOptions(folders));
  let loadToken = 0;

  async function load() {
    glb = null; error = null; loadFailed = false; copied = null;
    const t = ++loadToken;
    try {
      const a = await api.shared.get(id); if (t !== loadToken) return;
      item = a;
      folders = await api.folders.list(); if (t !== loadToken) return;
      if (a.contentType === 'model/gltf-binary') {
        downloading = true;
        const response = await fetch((await api.shared.download(id)).url); if (t !== loadToken) return;
        if (!response.ok) { error = `The mesh could not be downloaded (${response.status}). Reload to try again.`; return; }
        glb = new Uint8Array(await response.arrayBuffer());
      }
    } catch (e) { if (t === loadToken) { error = errorMessage(e); if (!item) loadFailed = true; } }
    finally { if (t === loadToken) downloading = false; }
  }
  $effect(() => { void load(); });

  async function copy() {
    busy = true; error = null;
    try {
      const r = await api.shared.copy([id], into || null);
      if (r.copied.length) copied = r.copied[0]!; else error = 'This model is no longer shared.';
    } catch (e) { error = errorMessage(e); } finally { busy = false; }
  }
</script>

<svelte:head><title>{item ? `${item.name} · Shared` : 'Shared model'} · Tabletop</title></svelte:head>

<p><a class="back" href="/library/shared"><Icon name="back" />Shared</a></p>
{#if item}
  <h1>{item.name}</h1>
  <div class="layout">
    <div class="panel view">
      {#if glb}<MeshPreview {glb} scale={item.scale} orientation={item.orientation} base={item.modelledBase ?? null} onerror={(m) => (error = `The preview could not show this mesh: ${m}`)} />
      {:else if downloading}<p class="loading" role="status">Downloading the mesh…</p>
      {:else}<p class="placeholder">{item.kind === 'texture' ? 'A board texture.' : ''}</p>{/if}
    </div>
    <aside class="panel stack">
      <p>Shared by <strong>{item.ownerName}</strong></p>
      <p class="meta"><small>{item.kind}{#if item.modelledBase}{' · '}{baseLabel(item.modelledBase)}{/if} · {formatBytes(item.byteSize)}</small></p>
      {#if item.folders.length}<p class="meta"><small>In {item.folders.join(', ')}</small></p>{/if}
      <label>Into folder <select bind:value={into} disabled={busy}><option value="">No folder</option>{#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}</select></label>
      <div class="actions"><button class="primary" disabled={busy} onclick={copy}><Icon name="plus" />Copy to library</button></div>
      {#if copied}<p class="notice" role="status">Copied. <a href="/library/{copied.id}">Open your copy</a></p>{/if}
    </aside>
  </div>
{:else if !loadFailed}
  <p class="loading" role="status">Loading…</p>
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>
  .layout { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: var(--s5); align-items: start }
  @media (max-width: 48rem) { .layout { grid-template-columns: minmax(0, 1fr); gap: 0 } }
  .view { min-height: 24rem; padding: 0; background: var(--view-bg); display: grid; overflow: hidden }
  .view > p { margin: auto; padding: var(--s5); text-align: center }
  .meta { margin: 0; overflow-wrap: anywhere }
  aside label { display: inline-flex; align-items: center; gap: var(--s2); flex-wrap: wrap }
</style>
