<script lang="ts">
  // Another player's Shared folder, opened by their username: browse its tree, preview, copy models or a folder into your library.
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { ApiError } from '$lib/api/http';
  import { errorMessage } from '$lib/api/errors';
  import type { FolderDto, Guid, SharedAssetDto, SharedLibraryDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import MeshPreview from '$lib/components/MeshPreview.svelte';
  import Thumb from '$lib/components/Thumb.svelte';
  import { folderOptions } from '$lib/library/bulk';
  import { buildTree, filterAssets, type FolderNode } from '$lib/library/folders';
  import { copySummary, sharedFolders, usernameProblem } from '$lib/library/shared';
  import { baseLabel } from '$lib/units/bases';
  import { formatBytes } from '$lib/validation';

  const user = $derived(page.url.searchParams.get('user') ?? '');
  let typed = $state(page.url.searchParams.get('user') ?? '');
  let view = $state<SharedLibraryDto | null>(null); let loading = $state(false);
  let mine = $state<FolderDto[]>([]); let into = $state<Guid | ''>('');
  let folderId = $state<Guid | null>(null); let picked = $state<Set<Guid>>(new Set());
  let busy = $state(false); let done = $state<string | null>(null); let error = $state<string | null>(null);
  let preview = $state<{ asset: SharedAssetDto; glb?: Uint8Array; image?: string; failed?: string } | null>(null);
  const problem = $derived(usernameProblem(typed));
  const folders = $derived(view ? sharedFolders(view) : []);
  const tree = $derived(buildTree(folders));
  const current = $derived(folders.find((f) => f.id === folderId) ?? folders.find((f) => !f.parentId) ?? null);
  const shown = $derived(view && current ? filterAssets(view.assets.map((a) => ({ ...a, uploaded: true })), folders, { folderId: current.id }) as SharedAssetDto[] : []);
  const targets = $derived(folderOptions(mine));
  const intoLabel = $derived(into ? targets.find((t) => t.id === into)?.label ?? null : null);

  async function load(name: string) {
    view = null; folderId = null; picked = new Set(); preview = null; done = null; error = null;
    if (!name) return;
    loading = true;
    try { [view, mine] = await Promise.all([api.shared.browse(name), api.folders.list()]); }
    catch (e) { error = e instanceof ApiError && e.status === 404 ? `No Shared folder under the username ${name}.` : errorMessage(e); }
    finally { loading = false; }
  }
  $effect(() => { void load(user); });
  function open(e: SubmitEvent) {
    e.preventDefault();
    if (problem) return;
    void goto(`/library/shared?user=${encodeURIComponent(typed.trim())}`);
  }
  function choose(id: Guid) { folderId = id; picked = new Set(); preview = null; }
  function toggle(id: Guid) { const next = new Set(picked); if (next.has(id)) next.delete(id); else next.add(id); picked = next; }

  async function show(a: SharedAssetDto) {
    if (!view) return;
    if (preview?.asset.id === a.id) { preview = null; return; }
    preview = { asset: a };
    try {
      const { url } = await api.shared.download(view.username, a.id);
      if (preview?.asset.id !== a.id) return;
      if (a.contentType === 'model/gltf-binary') {
        const r = await fetch(url);
        if (!r.ok) throw new Error(`The file could not be downloaded (${r.status}).`);
        const glb = new Uint8Array(await r.arrayBuffer());
        if (preview?.asset.id === a.id) preview = { ...preview, glb };
      } else preview = { ...preview, image: url };
    } catch (e) { if (preview?.asset.id === a.id) preview = { ...preview, failed: e instanceof Error && !('status' in e) ? e.message : errorMessage(e) }; }
  }
  async function copy(what: { assetIds: Guid[] } | { folderId: Guid }) {
    if (!view) return;
    busy = true; done = null; error = null;
    try {
      const r = await api.shared.copy(view.username, { assetIds: 'assetIds' in what ? what.assetIds : null, folderId: 'folderId' in what ? what.folderId : null, destinationId: into || null });
      done = copySummary(r, view.name, intoLabel);
      picked = new Set(); mine = await api.folders.list();
    } catch (e) { error = errorMessage(e); } finally { busy = false; }
  }
</script>

<svelte:head><title>Shared by players · Library · Tabletop</title></svelte:head>

{#snippet node(n: FolderNode)}
  <li>
    <button class="folder quiet" class:selected={current?.id === n.folder.id} aria-current={current?.id === n.folder.id ? 'true' : undefined} onclick={() => choose(n.folder.id)}>
      <Icon name="folder" />{n.folder.name} <small class="num">({n.folder.assetIds.length})</small></button>
    {#if n.children.length}<ul>{#each n.children as c (c.folder.id)}{@render node(c)}{/each}</ul>{/if}
  </li>
{/snippet}

<h1>Library</h1>
<LibraryTabs current="shared" />
<form class="open" onsubmit={open}>
  <label>Username <input bind:value={typed} maxlength={32} autocomplete="off" placeholder="A player's username" aria-invalid={!!(typed && problem)} aria-describedby="user-help" /></label>
  <button type="submit" class="primary" disabled={!!problem}>Open</button>
  {#if typed && problem}<small id="user-help" class="dim">{problem}</small>{/if}
</form>
{#if done}<p class="notice" role="status">{done}</p>{/if}
{#if error}<p role="alert">{error}</p>{/if}
{#if loading}
  <p class="loading" role="status">Loading…</p>
{:else if view}
  <h2 class="owner"><Icon name="player" />{view.name} <small class="dim">· {view.username}</small></h2>
  <div class="layout">
    <nav class="panel" aria-label="Shared folders"><ul class="tree">{#each tree as n (n.folder.id)}{@render node(n)}{/each}</ul></nav>
    <section class="panel stack" aria-label="Shared models">
      <div class="bar">
        <label>Into folder <select bind:value={into} disabled={busy}><option value="">No folder</option>{#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}</select></label>
        {#if current}<button disabled={busy || shown.length === 0} onclick={() => copy({ folderId: current.id })}><Icon name="folder" />Copy folder {current.name}</button>{/if}
        <button class="primary" disabled={busy || picked.size === 0} onclick={() => copy({ assetIds: [...picked] })}><Icon name="plus" />Copy selected{#if picked.size}{' '}({picked.size}){/if}</button>
      </div>
      {#if shown.length === 0}
        <div class="empty"><Icon name="mesh" size="2rem" /><p>No models</p></div>
      {:else}
        <ul class="assets" aria-label="Models">
          {#each shown as a (a.id)}
            <li><input type="checkbox" checked={picked.has(a.id)} aria-label="Select {a.name}" disabled={busy} onchange={() => toggle(a.id)} />
              <Thumb url={a.thumbnail} icon={a.kind} size="2.75rem" /><span class="name"><strong>{a.name}</strong>{' '}<small class="dim">{a.kind}{#if a.modelledBase}{' · '}{baseLabel(a.modelledBase)}{/if}{' · '}{formatBytes(a.byteSize)}</small></span>
              <button class="quiet" aria-pressed={preview?.asset.id === a.id} onclick={() => show(a)}>Preview</button></li>
          {/each}
        </ul>
      {/if}
      {#if preview}
        <div class="view">
          {#if preview.failed}<p role="alert">{preview.failed}</p>
          {:else if preview.glb}<MeshPreview glb={preview.glb} scale={preview.asset.scale} orientation={preview.asset.orientation} base={preview.asset.modelledBase ?? null} onerror={(m) => preview && (preview = { ...preview, glb: undefined, failed: `The preview could not show this mesh: ${m}` })} />
          {:else if preview.image}<img src={preview.image} alt={preview.asset.name} />
          {:else}<p class="loading" role="status">Downloading…</p>{/if}
        </div>
      {/if}
    </section>
  </div>
{/if}
<style>
  .open { display: flex; gap: var(--s2) var(--s3); align-items: center; flex-wrap: wrap; margin-bottom: var(--s4) }
  .open label { display: inline-flex; align-items: center; gap: var(--s2) }
  .layout { display: grid; grid-template-columns: 17rem minmax(0, 1fr); gap: var(--s5); align-items: start }
  @media (max-width: 48rem) { .layout { grid-template-columns: minmax(0, 1fr); gap: var(--s3) } }
  .tree, .tree ul { list-style: none; margin: 0; padding-left: var(--s3) } .tree { padding-left: 0 }
  .tree ul { border-left: 1px solid var(--rule); margin-left: var(--s2) }
  .folder { color: var(--text); justify-content: flex-start; gap: var(--s2); min-height: 2rem; padding: 0 var(--s2); max-width: 100%; white-space: normal; text-align: left }
  .selected { color: var(--accent); background: var(--rule); font-weight: 700 }
  .bar { display: flex; gap: var(--s2) var(--s3); align-items: center; flex-wrap: wrap; padding-bottom: var(--s3); border-bottom: 1px solid var(--rule) }
  .bar label { display: inline-flex; align-items: center; gap: var(--s2); flex-wrap: wrap }
  .assets { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--s1) }
  .assets li { display: flex; align-items: center; gap: var(--s2) }
  .assets :global(.icon) { color: var(--accent); flex: none }
  .name { flex: 1; overflow-wrap: anywhere }
  .view { min-height: 20rem; background: var(--view-bg); display: grid; overflow: hidden }
  .view > p { margin: auto; padding: var(--s5) }
  .view img { max-width: 100%; max-height: 24rem; margin: auto }
  .dim { color: var(--dim) }
  .owner { display: flex; align-items: center; gap: var(--s2) }
</style>
