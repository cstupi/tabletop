<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { AssetDto, AssetKind, Base, FolderDto, Guid } from '$lib/api/types';
  import FolderTree from '$lib/components/FolderTree.svelte';
  import UploadDialog from '$lib/components/UploadDialog.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import Icon from '$lib/components/Icon.svelte';
  import LibraryTabs from '$lib/components/LibraryTabs.svelte';
  import Thumb from '$lib/components/Thumb.svelte';
  import OfferDialog from '$lib/components/OfferDialog.svelte';
  import { filterAssets } from '$lib/library/folders';
  import { ASSET_DRAG, folderOptions, holdersIn, runBulk, uploadTarget, type BulkOutcome } from '$lib/library/bulk';
  import { clear, click, keepOnly, NONE, selectAll, type Selection } from '$lib/library/selection';
  import { baseLabel } from '$lib/units/bases';
  import { inchesToMm, round } from '$lib/units/units';
  import { ASSET_QUOTA, formatBytes, libraryUsage } from '$lib/validation';

  let username = $state<string | null>(null);
  let folders = $state<FolderDto[]>([]); let assets = $state<AssetDto[]>([]); let loading = $state(true);
  let kind = $state<AssetKind | ''>(''); let base = $state<Base | null>(null); let uploading = $state(false); let error = $state<string | null>(null);
  let addButton = $state<HTMLButtonElement>();
  let selection = $state<Selection>(NONE); let busy = $state(false); let done = $state<string | null>(null); let grid = $state<HTMLElement>();
  const selected = $derived(page.url.searchParams.get('folder'));
  const folderName = $derived(selected ? folders.find((f) => f.id === selected)?.name : undefined);
  const shown = $derived(filterAssets(assets, folders, { folderId: selected, kind: kind || undefined, base: base ?? undefined }));
  const order = $derived(shown.map((a) => a.id));
  const count = $derived(selection.ids.size);
  const targets = $derived(folderOptions(folders));
  const usage = $derived(libraryUsage(assets));
  const folderOfferable = $derived(selected ? filterAssets(assets, folders, { folderId: selected }).some((a) => a.uploaded) : false);
  let offering = $state<{ what: string; assetIds: Guid[] | null; folderId: Guid | null } | null>(null);
  const nearQuota = $derived(usage.assets >= ASSET_QUOTA.assets * 0.9 || usage.bytes >= ASSET_QUOTA.bytes * 0.9);
  // The selection holds only tiles in view: changing folder or filter, or deleting, drops the rest.
  $effect(() => { const next = keepOnly(selection, order); if (next !== selection) selection = next; });

  async function loadAll() {
    loading = true;
    try {
      const all: AssetDto[] = [];
      for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after }); all.push(...p.items); if (!p.next) break; after = p.next; }
      [assets, folders] = [all, await api.folders.list()];
      username = (await api.account.get()).username ?? null;
    } catch (e) { error = errorMessage(e); } finally { loading = false; }
  }
  $effect(() => { void loadAll(); });
  const actFolder = (fn: () => Promise<unknown>) => fn().then(async () => { folders = await api.folders.list(); error = null; }).catch((e) => (error = errorMessage(e)));
  function closeUpload() { uploading = false; addButton?.focus(); }

  // Selection: Ctrl/Cmd-click or the checkbox toggles, Shift-click adds the range from the last toggled tile; with anything selected a plain click toggles too.
  const pick = (id: Guid, range: boolean) => { selection = click(selection, order, id, { range }); done = null; };
  let pressTimer: ReturnType<typeof setTimeout> | undefined; let pressed = false; let pressAt = { x: 0, y: 0 };
  function onTileClick(e: MouseEvent, id: Guid) {
    if (pressed) { pressed = false; e.preventDefault(); return; }
    if (e.ctrlKey || e.metaKey || e.shiftKey || count > 0) { e.preventDefault(); pick(id, e.shiftKey); }
  }
  // Long-press on a touch screen selects, as Ctrl-click does with a mouse.
  function onPointerDown(e: PointerEvent, id: Guid) {
    if (e.pointerType !== 'touch') return;
    pressAt = { x: e.clientX, y: e.clientY }; clearTimeout(pressTimer);
    pressTimer = setTimeout(() => { pressed = true; pick(id, false); }, 500);
  }
  const cancelPress = () => clearTimeout(pressTimer);
  function onPointerMove(e: PointerEvent) { if (Math.hypot(e.clientX - pressAt.x, e.clientY - pressAt.y) > 10) cancelPress(); }
  function onTileKey(e: KeyboardEvent, id: Guid, i: number) {
    if (e.key === ' ') { e.preventDefault(); pick(id, e.shiftKey); return; }
    const tiles = [...(grid?.querySelectorAll<HTMLElement>('a.tile') ?? [])];
    const columns = grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : 1;
    const step = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: columns, ArrowUp: -columns }[e.key];
    if (step === undefined) return;
    e.preventDefault(); tiles[Math.min(Math.max(i + step, 0), tiles.length - 1)]?.focus();
  }
  function onDragStart(e: DragEvent, id: Guid) {
    const ids = selection.ids.has(id) ? [...selection.ids] : [id];
    e.dataTransfer?.setData(ASSET_DRAG, JSON.stringify(ids));
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copyMove';
  }

  const names = (ids: Guid[]) => {
    const n = ids.map((id) => assets.find((a) => a.id === id)?.name ?? 'an asset');
    return n.length > 10 ? `${n.slice(0, 10).join(', ')} and ${n.length - 10} more` : n.join(', ');
  };
  const plural = (n: number) => `${n} asset${n === 1 ? '' : 's'}`;
  /** One sentence per kind of failure: assets no longer there, and chunks the server did not answer. */
  function failures(o: BulkOutcome): string | null {
    const parts: string[] = [];
    if (o.notFound.length) parts.push(`${plural(o.notFound.length)} no longer exist: ${names(o.notFound)}.`);
    if (o.failed.length) parts.push(`${plural(o.failed.length)} were not changed (${names(o.failed)}): ${errorMessage(o.error)}`);
    return parts.length ? parts.join(' ') : null;
  }
  async function bulk(work: () => Promise<{ message: string; outcome: BulkOutcome }>) {
    busy = true; error = null; done = null;
    try {
      const { message, outcome } = await work();
      done = outcome.done.length ? message : null; error = failures(outcome);
      folders = await api.folders.list();
    } catch (e) { error = errorMessage(e); } finally { busy = false; }
  }
  /** Files the assets into `target`. Inside a folder it is a move: they leave the folder in view and its sub-folders (except `target`). */
  function moveTo(target: Guid, ids = [...selection.ids]) {
    if (ids.length === 0) return;
    const label = targets.find((t) => t.id === target)?.label ?? 'the folder';
    void bulk(async () => {
      const added = await runBulk(ids, (part) => api.folders.addAssets(target, part));
      const filed = new Set(added.done);
      if (selected && selected !== target) for (const h of holdersIn(folders, selected, ids.filter((id) => filed.has(id)), target)) await runBulk(h.ids, (part) => api.folders.removeAssets(h.folderId, part));
      if (selected && selected !== target) selection = clear();
      return { message: `${selected && selected !== target ? 'Moved' : 'Added'} ${plural(added.done.length)} to ${label}.`, outcome: added };
    });
  }
  function removeFromFolder() {
    if (!selected) return;
    const ids = [...selection.ids];
    void bulk(async () => {
      const outcome: BulkOutcome = { done: [], notFound: [], failed: [] };
      for (const h of holdersIn(folders, selected, ids)) {
        const r = await runBulk(h.ids, (part) => api.folders.removeAssets(h.folderId, part));
        outcome.done.push(...r.done); outcome.notFound.push(...r.notFound); outcome.failed.push(...r.failed); outcome.error ??= r.error;
      }
      if (!outcome.failed.length) selection = clear();
      return { message: `Removed ${plural(new Set(outcome.done).size)} from ${folderName ?? 'the folder'}. They stay in your library.`, outcome };
    });
  }
  function deleteSelected() {
    const ids = [...selection.ids];
    if (!confirm(`Delete ${plural(ids.length)}: ${names(ids)}? Armies pointing at them keep playing and draw the slot's hitbox instead.`)) return;
    void bulk(async () => {
      const outcome = await runBulk(ids, (part) => api.assets.deleteMany(part));
      const gone = new Set([...outcome.done, ...outcome.notFound]);
      assets = assets.filter((a) => !gone.has(a.id));
      return { message: `Deleted ${plural(outcome.done.length)}.`, outcome };
    });
  }
  function offerSelection() {
    const ids = assets.filter((a) => selection.ids.has(a.id) && a.uploaded).map((a) => a.id);
    if (ids.length === 0) { error = 'Only uploaded assets can be offered.'; return; }
    offering = { what: ids.length === 1 ? names(ids) : plural(ids.length), assetIds: ids, folderId: null };
  }
  /** `25 mm`, or `120 × 92 mm` for an oval or a rectangle. */
  const baseSize = (b: Base) => b.shape === 'circle' ? `${round(inchesToMm(b.a), 1)} mm` : `${round(inchesToMm(b.a), 1)} × ${round(inchesToMm(b.b), 1)} mm`;
  function onKey(e: KeyboardEvent) { if (e.key === 'Escape' && count > 0 && !uploading && !offering) { selection = clear(); done = null; } }
</script>

<svelte:head><title>Library · Tabletop</title></svelte:head>
<svelte:window onkeydown={onKey} />

<h1>Library{#if folderName} <small>· {folderName}</small>{/if}</h1>
<LibraryTabs current="mine" />
<div class="layout">
  <div class="panel folders"><FolderTree {folders} selected={selected} onselect={(id) => goto(id ? `/library?folder=${id}` : '/library')}
    oncreate={(p, name) => actFolder(() => api.folders.create({ name, parentId: p }))} onrename={(id, name) => actFolder(() => api.folders.patch(id, { name, parentId: folders.find((f) => f.id === id)?.parentId ?? null }))}
    onmove={(id, parentId) => actFolder(() => api.folders.patch(id, { name: folders.find((f) => f.id === id)!.name, parentId }))} ondelete={(id) => actFolder(() => api.folders.delete(id))}
    onassetsdrop={(id, ids) => moveTo(id, ids)} oninvalid={(m) => (error = m)} {username} onmakeshared={() => actFolder(() => api.folders.createShared())} /></div>
  <section class="panel assets" aria-label="Assets">
    <div class="bar">
      <button class="primary" bind:this={addButton} onclick={() => (uploading = true)}><Icon name="upload" />Add STL / GLB / texture</button>
      <label>Kind <select bind:value={kind}><option value="">Any kind</option><option value="miniature">Miniatures</option><option value="terrain">Terrain</option><option value="texture">Textures</option></select></label>
      <label>Made for base <BasePicker bind:value={base} allowNone label="Made for base" /></label>
      {#if selected && folderOfferable}<button disabled={busy} onclick={() => (offering = { what: `folder ${folderName ?? ''}`, assetIds: null, folderId: selected })}>Offer folder…</button>{/if}
      <span class="count" role="status">{#if !loading}Showing {shown.length} of {assets.length}{/if}</span>
    </div>
    {#if !loading && shown.length > 0}
      <div class="selbar" class:active={count > 0} role="toolbar" aria-label="Selection">
        {#if count > 0}
          <strong role="status">{count} selected</strong>
          <label class="pickfolder"><span class="visually-hidden">{selected ? 'Move the selection to a folder' : 'Add the selection to a folder'}</span>
            <select value="" disabled={busy || targets.length === 0} onchange={(e) => { const v = e.currentTarget.value; e.currentTarget.value = ''; if (v) moveTo(v); }}>
              <option value="" disabled>{selected ? 'Move to folder…' : 'Add to folder…'}</option>
              {#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}
            </select></label>
          {#if selected}<button disabled={busy} onclick={removeFromFolder}>Remove from folder</button>{/if}
          <button disabled={busy} onclick={offerSelection}>Offer…</button>
          <button class="danger" disabled={busy} onclick={deleteSelected}>Delete</button>
          <span class="end">{#if count < shown.length}<button class="quiet" disabled={busy} onclick={() => (selection = selectAll(selection, order))}>Select all {shown.length}</button>{/if}<button class="quiet" disabled={busy} onclick={() => { selection = clear(); done = null; }}>Clear</button></span>
        {:else}
          <button class="quiet" onclick={() => (selection = selectAll(selection, order))}>Select all {shown.length}</button>
        {/if}
      </div>
    {/if}
    {#if done}<p class="notice" role="status">{done}</p>{/if}
    {#if error}<p role="alert">{error}</p>{/if}
    {#if loading}
      <p class="loading" role="status">Loading…</p>
    {:else if assets.length === 0}
      <div class="empty"><Icon name="library" size="2.5rem" /><p>Your library is empty. Add an STL (or several parts of one kit), a GLB or a board texture.</p></div>
    {:else if shown.length === 0}
      <div class="empty"><p>Nothing here matches. {#if selected}This folder and its sub-folders hold no matching assets.{:else}Change the kind or base filter.{/if}</p></div>
    {:else}
      <div class="grid" role="grid" aria-label="Assets" aria-multiselectable="true" bind:this={grid}>
        {#each shown as a, i (a.id)}
          {@const on = selection.ids.has(a.id)}
          <div role="row" class="row"><div role="gridcell" class="cell" class:on aria-selected={on}>
            <input type="checkbox" class="check" checked={on} aria-label="Select {a.name}" disabled={busy}
              onclick={(e) => { pick(a.id, e.shiftKey); e.currentTarget.checked = selection.ids.has(a.id); }} />
            <a class="tile" href="/library/{a.id}" draggable="true" onclick={(e) => onTileClick(e, a.id)} onkeydown={(e) => onTileKey(e, a.id, i)}
              ondragstart={(e) => onDragStart(e, a.id)} onpointerdown={(e) => onPointerDown(e, a.id)} onpointerup={cancelPress} onpointercancel={cancelPress} onpointermove={onPointerMove}
              oncontextmenu={(e) => { if (pressed) e.preventDefault(); }}>
              <Thumb url={a.thumbnail} icon={a.kind} size="100%" alt="" />
              <span class="label"><strong>{a.name}</strong>
                <small class="meta"><span title={a.kind}><Icon name={a.kind} size="1.1em" label={a.kind} /></span>{#if a.modelledBase}<span title={baseLabel(a.modelledBase)}><Icon name="base" size="1.1em" />{baseSize(a.modelledBase)}</span>{/if}{#if !a.uploaded}<span class="warn"><Icon name="warning" size="1.1em" />upload incomplete</span>{/if}</small></span></a>
          </div></div>
        {/each}
      </div>
    {/if}
    {#if !loading}
      {@const fill = Math.max(usage.assets / ASSET_QUOTA.assets, usage.bytes / ASSET_QUOTA.bytes)}
      <div class="usage" class:near={nearQuota}>
        <span class="meter" role="meter" aria-label="Library space" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(fill * 100)}><span style:width="{Math.min(fill, 1) * 100}%"></span></span>
        <small><span class="num">{formatBytes(usage.bytes)}</span> of {formatBytes(ASSET_QUOTA.bytes)} · <span class="num">{usage.assets}</span> of {ASSET_QUOTA.assets}{#if nearQuota}{' · '}nearly full{/if}</small>
      </div>
    {/if}
  </section>
</div>
<OfferDialog open={offering !== null} what={offering?.what ?? ''} assetIds={offering?.assetIds ?? null} folderId={offering?.folderId ?? null} onclose={() => (offering = null)} />
<UploadDialog open={uploading} {folders} folderId={uploadTarget(folders, selected)} onclose={closeUpload} onuploaded={() => void loadAll()} />
<style>
  .layout { display: grid; grid-template-columns: 17rem minmax(0, 1fr); gap: var(--space-4); align-items: start }
  @media (max-width: 48rem) { .layout { grid-template-columns: minmax(0, 1fr); gap: 0 } }
  .bar { display: flex; gap: var(--space-2) var(--space-3); align-items: center; flex-wrap: wrap; padding-bottom: var(--space-3); border-bottom: 1px solid var(--rule) }
  .bar label { display: inline-flex; align-items: center; gap: var(--space-2); flex-wrap: wrap }
  .count { color: var(--dim); margin-left: auto }
  .selbar { display: flex; gap: var(--space-2) var(--space-3); align-items: center; flex-wrap: wrap; padding: var(--space-2) 0; min-height: 2.75rem }
  .selbar.active { position: sticky; top: 0; z-index: 2; background: var(--panel-solid); padding: var(--space-2) var(--space-3); margin: var(--space-2) calc(-1 * var(--space-3)) 0; border-bottom: 2px solid var(--accent) }
  .selbar .end { display: inline-flex; gap: var(--space-2); margin-left: auto }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(10.5rem, 40%), 1fr)); gap: var(--space-3); margin: var(--space-3) 0 }
  .row, .cell { display: block; position: relative; height: 100% }
  .check { position: absolute; top: var(--space-2); right: var(--space-2); z-index: 1; width: 1.35rem; height: 1.35rem; margin: 0; cursor: pointer }
  .tile { display: flex; flex-direction: column; gap: var(--space-2); height: 100%; padding: var(--space-2); color: var(--text); text-decoration: none; border: 1px solid var(--rule); background: var(--panel-solid); overflow-wrap: anywhere; -webkit-touch-callout: none; user-select: none }
  .tile :global(.icon) { color: var(--accent) }
  :global([data-theme='fantasy']) .tile :global(.icon) { color: var(--wood) }
  .tile > :global(.thumb) { aspect-ratio: 1; height: auto }
  .label { display: flex; flex-direction: column; gap: var(--space-1); padding: 0 var(--space-1) var(--space-1) }
  .meta { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-3); color: var(--dim) }
  .meta > span { display: inline-flex; align-items: center; gap: var(--space-1) }
  .warn { color: var(--warning) }
  .tile:hover { border-color: var(--accent); color: var(--text) }
  .on .tile { border-color: var(--accent); box-shadow: inset 0 0 0 2px var(--accent); background: var(--rule) }
  :global([data-theme='fantasy']) .tile { border-radius: 4px; background: #F1E6CA; border-color: #4A2F1B55 }
  :global([data-theme='fantasy']) .on .tile { border-color: var(--wood); box-shadow: inset 0 0 0 2px var(--wood); background: #E6D3A8 }
  :global([data-theme='scifi']) .tile { clip-path: polygon(8px 0, 100% 0, 100% calc(100% - 8px), calc(100% - 8px) 100%, 0 100%, 0 8px) }
  .usage { display: flex; flex-direction: column; gap: var(--space-1); margin: 0 }
  .meter { display: block; height: 6px; background: var(--rule); overflow: hidden }
  .meter > span { display: block; height: 100%; background: var(--accent) }
  .near .meter > span { background: var(--warning) }
  :global([data-theme='fantasy']) .meter > span { background: var(--wood) }
</style>
