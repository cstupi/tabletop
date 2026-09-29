<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import type { AssetDto, Base, FolderDto } from '$lib/api/types';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import VolumeEditor from '$lib/components/VolumeEditor.svelte';

  const id = $derived(page.params.id!);
  let asset = $state<AssetDto | null>(null); let folders = $state<FolderDto[]>([]); let glb = $state<Uint8Array | null>(null); let error = $state<string | null>(null);
  let name = $state(''); let scale = $state(1); let orientation = $state({ pitch: 0, yaw: 0, roll: 0 }); let base = $state<Base | null>(null);   // never undefined: MeshPreview and BasePicker take Base | null
  let loadToken = 0;

  async function load() {
    glb = null; error = null;
    const t = ++loadToken;
    try {
      asset = await api.assets.get(id); if (t !== loadToken) return;
      folders = await api.folders.list(); if (t !== loadToken) return;
      ({ name, scale, orientation } = asset); base = asset.modelledBase ?? null;
      if (asset.uploaded && asset.contentType === 'model/gltf-binary') {
        const response = await fetch((await api.assets.download(id)).url); if (t !== loadToken) return;
        if (!response.ok) { error = `${response.status}: could not download the mesh`; return; }
        glb = new Uint8Array(await response.arrayBuffer()); if (t !== loadToken) return;
      }
    } catch (e) { error = e instanceof Error ? e.message : String(e); }
  }
  $effect(() => { void load(); });
  const numbersOk = $derived(asset?.kind === 'texture' || [scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite));
  const save = () => api.assets.patch(id, { name, scale, orientation, modelledBase: asset?.kind === 'miniature' && base ? base : undefined }).then((a) => (asset = a)).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const toggleFolder = (f: FolderDto, on: boolean) => (on ? api.folders.addAsset(f.id, id) : api.folders.removeAsset(f.id, id)).then(async () => { folders = await api.folders.list(); }).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const remove = async () => { if (confirm('Delete this asset? Armies pointing at it keep playing and draw the slot\'s hitbox instead.')) { try { await api.assets.delete(id); await goto('/library'); } catch (e) { error = e instanceof Error ? e.message : String(e); } } };
</script>

{#if asset}
  <h1>{asset.name}</h1>
  <div class="layout">
    <div class="view">{#if glb}<MeshPreview {glb} {scale} {orientation} {base} onerror={(m) => (error = m)} />{:else}<p>{asset.kind === 'texture' ? 'A board texture.' : 'No preview: the upload never completed.'}</p>{/if}</div>
    <aside>
      <label>Name <input bind:value={name} /></label>
      {#if asset.kind !== 'texture'}
        <label>Scale <input type="number" step="any" bind:value={scale} /></label>
        <label>Pitch <input type="number" step="15" bind:value={orientation.pitch} /></label><label>Roll <input type="number" step="15" bind:value={orientation.roll} /></label><label>Yaw <input type="number" step="15" bind:value={orientation.yaw} /></label>
      {/if}
      {#if asset.kind === 'miniature'}<label>Modelled base <BasePicker bind:value={base} /></label>{/if}
      {#if !numbersOk}<p role="alert">Scale and orientation must be numbers.</p>{/if}
      <button onclick={save} disabled={!numbersOk}>Save</button>
      <h2>Folders</h2>
      {#each folders as f (f.id)}<label><input type="checkbox" checked={f.assetIds.includes(id)} onchange={(e) => void toggleFolder(f, e.currentTarget.checked)} /> {f.name}</label>{/each}
      <p><small>{asset.hash.slice(0, 12)}… · {(asset.byteSize / 1024).toFixed(0)} KB</small></p>
      <button onclick={remove}>Delete asset</button>
    </aside>
  </div>
  {#if asset.kind === 'terrain' && glb}<VolumeEditor assetId={id} {glb} {scale} {orientation} />{/if}
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>.layout { display: grid; grid-template-columns: 2fr 1fr; gap: 1rem } .view { min-height: 24rem }</style>
