<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, Base, FolderDto } from '$lib/api/types';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import VolumeEditor from '$lib/components/VolumeEditor.svelte';
  import ScaleOrientation from '$lib/components/ScaleOrientation.svelte';
  import { describeGlb } from '$lib/mesh/convert';
  import type { Bounds } from '$lib/mesh/types';
  import Icon from '$lib/components/Icon.svelte';
  import { formatBytes, MAX_NAME, nameProblem } from '$lib/validation';

  const id = $derived(page.params.id!);
  let asset = $state<AssetDto | null>(null); let folders = $state<FolderDto[]>([]); let glb = $state<Uint8Array | null>(null); let error = $state<string | null>(null); let bounds = $state<Bounds | null>(null);
  let name = $state(''); let scale = $state(1); let orientation = $state({ pitch: 0, yaw: 0, roll: 0 }); let base = $state<Base | null>(null);   // never undefined: MeshPreview and BasePicker take Base | null
  let loadFailed = $state(false); let downloading = $state(false); let busy = $state(false); let saved = $state(false);
  let loadToken = 0;

  async function load() {
    glb = null; bounds = null; error = null; loadFailed = false;
    const t = ++loadToken;
    try {
      const a = await api.assets.get(id); if (t !== loadToken) return;
      folders = await api.folders.list(); if (t !== loadToken) return;
      // the fields before the asset: BasePicker reads its value once, when the form first renders
      ({ name, scale, orientation } = a); base = a.modelledBase ?? null; asset = a;
      if (asset.uploaded && asset.contentType === 'model/gltf-binary') {
        downloading = true;
        const response = await fetch((await api.assets.download(id)).url); if (t !== loadToken) return;
        if (!response.ok) { error = `The mesh could not be downloaded (${response.status}). Reload to try again.`; return; }
        const bytes = new Uint8Array(await response.arrayBuffer()); if (t !== loadToken) return;
        glb = bytes; bounds = (await describeGlb(bytes)).bounds;
      }
    } catch (e) { if (t === loadToken) { error = errorMessage(e); if (!asset) loadFailed = true; } }
    finally { if (t === loadToken) downloading = false; }
  }
  $effect(() => { void load(); });
  const nameError = $derived(nameProblem(name));
  const numbersOk = $derived(asset?.kind === 'texture' || ([scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite) && scale > 0));
  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (nameError || !numbersOk) return;
    busy = true; error = null; saved = false;
    try { asset = await api.assets.patch(id, { name: name.trim(), scale, orientation, modelledBase: asset?.kind === 'miniature' && base ? base : undefined }); name = asset.name; saved = true; }
    catch (err) { error = errorMessage(err); } finally { busy = false; }
  }
  const toggleFolder = (f: FolderDto, on: boolean) => (on ? api.folders.addAsset(f.id, id) : api.folders.removeAsset(f.id, id)).then(async () => { folders = await api.folders.list(); }).catch((e) => (error = errorMessage(e)));
  const remove = async () => {
    if (!confirm(`Delete "${asset?.name}"? Armies pointing at it keep playing and draw the slot's hitbox instead.`)) return;
    busy = true;
    try { await api.assets.delete(id); await goto('/library'); } catch (e) { error = errorMessage(e); } finally { busy = false; }
  };
</script>

<svelte:head><title>{asset ? `${asset.name} · Library` : 'Asset'} · Tabletop</title></svelte:head>

<p><a class="back" href="/library"><Icon name="back" />Library</a></p>
{#if asset}
  <h1>{asset.name}</h1>
  <div class="layout">
    <div class="panel view">
      {#if glb}<MeshPreview {glb} {scale} {orientation} {base} onerror={(m) => (error = `The preview could not show this mesh: ${m}`)} />
      {:else if downloading}<p class="loading" role="status">Downloading the mesh…</p>
      {:else}<p class="placeholder">{asset.kind === 'texture' ? 'A board texture.' : error ? '' : 'No preview: the upload never finished. Delete this asset and upload the file again.'}</p>{/if}
    </div>
    <aside class="panel">
      <form class="stack" onsubmit={save}>
        <label>Name <input bind:value={name} maxlength={MAX_NAME} aria-invalid={!!nameError} aria-describedby={nameError ? 'name-error' : undefined} oninput={() => (saved = false)} /></label>
        {#if nameError}<p id="name-error"><small>{nameError}</small></p>{/if}
        {#if asset.kind !== 'texture'}
          <ScaleOrientation bind:scale bind:orientation {bounds} checkUnit gltf disabled={busy} />
        {/if}
        {#if asset.kind === 'miniature'}<label>Modelled base <BasePicker bind:value={base} label="Modelled base" /></label>{/if}
        {#if !numbersOk}<p role="alert">Scale must be a number above zero, and the orientation numbers.</p>{/if}
        <div class="actions">
          <button type="submit" class="primary" disabled={busy || !numbersOk || !!nameError}>Save</button>
          <span class="saved" role="status">{#if saved}Saved.{/if}</span>
        </div>
      </form>
      <h2>Folders</h2>
      {#each folders as f (f.id)}<label class="check"><input type="checkbox" checked={f.assetIds.includes(id)} onchange={(e) => void toggleFolder(f, e.currentTarget.checked)} /> {f.name}</label>{:else}<p><small>No folders yet. Make one in the <a href="/library">Library</a>.</small></p>{/each}
      <p class="meta"><small>{asset.kind} · {formatBytes(asset.byteSize)} · <span class="num" title={asset.hash}>{asset.hash.slice(0, 12)}…</span></small></p>
      <button class="danger" onclick={remove} disabled={busy}>Delete asset</button>
    </aside>
  </div>
  {#if asset.kind === 'terrain' && glb}<VolumeEditor assetId={id} {glb} {scale} {orientation} />{/if}
{:else if !loadFailed}
  <p class="loading" role="status">Loading…</p>
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>
  .layout { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: var(--s5); align-items: start }
  @media (max-width: 48rem) { .layout { grid-template-columns: minmax(0, 1fr); gap: 0 } }
  .view { min-height: 24rem; padding: 0; background: var(--view-bg); display: grid; overflow: hidden }
  .view > p { margin: auto; padding: var(--s5); text-align: center }
  :global([data-theme='fantasy']) .view { background: var(--view-bg) }
  .check { display: flex; align-items: center; gap: var(--s1); margin: var(--s1) 0 }
  aside h2 { margin-top: var(--s5) }
  .meta { margin: var(--s4) 0 var(--s3); overflow-wrap: anywhere }
</style>
