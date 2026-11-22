<script lang="ts">
  // The army page's mesh picker (mesh-picker spec): search, filter chips, folders, a Suggested row and a virtual grid over the
  // library the page holds; a pick patches the slots of the chosen scope. A modal <dialog>: its close event is the one exit.
  import type { Army, ArmyMeshUse, FolderDto, Guid, ModelSlot, Unit } from '$lib/api/types';
  import { buildTree, type FolderNode } from '$lib/library/folders';
  import { fits, search, type Entry } from '$lib/meshpicker/search';
  import { suggest, type Suggestion } from '$lib/meshpicker/suggest';
  import { changeLine, defaultScope, modelName, scopeChange, scopes, type RowKind, type Scope, type ScopeKind } from '$lib/meshpicker/scope';
  import { gridMove } from '$lib/meshpicker/nav';
  import { loadLastFolder, loadRecent, pushRecent, saveLastFolder } from '$lib/meshpicker/memory';
  import { baseLabel } from '$lib/units/bases';
  import Icon from './Icon.svelte';
  import Thumb from './Thumb.svelte';

  let { open, army, unit, slots, row, label, index, folders, uses, accountId, onpick, onclose }: {
    open: boolean; army: Army; unit: Unit; slots: ModelSlot[]; row: RowKind; label: string;
    index: Entry[]; folders: FolderDto[]; uses: ArmyMeshUse[]; accountId: Guid | null;
    onpick: (scope: Scope, assetId: Guid | null) => void; onclose: () => void;
  } = $props();

  // GAP is the grid's gap, var(--space-3).
  const MIN_TILE = 116, GAP = 16, NAME_H = 46;

  let dialog = $state<HTMLDialogElement>(); let input = $state<HTMLInputElement>();
  let scroller = $state<HTMLDivElement>(); let gridEl = $state<HTMLDivElement>();
  let q = $state(''); let folder = $state<Guid | null>(null);
  let onlyFits = $state(false); let onlyArmy = $state(false); let onlyRecent = $state(false);
  let recent = $state<Guid[]>([]); let active = $state(-1); let navigating = $state(false);
  let scopeKind = $state<ScopeKind>('row');
  let width = $state(800); let scrollTop = $state(0); let viewH = $state(600); let gridTop = $state(0);

  const base = $derived(slots[0]?.base ?? null);
  const held = $derived(new Set(slots.map((s) => s.assetId).filter((x): x is Guid => !!x)));
  const armyIds = $derived(new Set(army.units.flatMap((u) => u.models).map((m) => m.assetId).filter((x): x is Guid => !!x)));
  const recentSet = $derived(new Set(recent));
  const byFolder = $derived(new Map(folders.map((f) => [f.id, f])));
  const tree = $derived(buildTree(folders));
  const counts = $derived.by(() => {
    const c = new Map<Guid, number>();
    for (const e of index) for (const id of e.under) c.set(id, (c.get(id) ?? 0) + 1);
    return c;
  });
  const trail = $derived.by(() => {
    const out: FolderDto[] = [];
    for (let f = folder ? byFolder.get(folder) : undefined; f && !out.includes(f); f = f.parentId ? byFolder.get(f.parentId) : undefined) out.unshift(f);
    return out;
  });
  const children = $derived(folders.filter((f) => (f.parentId ?? null) === folder).sort((a, b) => Number(!!b.shared) - Number(!!a.shared) || a.name.localeCompare(b.name)));

  const hits = $derived(search(index, q, { folder, base, recent }));
  const elsewhere = $derived(folder && q.trim() ? search(index, q, { base }).length - hits.length : 0);
  const fitCount = $derived(hits.filter((h) => h.fits).length);
  const armyCount = $derived(hits.filter((h) => armyIds.has(h.entry.asset.id)).length);
  const recentCount = $derived(hits.filter((h) => recentSet.has(h.entry.asset.id)).length);
  const results = $derived.by(() => {
    const kept = hits.filter((h) => (!onlyFits || h.fits) && (!onlyArmy || armyIds.has(h.entry.asset.id)) && (!onlyRecent || recentSet.has(h.entry.asset.id)));
    return onlyRecent ? kept.sort((a, b) => recent.indexOf(a.entry.asset.id) - recent.indexOf(b.entry.asset.id)) : kept;
  });

  const cols = $derived(Math.max(3, Math.floor((width + GAP) / (MIN_TILE + GAP))));
  const tileW = $derived((width - GAP * (cols - 1)) / cols);
  const rowH = $derived(tileW + NAME_H + GAP);
  const browsing = $derived(!q.trim() && !onlyFits && !onlyArmy && !onlyRecent);
  const suggestions = $derived<Suggestion[]>(browsing && open ? suggest({ index, unit, slots, uses, recent, limit: cols }) : []);
  const items = $derived([...suggestions.map((s) => s.entry), ...results.map((h) => h.entry)]);
  const rows = $derived(Math.ceil(results.length / cols));
  const first = $derived(Math.max(0, Math.floor((scrollTop - gridTop) / rowH) - 2));
  const last = $derived(Math.min(rows, Math.ceil((scrollTop + viewH - gridTop) / rowH) + 2));
  const visible = $derived(results.slice(first * cols, last * cols));

  const available = $derived(scopes(army, unit, slots, row));
  const scope = $derived(available.find((s) => s.kind === scopeKind) ?? available[0]!);
  const target = $derived(active >= 0 ? (items[active]?.asset.id ?? null) : null);
  const change = $derived(changeLine(scopeChange(army, scope, target)));
  const anyMesh = $derived(scope.slotIds.some((id) => army.units.some((u) => u.models.some((m) => m.id === id && m.assetId))));

  $effect(() => {
    if (open && dialog && !dialog.open) {
      q = ''; onlyFits = false; onlyArmy = false; onlyRecent = false; navigating = false;
      folder = accountId ? loadLastFolder(accountId, folders) : null;
      recent = accountId ? loadRecent(accountId) : [];
      scopeKind = defaultScope(available, row);
      active = 0;
      dialog.showModal(); input?.focus();
      if (scroller) scroller.scrollTop = 0;
    }
  });
  $effect(() => {
    if (!gridEl || !scroller) return;
    const ro = new ResizeObserver(() => { width = gridEl!.clientWidth; viewH = scroller!.clientHeight; gridTop = gridEl!.offsetTop; });
    ro.observe(gridEl); ro.observe(scroller);
    return () => ro.disconnect();
  });
  // the grid moves down when the Suggested row comes or goes
  $effect(() => { void suggestions.length; void cols; if (gridEl) gridTop = gridEl.offsetTop; });

  function setFolder(id: Guid | null) {
    folder = id; active = 0; navigating = false;
    if (accountId) saveLastFolder(accountId, id);
    if (scroller) scroller.scrollTop = 0;
  }
  function typed() { active = items.length ? 0 : -1; navigating = false; if (scroller) scroller.scrollTop = 0; }
  function toggle(set: () => void) { set(); active = 0; navigating = false; }

  function reveal(i: number) {
    if (!scroller || i < 0) return;
    if (i < suggestions.length) { scroller.querySelector(`#mp-opt-${i}`)?.scrollIntoView({ block: 'nearest' }); return; }
    const top = gridTop + Math.floor((i - suggestions.length) / cols) * rowH;
    if (top < scroller.scrollTop) scroller.scrollTop = top - GAP;
    else if (top + rowH > scroller.scrollTop + viewH) scroller.scrollTop = top + rowH - viewH + GAP;
  }
  function keys(e: KeyboardEvent) {
    const vertical = ['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp'].includes(e.key);
    const along = ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key);
    if (e.key === 'Enter' && e.target === input) {
      e.preventDefault();
      if (active >= 0 && items[active]) pick(items[active]!.asset.id);
      return;
    }
    if (!vertical && !(along && (navigating || !q))) return;
    if (e.target !== input && !(e.target instanceof HTMLElement && e.target.closest('.scroll'))) return;
    e.preventDefault(); navigating = true;
    active = gridMove(active, e.key, { cols, sections: [suggestions.length, results.length], page: Math.max(1, Math.floor(viewH / rowH)) });
    reveal(active);
    input?.focus();
  }
  function pick(assetId: Guid | null) {
    if (assetId && accountId) recent = pushRecent(accountId, assetId);
    onpick(scope, assetId);
    dialog?.close();
  }
  const why = { unit: 'armies', army: 'copy', name: 'search' } as const;
</script>

{#snippet tile(e: Entry, i: number, mark?: Suggestion)}
  <button type="button" role="option" id="mp-opt-{i}" class="tile-m" class:active={i === active} class:held={held.has(e.asset.id)} aria-selected={i === active}
    title={e.asset.name + (e.asset.modelledBase ? ` · ${baseLabel(e.asset.modelledBase)}` : '')} tabindex="-1"
    onclick={() => pick(e.asset.id)} onmousemove={() => { if (active !== i) active = i; }}>
    <span class="pic">
      <Thumb url={e.asset.thumbnail} alt="" size="100%" />
      {#if fits(e.asset, base)}<span class="fit" title="Fits {baseLabel(base!)}"><Icon name="base" size="1.1rem" label="Fits this base" /></span>{/if}
      {#if held.has(e.asset.id)}<span class="now" title="On this row now"><Icon name="ready" size="1.1rem" label="On this row now" /></span>{/if}
      {#if mark}<span class="why why-{mark.reason}"><Icon name={why[mark.reason]} size="0.9rem" />{mark.label}</span>{/if}
    </span>
    <span class="name">{e.asset.name}</span>
  </button>
{/snippet}

{#snippet node(n: FolderNode, depth: number)}
  <li>
    <button type="button" class="quiet fold" class:here={folder === n.folder.id} aria-current={folder === n.folder.id ? 'true' : undefined} style:padding-left="calc(var(--space-2) + {depth} * var(--space-3))" onclick={() => setFolder(n.folder.id)}>
      <Icon name={n.folder.shared ? 'offer' : 'folder'} /><span class="fname">{n.folder.name}</span><small class="num">{counts.get(n.folder.id) ?? 0}</small>
    </button>
    {#if n.children.length && (folder === n.folder.id || trail.some((t) => t.id === n.folder.id))}<ul>{#each n.children as c (c.folder.id)}{@render node(c, depth + 1)}{/each}</ul>{/if}
  </li>
{/snippet}

<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<dialog bind:this={dialog} class="mesh-picker" aria-labelledby="mp-heading" onclose={onclose} onkeydown={keys}>
  <header class="top">
    <Icon name="mesh" size="1.5rem" />
    <h2 id="mp-heading">Mesh for {label}{#if modelName(label) !== unit.name}<span class="dim">{' · '}{unit.name}</span>{/if}</h2>
    {#if base}<span class="chip" title="This row's base"><Icon name="base" size="1em" />{baseLabel(base)}</span>{/if}
    <button type="button" class="icon-btn" aria-label="Close" title="Close" onclick={() => dialog?.close()}><Icon name="close" size="1.1em" /></button>
  </header>

  <div class="find">
    <label class="searchbox"><Icon name="search" /><span class="visually-hidden">Search meshes by name or folder</span>
      <input bind:this={input} bind:value={q} oninput={typed} type="search" placeholder="Search name or folder" autocomplete="off" spellcheck="false"
        role="combobox" aria-expanded="true" aria-controls="mp-list" aria-activedescendant={active >= 0 ? `mp-opt-${active}` : undefined} /></label>
    <div class="chips" role="group" aria-label="Filters">
      {#if base}<button type="button" class="fchip" aria-pressed={onlyFits} onclick={() => toggle(() => (onlyFits = !onlyFits))}><Icon name={onlyFits ? 'ready' : 'base'} />Fits <span class="num">{fitCount}</span></button>{/if}
      <button type="button" class="fchip" aria-pressed={onlyArmy} onclick={() => toggle(() => (onlyArmy = !onlyArmy))}><Icon name={onlyArmy ? 'ready' : 'armies'} />In this army <span class="num">{armyCount}</span></button>
      <button type="button" class="fchip" aria-pressed={onlyRecent} onclick={() => toggle(() => (onlyRecent = !onlyRecent))}><Icon name={onlyRecent ? 'ready' : 'recent'} />Recent <span class="num">{recentCount}</span></button>
      <span class="total num" role="status">{results.length} of {index.length}</span>
    </div>
    <nav class="fchips" aria-label="Folders">
      <button type="button" class="fchip" aria-pressed={folder === null} onclick={() => setFolder(null)}><Icon name="library" />All</button>
      {#each trail as t (t.id)}<span class="sep">›</span><button type="button" class="fchip" aria-pressed={folder === t.id} onclick={() => setFolder(t.id)}><Icon name={t.shared ? 'offer' : 'folder'} />{t.name}</button>{/each}
      {#each children as c (c.id)}<button type="button" class="fchip sub" onclick={() => setFolder(c.id)}><Icon name={c.shared ? 'offer' : 'folder'} />{c.name} <span class="num">{counts.get(c.id) ?? 0}</span></button>{/each}
    </nav>
  </div>

  <div class="body">
    <nav class="tree" aria-label="Folders">
      <ul>
        <li><button type="button" class="quiet fold" class:here={folder === null} aria-current={folder === null ? 'true' : undefined} onclick={() => setFolder(null)}><Icon name="library" /><span class="fname">All</span><small class="num">{index.length}</small></button></li>
        {#each tree as n (n.folder.id)}{@render node(n, 0)}{/each}
      </ul>
    </nav>
    <div class="scroll" bind:this={scroller} onscroll={() => (scrollTop = scroller!.scrollTop)} role="listbox" id="mp-list" aria-label="Meshes" tabindex="-1">
      {#if suggestions.length}
        <h3 class="band"><Icon name="suggest" />Suggested</h3>
        <div class="grid" style:--cols={cols}>{#each suggestions as s, i (s.entry.asset.id)}{@render tile(s.entry, i, s)}{/each}</div>
      {/if}
      <h3 class="band"><Icon name={folder ? 'folder' : 'library'} />{#if trail.length}{trail.map((t) => t.name).join(' › ')}{:else}All{/if} <span class="num dim">· {results.length}</span></h3>
      <div class="vgrid" bind:this={gridEl} style:height="{Math.max(0, rows * rowH - GAP)}px">
        <div class="grid" style:--cols={cols} style:transform="translateY({first * rowH}px)" style:--row="{rowH - GAP}px">
          {#each visible as h, k (h.entry.asset.id)}{@render tile(h.entry, suggestions.length + first * cols + k)}{/each}
        </div>
      </div>
      {#if results.length === 0}<p class="empty"><Icon name="search" />No meshes match</p>{/if}
      {#if elsewhere > 0}<p class="more">{elsewhere} more in other folders <button type="button" class="primary" onclick={() => setFolder(null)}><Icon name="library" />Search all</button></p>{/if}
    </div>
  </div>

  <footer class="foot">
    <div class="scopes" role="radiogroup" aria-label="Applies to">
      <span class="dim">Applies to</span>
      {#each available as s (s.kind)}
        <button type="button" role="radio" class="fchip scope" aria-checked={s.kind === scope.kind} onclick={() => { scopeKind = s.kind; input?.focus(); }}>
          {#if s.kind === scope.kind}<Icon name="ready" />{/if}{s.label} <span class="num">{s.slotIds.length}</span></button>
      {/each}
    </div>
    <div class="go">
      <span class="change" role="status">{change}</span>
      <span class="push"></span>
      <span class="hint dim"><kbd>↵</kbd> pick <kbd>esc</kbd> close</span>
      <button type="button" class="danger" disabled={!anyMesh} onclick={() => pick(null)}><Icon name="remove" />No mesh</button>
    </div>
  </footer>
</dialog>

<style>
  dialog.mesh-picker { width: min(78rem, 96vw); height: min(54rem, 94vh); max-width: none; max-height: none; padding: var(--space-3) var(--space-4); }
  dialog.mesh-picker[open] { display: grid; grid-template-rows: auto auto minmax(0, 1fr) auto; gap: var(--space-3) }
  .top { display: flex; align-items: center; gap: var(--space-3) }
  .top h2 { margin: 0; flex: 1; min-width: 0; font-size: 1.15rem; overflow-wrap: anywhere }
  .top h2::after { display: none }
  .top > :global(.icon) { color: var(--accent) }
  :global([data-theme='fantasy']) .top > :global(.icon) { color: var(--wood) }
  .dim { color: var(--dim) }
  .find { display: grid; gap: var(--space-2) }
  .searchbox { display: flex; align-items: center; gap: var(--space-2) }
  .searchbox :global(.icon) { color: var(--accent); flex: none; width: 1.5rem; height: 1.5rem }
  .searchbox input { flex: 1; min-width: 0; font-size: 1.1rem; min-height: 2.75rem }
  .chips, .fchips { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) }
  .fchip { min-height: 2rem; padding: 0 var(--space-2); gap: var(--space-1); border-radius: 1rem; font-size: .92rem }
  .fchip .num { opacity: .75 }
  .fchip.sub { opacity: .9 }
  .total { margin-left: auto; color: var(--dim) }
  .sep { color: var(--dim) }
  .fchips { display: none }
  .body { display: grid; grid-template-columns: 15rem minmax(0, 1fr); gap: var(--space-3); min-height: 0 }
  .tree { overflow: auto; border-right: 1px solid var(--rule); padding-right: var(--space-2) }
  .tree ul { list-style: none; margin: 0; padding: 0 }
  .fold { width: 100%; justify-content: flex-start; gap: var(--space-2); min-height: 2.1rem; padding: 0 var(--space-2); color: var(--text); white-space: nowrap }
  .fold .fname { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; text-align: left }
  .fold small { color: var(--dim) }
  .fold.here { color: var(--accent); background: var(--rule); font-weight: 700 }
  :global([data-theme='fantasy']) .fold.here { color: var(--wood) }
  .scroll { overflow-y: auto; overflow-x: hidden; position: relative; min-height: 0; padding: var(--space-1) }
  .band { display: flex; align-items: center; gap: var(--space-2); margin: var(--space-2) 0 var(--space-2); font-size: .85rem; letter-spacing: .08em; text-transform: uppercase; color: var(--dim) }
  .band :global(.icon) { color: var(--accent) }
  :global([data-theme='fantasy']) .band :global(.icon) { color: var(--wood) }
  .band:first-child { margin-top: 0 }
  .vgrid { position: relative }
  .grid { display: grid; grid-template-columns: repeat(var(--cols), minmax(0, 1fr)); gap: var(--space-3); margin-bottom: var(--space-3) }
  .vgrid .grid { position: absolute; inset: 0 0 auto 0; grid-auto-rows: var(--row); margin: 0 }
  .tile-m { position: relative; display: flex; flex-direction: column; align-items: stretch; justify-content: flex-start; gap: var(--space-1); padding: var(--space-2); min-height: 0;
    background: var(--panel-solid); color: var(--text); border: 2px solid var(--rule); white-space: normal; text-align: left; clip-path: none; border-radius: 6px }
  .tile-m :global(.thumb) { width: 100%; height: auto; aspect-ratio: 1; border: 0 }
  .tile-m .name { font-size: .85rem; line-height: 1.2; height: 2.4em; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow-wrap: anywhere }
  .tile-m.active { border-color: var(--accent); box-shadow: 0 0 0 2px var(--accent) }
  .tile-m:hover:not(:disabled) { background: var(--panel-solid); color: var(--text); border-color: var(--accent) }
  :global([data-theme='fantasy']) .tile-m.active { border-color: var(--wood); box-shadow: 0 0 0 2px var(--wood) }
  .tile-m.held { border-style: dashed }
  .pic { position: relative; display: block }
  .fit, .now { position: absolute; top: 4px; display: inline-grid; place-items: center; width: 1.6rem; height: 1.6rem; border-radius: 50%; background: var(--panel-solid) }
  .fit { right: 4px; color: var(--success) }
  .now { left: 4px; color: var(--accent) }
  .why { position: absolute; left: 4px; bottom: 4px; display: inline-flex; align-items: center; gap: var(--space-1); max-width: calc(100% - 8px);
    padding: 0 var(--space-1); font-size: .72rem; border-radius: .7em; background: var(--panel-solid); border: 1px solid currentColor; overflow: hidden; white-space: nowrap; text-overflow: ellipsis }
  .why-unit { color: var(--success) } .why-army { color: var(--accent) } .why-name { color: var(--warning) }
  :global([data-theme='fantasy']) .why-army { color: var(--wood) }
  .empty { display: flex; align-items: center; gap: var(--space-2); color: var(--dim) }
  .more { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); color: var(--dim) }
  .foot { display: grid; gap: var(--space-2); padding-top: var(--space-3); border-top: 2px solid var(--accent) }
  :global([data-theme='fantasy']) .foot { border-top-color: var(--wood) }
  .scope[aria-checked='true'], .scope[aria-checked='true']:hover:not(:disabled) { background-color: var(--btn-pressed-bg); color: var(--btn-pressed-text) }
  .scopes { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) }
  .go { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3) }
  .change { color: var(--text) }
  .push { flex: 1 }
  .hint kbd { margin: 0 var(--space-1) 0 var(--space-2) }
  @media (max-width: 60rem) {
    dialog.mesh-picker { width: 100vw; height: 100dvh; max-height: 100dvh; padding: var(--space-3) }
    .body { grid-template-columns: minmax(0, 1fr) }
    .tree { display: none }
    .fchips { display: flex; flex-wrap: nowrap; overflow-x: auto; padding-bottom: var(--space-1) }
    .fchips > * { flex: none }
    .hint { display: none }
    .total { margin-left: 0 }
  }
</style>
