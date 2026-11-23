<!-- viewer/src/lib/setup/SetupTools.svelte -->
<!-- The setup sidebar (SetupToolbar.cs, spec 2026-11-07-ux-redesign-1-client-shell.md → Setup): Still to do and the teams on top, a line a team
     that opens into the compact columns; then Terrain (Random table as the primary tile with the planet inside, the search, the group legend
     whose dots filter, the catalog as tiles that add beside the table on a click or land where they are dropped on the board, My terrain,
     Clear), Objectives (a row each), Table (size and the prints), Zones (a diagram in the team colours, the styles as tiles, ⇄ swap) and
     Layouts. Anyone may use every control; each sends one edit Godot sends. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { ArmySummary, AssetDto, Planet } from '../api/types';
  import { teamColour } from '../board/palette';
  import type { ConnectionState } from '../connection/gameConnection';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import type { Geometry } from '../geometry';
  import TeamColumns from '../lobby/TeamColumns.svelte';
  import TeamIcon from '../lobby/TeamIcon.svelte';
  import { columnLine, columnsOf, stillToDo } from '../lobby/teamColumns';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import type { BuiltinTerrain, Catalog } from './catalog';
  import {
    ZONE_STYLES, addAsset as addAssetEdit, addBuiltin as addBuiltinEdit, clearTerrain, objective, removeMarker, swapZones, tableSize, texture, zones,
  } from './edits';
  import LayoutsSection from './LayoutsSection.svelte';
  import { MARKERS, MAX_EDGE, MIN_EDGE, TERRAIN, cannotAddMarker, cannotAddTerrain, cannotClearTerrain } from './limits';
  import { PLANETS, randomEdit, randomRequest, randomSummary } from './layouts';
  import { TILE_TYPE, objectiveRows, styleOf, zoneDiagram } from './setupPanel';
  import SetupIcon from './SetupIcon.svelte';
  import { TERRAIN_KINDS, terrainTiles, textureLabel, toggleKind, zoneStyleLabel } from './terrainGroups';
  import { SizeFields } from './sizeFields';
  import { untrack } from 'svelte';

  let { draft, catalog, api, snap, creator, geometry, send, report }: {
    draft: TableState; catalog: Catalog; api: Api; snap: ConnectionState; creator: boolean; geometry: Geometry | null;
    send: (edit: Edit) => Promise<void>; report: (text: string | null) => void;
  } = $props();


  // The size fields follow the draft when its size changes (another player's Set shows) and keep what was typed otherwise.
  const fields = new SizeFields(untrack(() => draft.size));
  let width = $state(fields.width), length = $state(fields.length);
  $effect(() => {
    const s = { width: draft.size.width, length: draft.size.length };
    untrack(() => { fields.width = width; fields.length = length; fields.follow(s); width = fields.width; length = fields.length; });
  });
  const sizeChanged = $derived(width !== draft.size.width || length !== draft.size.length);

  let query = $state('');
  let hidden: ReadonlySet<string> = $state(new Set());
  const tiles = $derived(terrainTiles(catalog, query, hidden, snap.terrainWords));
  let planet: Planet = $state('generic');
  let rolling = $state(false);
  let teamsOpen = $state(false);
  const columns = $derived(snap.game === null ? [] : columnsOf(snap.game, snap.participantId, creator, true));
  const teams = $derived([...(snap.game?.teams ?? [])].sort((a, b) => a.ordinal - b.ordinal).map((t) => t.ordinal));
  const pairs = $derived(teams.flatMap((a, i) => teams.slice(i + 1).map((b) => [a, b] as const)));
  let asking: Question | null = $state(null);
  let assets: AssetDto[] | null = $state(null);
  let armies: ArmySummary[] = $state([]);
  let diagramWidth = $state(256);

  const addTerrain = $derived(cannotAddTerrain(draft));
  const clear = $derived(cannotClearTerrain(draft));
  const addMarker = $derived(cannotAddMarker(draft));
  const objectives = $derived(objectiveRows(draft));
  const style = $derived(styleOf(draft.zones));
  const diagram = $derived(geometry === null ? null
    : zoneDiagram(draft.size, draft.zones, (shape) => geometry!.zonePolygons(shape, draft.size, snap.zones ?? undefined), diagramWidth, 180));

  $effect(() => { api.listArmies().then((list) => { armies = list; }).catch(() => {}); });

  const addBuiltin = (entry: BuiltinTerrain) => send(addBuiltinEdit(entry, draft.size));
  const dragTile = (e: DragEvent, entry: BuiltinTerrain) => {
    if (e.dataTransfer === null || addTerrain !== null) { e.preventDefault(); return; }
    e.dataTransfer.setData(TILE_TYPE, entry.id); e.dataTransfer.effectAllowed = 'copy';
  };

  async function randomTable() {
    rolling = true;
    report(null);
    try {
      // The server rolls and lays the table, balanced across the teams; the draft keeps its size, texture, markers and zones.
      const answer = await api.randomTable(randomRequest(draft, planet));
      await send(randomEdit(draft, answer));
      if (answer.unplaced.length > 0 || answer.unlisted.length > 0) report(randomSummary(answer));
    } catch (e) { report((e as Error).message); }
    rolling = false;
  }

  async function showAssets() {
    report(null);
    try {
      const list = await api.terrainAssets();
      if (list.length === 0) { report('Your library has no terrain yet: upload some on the website\'s Library page.'); return; }
      assets = list;
    } catch (e) { report((e as Error).message); }
  }

  async function addAsset(asset: AssetDto) {
    try {
      // Scale 1: the asset's own scale is already in its mesh and volumes.
      const volumes = await api.assetVolumes(asset.id);
      await send(addAssetEdit(asset.id, volumes.map((v) => v.volume), draft.size));
    } catch (e) { report((e as Error).message); }
  }

  const askClear = () => {
    asking = {
      title: 'Clear terrain?', ok: 'Clear terrain', danger: true,
      text: `All ${draft.terrain.length} terrain pieces come off the table. Objectives and zones stay.`,
      run: () => send(clearTerrain(draft)),
    };
  };
</script>

<div class="tools">
  {#if snap.game !== null}
    <div class="teams">
      <ul class="todo" aria-label="Still to do">
        {#each stillToDo(snap.game, creator) as line (line.text)}
          <li class:missing={line.missing}>{#if line.missing}<TeamIcon name="warning" size={14} />{/if}{line.text}</li>
        {/each}
      </ul>
      {#if teamsOpen}
        <button class="quiet fold" onclick={() => (teamsOpen = false)}>▾ Teams</button>
        <TeamColumns {snap} {armies} {send} compact />
      {:else}
        <div class="lines">
          <ul>
            {#each columns as c (c.teamId ?? 'watching')}
              <li title={`${c.title}: ${c.stateText}`}>
                <span class="chip" style:--team={c.ordinal > 0 ? teamColour(c.ordinal) : 'var(--dim)'}>
                  <TeamIcon name={c.teamId === null ? 'watch' : 'team'} size={13} />{c.teamId === null ? 'Watch' : `Team ${c.ordinal}`}
                  {#if c.state !== 'watching'}<TeamIcon name={c.state === 'ready' ? 'ready' : 'warning'} size={13} />{/if}
                </span>
                <span class="line" class:warn={c.state === 'needsArmy' || c.state === 'needsPlayer'}>{columnLine(c)}</span>
              </li>
            {/each}
          </ul>
          <button class="quiet open" title="Open the teams: move players, pick an army" aria-label="Open the teams" onclick={() => (teamsOpen = true)}>▸</button>
        </div>
      {/if}
    </div>
  {/if}

  <details class="section" open>
    <summary><SetupIcon name="terrain" size={18} /><span>Terrain</span><span class="meta">{draft.terrain.length}/{TERRAIN}</span></summary>
    <div class="body">
      <div class="primary">
        <button class="roll" disabled={rolling} onclick={randomTable}><SetupIcon name="dice" size={22} />{rolling ? 'Rolling…' : 'Random table'}</button>
        <select aria-label="Planet" bind:value={planet}>{#each PLANETS as p (p.planet)}<option value={p.planet}>{p.label}</option>{/each}</select>
      </div>
      <input type="search" class="search" placeholder="Search terrain" aria-label="Search terrain" bind:value={query} />
      <div class="legend" role="group" aria-label="Terrain groups">
        {#each TERRAIN_KINDS as k (k.key)}
          <button class="dot" class:off={hidden.has(k.key)} style:--kind={k.colour} aria-pressed={!hidden.has(k.key)}
            title={hidden.has(k.key) ? `Show ${k.legend}` : `Hide ${k.legend}`} onclick={() => (hidden = toggleKind(hidden, k.key))}>
            <SetupIcon name={k.key} size={14} />{k.legend}
          </button>
        {/each}
      </div>
      {#if addTerrain !== null}<small class="limit">{addTerrain}</small>{/if}
      <div class="tiles">
        {#each tiles as t (t.entry.id)}
          <button class="tile" style:--kind={t.kind.colour} disabled={addTerrain !== null} draggable={addTerrain === null}
            title={`${t.entry.label}: ${t.summary}\nClick: beside the table · Drag: onto the table`}
            ondragstart={(e) => dragTile(e, t.entry)} onclick={() => addBuiltin(t.entry)}>
            <span class="glyph"><SetupIcon name={t.kind.key} size={26} /></span><span class="name">{t.entry.label}</span>
          </button>
        {:else}
          <p class="dim none">No terrain matches</p>
        {/each}
        {#if assets !== null}
          {#each assets as a (a.id)}
            <button class="tile mine" disabled={addTerrain !== null} title={`Add ${a.name} beside the table`} onclick={() => addAsset(a)}>
              <span class="glyph"><SetupIcon name="terrain" size={26} /></span><span class="name">{a.name}</span>
            </button>
          {/each}
        {/if}
      </div>
      <div class="pair">
        {#if assets === null}<button class="small" disabled={addTerrain !== null} onclick={showAssets}><SetupIcon name="add" size={16} />My terrain…</button>{/if}
        <button class="small danger narrow" disabled={clear !== null} title={clear ?? 'Clear terrain'} onclick={askClear}><SetupIcon name="remove" size={16} />Clear</button>
      </div>
    </div>
  </details>

  <details class="section">
    <summary>
      <SetupIcon name="objective" size={18} /><span>Objectives</span><span class="meta">{draft.markers.length}/{MARKERS}</span>
      <button class="small inline-action" disabled={addMarker !== null} title={addMarker ?? 'Add an objective'}
        onclick={(e) => { e.preventDefault(); void send(objective(draft.size, draft.markers)); }}><SetupIcon name="add" size={14} />Objective</button>
    </summary>
    <div class="body">
      {#each objectives as row (row.id)}
        <div class="row">
          <SetupIcon name="objective" size={16} /><span class="label">{row.label}</span><span class="dim">{row.position}</span>
          <select class="menu" aria-label={`${row.label}: more`} value="" onchange={(e) => { if (e.currentTarget.value === 'remove') void send(removeMarker(row.id)); e.currentTarget.value = ''; }}>
            <option value="" disabled>▸</option><option value="remove">Remove</option>
          </select>
        </div>
      {:else}
        <p class="dim none">No objectives</p>
      {/each}
    </div>
  </details>

  <details class="section">
    <summary><SetupIcon name="table" size={18} /><span>Table</span><span class="meta">{draft.size.width} × {draft.size.length}"{draft.texture.builtin !== undefined ? ` · ${textureLabel(draft.texture.builtin)}` : ''}</span></summary>
    <div class="body">
      <div class="size">
        <input type="number" aria-label="Width" title={`Inches, ${MIN_EDGE}–${MAX_EDGE}" a side`} min={MIN_EDGE} max={MAX_EDGE} bind:value={width} />
        <span class="x" aria-hidden="true">×</span>
        <input type="number" aria-label="Length" title={`Inches, ${MIN_EDGE}–${MAX_EDGE}" a side`} min={MIN_EDGE} max={MAX_EDGE} bind:value={length} />
        <button class="small" disabled={!sizeChanged} onclick={() => send(tableSize(width, length))}>Set</button>
      </div>
      <div class="swatches" role="group" aria-label="Surface">
        {#each catalog.textures as t (t)}
          <button class="swatch" class:on={draft.texture.builtin === t} aria-pressed={draft.texture.builtin === t}
            onclick={() => { if (draft.texture.builtin !== t) void send(texture(t)); }}>{textureLabel(t)}</button>
        {/each}
      </div>
    </div>
  </details>

  <details class="section">
    <summary><SetupIcon name="zone" size={18} /><span>Zones</span><span class="meta">{style === null ? '' : zoneStyleLabel(style)}</span></summary>
    <div class="body">
      <div class="diagram" bind:clientWidth={diagramWidth}>
        {#if diagram !== null}
          <svg width={diagram.width} height={diagram.height + 20} role="img" aria-label={`Zones: ${diagram.style}`}>
            <rect x="0" y="0" width={diagram.width} height={diagram.height} class="outline" />
            {#each diagram.zones as z (z.ordinal)}
              {#each z.polygons as p, i (i)}
                <polygon points={p.map((v) => `${v.x},${v.y}`).join(' ')} fill={teamColour(z.ordinal)} fill-opacity="0.55" stroke={teamColour(z.ordinal)} stroke-width="2" />
              {/each}
            {/each}
            <text x={diagram.width / 2} y={diagram.height + 15} text-anchor="middle" class="caption">{diagram.style}</text>
          </svg>
        {/if}
        {#if pairs.length === 1}
          <button class="small swap" title={`Swap Team ${pairs[0]![0]} and Team ${pairs[0]![1]}`} onclick={() => send(swapZones(pairs[0]![0], pairs[0]![1]))}>⇄</button>
        {:else if pairs.length > 1}
          <select class="swap" aria-label="Swap two teams' zones" value="" onchange={(e) => { const [a, b] = e.currentTarget.value.split('-').map(Number); if (a && b) void send(swapZones(a, b)); e.currentTarget.value = ''; }}>
            <option value="" disabled>⇄</option>
            {#each pairs as [a, b] (`${a}-${b}`)}<option value={`${a}-${b}`}>Team {a} ⇄ Team {b}</option>{/each}
          </select>
        {/if}
      </div>
      <div class="styles" role="group" aria-label="Deployment style">
        {#each ZONE_STYLES as z (z)}
          <button class="swatch" class:on={style === z} aria-pressed={style === z} onclick={() => { if (style !== z) void send(zones(z)); }}>{zoneStyleLabel(z)}</button>
        {/each}
      </div>
    </div>
  </details>

  <details class="section">
    <summary><SetupIcon name="layout" size={18} /><span>Layouts</span></summary>
    <div class="body"><LayoutsSection {draft} {api} {send} {report} /></div>
  </details>
</div>

<Confirm bind:ask={asking} />

<style>
  .tools { display: flex; flex-direction: column; }
  .section { border-bottom: 1px solid var(--rule); }
  .teams { display: flex; flex-direction: column; gap: var(--space-2); padding: var(--space-2) var(--space-2) var(--space-2); border-bottom: 1px solid var(--rule); }
  .todo { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; column-gap: var(--space-2); row-gap: 0; color: var(--accent); font-size: .8rem; }
  .todo li { display: flex; align-items: center; gap: var(--space-1); }
  .todo li.missing { color: var(--warning); }
  .lines { display: flex; gap: var(--space-1); align-items: flex-start; }
  .lines ul { list-style: none; margin: 0; padding: 0; flex: 1; min-width: 0; display: flex; flex-direction: column; gap: var(--space-1); }
  .lines li { display: flex; align-items: center; gap: var(--space-2); min-width: 0; font-size: .8rem; }
  .chip { flex: none; display: inline-flex; align-items: center; gap: var(--space-1); padding: var(--space-2); min-width: 5rem; background: var(--team); color: var(--bg);
    text-transform: uppercase; font-size: .72rem; letter-spacing: .04em; }
  .line { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .line.warn { color: var(--warning); }
  .fold { align-self: flex-start; }
  summary { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-2) var(--space-3); cursor: pointer; list-style: none;
    font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case); letter-spacing: var(--display-tracking);
    font-size: .85rem; color: var(--accent); user-select: none; }
  :global([data-theme='fantasy']) summary { color: var(--wood); font-size: 1rem; }
  summary::-webkit-details-marker { display: none; }
  summary::after { content: '▸'; margin-left: var(--space-1); color: var(--dim); }
  details[open] > summary::after { content: '▾'; }
  summary:hover { color: var(--text); }
  .meta { margin-left: auto; font-family: var(--font-num); font-weight: var(--num-weight); font-size: .8rem; color: var(--dim); text-transform: none; letter-spacing: 0; }
  .inline-action { flex: none; padding: var(--space-2); display: inline-flex; align-items: center; gap: var(--space-1); text-transform: none;
    letter-spacing: 0; font-family: var(--font-body); font-weight: normal; font-size: .8rem; }
  .body { display: flex; flex-direction: column; gap: var(--space-2); padding: 0 var(--space-3) var(--space-3); }
  .primary { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: stretch; padding: var(--space-2); border: 1px solid color-mix(in srgb, var(--accent) 70%, transparent);
    border-left-width: 4px; background: color-mix(in srgb, var(--accent) 12%, transparent); }
  .roll { flex: 1 1 100%; min-width: 0; display: inline-flex; align-items: center; gap: var(--space-2); justify-content: flex-start; min-height: 2.5rem; white-space: nowrap; }
  .primary select { flex: 1 1 100%; min-width: 0; }
  .search { width: 100%; }
  .legend { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-2); }
  .dot { all: unset; display: inline-flex; align-items: center; gap: var(--space-1); font-size: .78rem; color: var(--text); cursor: pointer; }
  .dot :global(.icon) { color: var(--kind); }
  .dot.off { opacity: .45; }
  .dot:focus-visible { outline: 2px solid var(--focus); }
  .limit { color: var(--warning); }
  .tiles { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--space-2); max-height: 22rem; overflow-y: auto; padding: var(--space-1); }
  .tile { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: var(--space-1); padding: var(--space-2); min-height: 4.6rem; min-width: 0;
    font-size: .72rem; line-height: 1.15; }
  .tile .glyph { color: var(--kind, var(--accent)); display: inline-flex; }
  .tile .name { white-space: normal; overflow-wrap: break-word; text-align: center; max-width: 100%; }
  .none { margin: var(--space-2) 0; font-size: .9rem; grid-column: 1 / -1; }
  .pair { display: flex; gap: var(--space-2); }
  .pair > button { flex: 1; display: inline-flex; align-items: center; gap: var(--space-1); justify-content: center; }
  .pair > button.narrow { flex: none; }
  .row { display: flex; align-items: center; gap: var(--space-2); font-size: .9rem; color: var(--accent); }
  .row .label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
  .menu { width: 2.6rem; padding: 0 var(--space-1); }
  .size { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr) auto; align-items: center; gap: var(--space-2); }
  .size input { width: 100%; }
  .x { color: var(--dim); }
  .swatches, .styles { display: flex; flex-wrap: wrap; gap: var(--space-2); }
  .swatch { font-size: .8rem; padding: var(--space-2); }
  .swatch.on { outline: 2px solid var(--accent); outline-offset: -2px; background: color-mix(in srgb, var(--accent) 18%, transparent); }
  .diagram { position: relative; width: 100%; }
  .diagram svg { display: block; }
  .outline { fill: color-mix(in srgb, var(--bg) 60%, transparent); stroke: var(--dim); stroke-width: 1.5; }
  .caption { fill: var(--dim); font-size: 12px; }
  .swap { position: absolute; top: 4px; right: 4px; }
  select.swap { width: 3rem; }
  small { font-size: .85rem; }
</style>
