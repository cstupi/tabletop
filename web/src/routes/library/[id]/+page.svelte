<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, Base, FolderDto } from '$lib/api/types';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import VolumeEditor from '$lib/components/VolumeEditor.svelte';
  import { formatBytes, MAX_NAME, nameProblem } from '$lib/validation';

  const id = $derived(page.params.id!);
  let asset = $state<AssetDto | null>(null); let folders = $state<FolderDto[]>([]); let glb = $state<Uint8Array | null>(null); let error = $state<string | null>(null);
  let name = $state(''); let scale = $state(1); let orientation = $state({ pitch: 0, yaw: 0, roll: 0 }); let base = $state<Base | null>(null);   // never undefined: MeshPreview and BasePicker take Base | null
  let loadFailed = $state(false); let downloading = $state(false); let busy = $state(false); let saved = $state(false);
  let loadToken = 0;

  async function load() {
    glb = null; error = null; loadFailed = false;
    const t = ++loadToken;
    try {
      asset = await api.assets.get(id); if (t !== loadToken) return;
      folders = await api.folders.list(); if (t !== loadToken) return;
      ({ name, scale, orientation } = asset); base = asset.modelledBase ?? null;
      if (asset.uploaded && asset.contentType === 'model/gltf-binary') {
        downloading = true;
        const response = await fetch((await api.assets.download(id)).url); if (t !== loadToken) return;
        if (!response.ok) { error = `The mesh could not be downloaded (${response.status}). Reload to try again.`; return; }
        glb = new Uint8Array(await response.arrayBuffer()); if (t !== loadToken) return;
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

<p><a href="/library">← Library</a></p>
{#if asset}
  <h1>{asset.name}</h1>
  <div class="layout">
    <div class="view">
      {#if glb}<MeshPreview {glb} {scale} {orientation} {base} onerror={(m) => (error = `The preview could not show this mesh: ${m}`)} />
      {:else if downloading}<p role="status">Downloading the mesh…</p>
      {:else}<p>{asset.kind === 'texture' ? 'A board texture.' : error ? '' : 'No preview: the upload never finished. Delete this asset and upload the file again.'}</p>{/if}
    </div>
    <aside>
      <form class="stack" onsubmit={save}>
        <label>Name <input bind:value={name} maxlength={MAX_NAME} aria-invalid={!!nameError} aria-describedby={nameError ? 'name-error' : undefined} oninput={() => (saved = false)} /></label>
        {#if nameError}<p id="name-error"><small>{nameError}</small></p>{/if}
        {#if asset.kind !== 'texture'}
          <label>Scale (inches per model unit) <input type="number" step="any" min="0" bind:value={scale} /></label>
          <fieldset><legend>Orientation (degrees)</legend>
            <label>Pitch <input type="number" step="15" bind:value={orientation.pitch} /></label><label>Roll <input type="number" step="15" bind:value={orientation.roll} /></label><label>Yaw (facing) <input type="number" step="15" bind:value={orientation.yaw} /></label>
          </fieldset>
        {/if}
        {#if asset.kind === 'miniature'}<label>Modelled base <BasePicker bind:value={base} label="Modelled base" /></label>{/if}
        {#if !numbersOk}<p role="alert">Scale must be a number above zero, and the orientation numbers.</p>{/if}
        <div class="actions">
          <button type="submit" disabled={busy || !numbersOk || !!nameError}>Save</button>
          <span role="status">{#if saved}Saved.{/if}</span>
        </div>
      </form>
      <h2>Folders</h2>
      {#each folders as f (f.id)}<label class="check"><input type="checkbox" checked={f.assetIds.includes(id)} onchange={(e) => void toggleFolder(f, e.currentTarget.checked)} /> {f.name}</label>{:else}<p><small>No folders yet. Make one in the <a href="/library">Library</a>.</small></p>{/each}
      <p><small>{asset.kind} · {formatBytes(asset.byteSize)} · <span title={asset.hash}>{asset.hash.slice(0, 12)}…</span></small></p>
      <button onclick={remove} disabled={busy}>Delete asset</button>
    </aside>
  </div>
  {#if asset.kind === 'terrain' && glb}<VolumeEditor assetId={id} {glb} {scale} {orientation} />{/if}
{:else if !loadFailed}
  <p role="status">Loading…</p>
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>
  .layout { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: 1rem }
  @media (max-width: 48rem) { .layout { grid-template-columns: minmax(0, 1fr) } }
  .view { min-height: 24rem } .check { display: block }
</style>
