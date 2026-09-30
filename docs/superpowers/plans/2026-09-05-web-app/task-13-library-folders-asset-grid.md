# Task 13: The library — folders, the asset grid, the asset page

**Files:**
- Create: `web/src/lib/components/FolderTree.svelte`, `web/src/lib/library/folders.ts`, `web/src/routes/library/+page.svelte`, `web/src/routes/library/[id]/+page.svelte`
- Test: `web/tests/folders.test.ts`

**Interfaces:**
- Consumes: `api.assets.*`, `api.folders.*`, `FolderDto`, `AssetDto`, `UploadDialog`, `MeshPreview` (with `onerror`), `BasePicker`, `baseLabel`, `sameBase`.
- Produces: `buildTree(folders: FolderDto[]): FolderNode[]` (`{ folder, children }`, roots first, sorted by name), `descendantIds(folders, id): Set<Guid>` (the folder and everything under it), `filterAssets(assets, folders, { folderId, kind?, base? })`; `FolderTree` with props `folders`, `selected: Guid | null`, `onselect(id | null)`, `oncreate(parentId | null, name)`, `onrename(id, name)`, `onmove(id, parentId | null)`, `ondelete(id)`; `/library?folder=<id>` and `/library/[id]`.
- The grid loads every page of `GET /v1/assets` (keyset, 200 at a time) into memory — five hundred meshes is the design case and folders are a client-side filter over `assetIds`. Filters: folder (including sub-folders), kind, and modelled base (the asset library's two search axes). An asset in no folder shows under *All*. Folder create, rename, move and delete re-read only `GET /v1/folders`, not the asset pages. A folder row's `ondragstart` stops propagation, so dragging a nested folder moves that folder and not its top-level ancestor. Deleting an asset is never blocked; the page says armies pointing at it will draw their hitbox instead.
- The asset page edits what `PATCH /v1/assets/{id}` accepts — name, scale, orientation, modelled base — over the same `MeshPreview` (loaded from `GET /download`'s presigned URL), lists folder membership as checkboxes (`PUT`/`DELETE /v1/folders/{id}/assets/{assetId}`), and, for terrain, mounts Task 14's `VolumeEditor`. Toggling a checkbox re-reads only the folders, so it neither re-downloads the GLB nor overwrites unsaved edits. `load` resets `glb` and `error`, takes a plain (non-`$state`) token and drops any answer that arrives after a newer load, and reports a presigned download whose response is not `ok`. Save is disabled while scale or an orientation angle is not finite, or scale is not positive. Every catch reports `e.message`, including a failed delete.

Spec: asset library → Folders ("Folders nest, and an asset can be in several … an asset in no folder is fine"; "the answer to a user with five hundred meshes finding one, together with the modelled base as a filter"), The modelled base suggests; the army → Assets are a soft reference ("deleting an asset is not blocked"); management plan Tasks 7–8 (folder cycle → `422`; `DELETE` cascades children, assets untouched).

- [ ] **Step 1: Write the failing tests and `folders.ts`**

```ts
// web/tests/folders.test.ts
import { describe, expect, it } from 'vitest';
import { buildTree, descendantIds, filterAssets } from '../src/lib/library/folders';
import type { AssetDto, FolderDto } from '../src/lib/api/types';

const folders: FolderDto[] = [
  { id: 'orks', name: 'Orks', assetIds: ['a'] }, { id: 'boyz', parentId: 'orks', name: 'Boyz', assetIds: ['b'] },
  { id: 'winter', name: 'Winter', assetIds: ['b'] }, { id: 'nobz', parentId: 'orks', name: 'Nobz', assetIds: [] },
];
const asset = (id: string, kind: AssetDto['kind'] = 'miniature', a = 1.26): AssetDto => ({ id, name: id, kind, hash: id, byteSize: 1, contentType: 'model/gltf-binary', uploaded: true, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, modelledBase: kind === 'miniature' ? { shape: 'circle', a, b: a } : undefined });

describe('folders', () => {
  it('builds a sorted tree and finds descendants', () => {
    const tree = buildTree(folders);
    expect(tree.map((n) => n.folder.name)).toEqual(['Orks', 'Winter']);
    expect(tree[0]!.children.map((n) => n.folder.name)).toEqual(['Boyz', 'Nobz']);
    expect(descendantIds(folders, 'orks')).toEqual(new Set(['orks', 'boyz', 'nobz']));
  });
  it('filters by folder subtree, kind and modelled base', () => {
    const assets = [asset('a'), asset('b'), asset('c', 'terrain'), asset('d', 'miniature', 40 / 25.4)];
    expect(filterAssets(assets, folders, { folderId: 'orks' }).map((x) => x.id)).toEqual(['a', 'b']);
    expect(filterAssets(assets, folders, { folderId: null, kind: 'terrain' }).map((x) => x.id)).toEqual(['c']);
    expect(filterAssets(assets, folders, { folderId: null, base: { shape: 'circle', a: 40 / 25.4, b: 40 / 25.4 } }).map((x) => x.id)).toEqual(['d']);
  });
});
```

```ts
// web/src/lib/library/folders.ts — folders are a client-side grouping over the whole library; nothing else reads them.
import type { AssetDto, AssetKind, Base, FolderDto, Guid } from '$lib/api/types';
import { sameBase } from '$lib/units/bases';
export interface FolderNode { folder: FolderDto; children: FolderNode[] }

export function buildTree(folders: FolderDto[]): FolderNode[] {
  const byParent = new Map<Guid | undefined, FolderDto[]>();
  for (const f of folders) byParent.set(f.parentId, [...(byParent.get(f.parentId) ?? []), f]);
  const build = (parent: Guid | undefined): FolderNode[] =>
    (byParent.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name)).map((f) => ({ folder: f, children: build(f.id) }));
  return build(undefined);
}
export function descendantIds(folders: FolderDto[], id: Guid): Set<Guid> {
  const out = new Set<Guid>([id]);
  let grew = true;
  while (grew) { grew = false; for (const f of folders) if (f.parentId && out.has(f.parentId) && !out.has(f.id)) { out.add(f.id); grew = true; } }
  return out;
}
/** A folder shows its own assets and its sub-folders'; an asset in no folder shows only under *All*. */
export function filterAssets(assets: AssetDto[], folders: FolderDto[], f: { folderId: Guid | null; kind?: AssetKind; base?: Base }): AssetDto[] {
  const subtree = f.folderId ? descendantIds(folders, f.folderId) : null;
  const inFolders = subtree ? new Set(folders.filter((x) => subtree.has(x.id)).flatMap((x) => x.assetIds)) : null;
  return assets.filter((a) => (!inFolders || inFolders.has(a.id)) && (!f.kind || a.kind === f.kind) && (!f.base || (a.modelledBase !== undefined && sameBase(a.modelledBase, f.base))));
}
```

- [ ] **Step 2: Run; expect failures then pass. Write `FolderTree.svelte`:**

```svelte
<script lang="ts">
  import type { FolderDto, Guid } from '$lib/api/types';
  import { buildTree, type FolderNode } from '$lib/library/folders';
  let { folders, selected, onselect, oncreate, onrename, onmove, ondelete }: {
    folders: FolderDto[]; selected: Guid | null; onselect: (id: Guid | null) => void; oncreate: (parentId: Guid | null, name: string) => void;
    onrename: (id: Guid, name: string) => void; onmove: (id: Guid, parentId: Guid | null) => void; ondelete: (id: Guid) => void;
  } = $props();
  const tree = $derived(buildTree(folders));
  let dragging = $state<Guid | null>(null);
  const create = (parentId: Guid | null) => { const name = prompt('Folder name'); if (name?.trim()) oncreate(parentId, name.trim()); };
  const rename = (f: FolderDto) => { const name = prompt('Rename folder', f.name); if (name?.trim() && name !== f.name) onrename(f.id, name.trim()); };
</script>

{#snippet node(n: FolderNode)}
  <li draggable="true" ondragstart={(e) => { e.stopPropagation(); dragging = n.folder.id; }} ondragover={(e) => e.preventDefault()} ondrop={(e) => { e.stopPropagation(); if (dragging && dragging !== n.folder.id) onmove(dragging, n.folder.id); dragging = null; }}>
    <button class:selected={selected === n.folder.id} onclick={() => onselect(n.folder.id)}>{n.folder.name} <small>({n.folder.assetIds.length})</small></button>
    <button title="New sub-folder" onclick={() => create(n.folder.id)}>+</button>
    <button title="Rename" onclick={() => rename(n.folder)}>✎</button>
    <button title="Delete folder and sub-folders (assets stay)" onclick={() => confirm(`Delete "${n.folder.name}" and its sub-folders? Assets are kept.`) && ondelete(n.folder.id)}>×</button>
    {#if n.children.length}<ul>{#each n.children as c (c.folder.id)}{@render node(c)}{/each}</ul>{/if}
  </li>
{/snippet}

<nav ondragover={(e) => e.preventDefault()} ondrop={() => { if (dragging) onmove(dragging, null); dragging = null; }}>
  <button class:selected={selected === null} onclick={() => onselect(null)}>All assets</button>
  <button onclick={() => create(null)}>New folder</button>
  <ul>{#each tree as n (n.folder.id)}{@render node(n)}{/each}</ul>
</nav>
<style>.selected { font-weight: bold } ul { list-style: none; padding-left: 1rem }</style>
```

- [ ] **Step 3: Write `library/+page.svelte`**

```svelte
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
  const actFolder = (fn: () => Promise<unknown>) => fn().then(async () => { folders = await api.folders.list(); error = null; }).catch((e) => (error = e instanceof Error ? e.message : String(e)));
</script>

<h1>Library</h1>
<div class="layout">
  <FolderTree {folders} selected={selected} onselect={(id) => goto(id ? `/library?folder=${id}` : '/library')}
    oncreate={(p, name) => actFolder(() => api.folders.create({ name, parentId: p }))} onrename={(id, name) => actFolder(() => api.folders.patch(id, { name, parentId: folders.find((f) => f.id === id)?.parentId ?? null }))}
    onmove={(id, parentId) => actFolder(() => api.folders.patch(id, { name: folders.find((f) => f.id === id)!.name, parentId }))} ondelete={(id) => actFolder(() => api.folders.delete(id))} />
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
```

- [ ] **Step 4: Write `library/[id]/+page.svelte`**

```svelte
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
  const numbersOk = $derived(asset?.kind === 'texture' || ([scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite) && scale > 0));
  const save = () => api.assets.patch(id, { name, scale, orientation, modelledBase: asset?.kind === 'miniature' && base ? base : undefined }).then((a) => (asset = a)).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const toggleFolder = (f: FolderDto, on: boolean) => (on ? api.folders.addAsset(f.id, id) : api.folders.removeAsset(f.id, id)).then(async () => { folders = await api.folders.list(); }).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const remove = async () => { if (confirm('Delete this asset? Armies pointing at it keep playing and draw the slot\'s hitbox instead.')) { try { await api.assets.delete(id); await goto('/library'); } catch (e) { error = e instanceof Error ? e.message : String(e); } } };
</script>

{#if asset}
  <h1>{asset.name}</h1>
  <div class="layout">
    <div class="view">{#if glb}<MeshPreview {glb} {scale} {orientation} {base} onerror={(m) => (error = m)} />{:else}<p>{asset.kind === 'texture' ? 'A board texture.' : error ? '' : 'No preview: the upload never completed.'}</p>{/if}</div>
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
```

- [ ] **Step 5: Stub `VolumeEditor.svelte` as an empty component so `npm run check` passes (Task 14 fills it); run the app: create nested folders, drag one under another; open the upload dialog and drop `web/tests/fixtures/dense-binary.stl`: the worker converts, the preview stands the ellipsoid up (pitch 90) on a 32 mm ring, the counts read 47,800 → ≤ 15,000; confirm, and the asset appears in MinIO under `{account}/{sha}`; drop `supported-binary.stl` and read the warning; file an asset in two folders, filter by base, open the asset, change its yaw and save, delete it. `npm test` 54 passed. Commit: `Add the library: folders, the asset grid with filters, and the asset page`.**
