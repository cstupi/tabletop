<!-- viewer/src/lib/setup/SetupTools.svelte -->
<!-- The setup sidebar (SetupToolbar.cs's closed set of edits): Table, Terrain (a search over the catalog, grouped), Objectives, Zones,
     Layouts and Players, each a collapsible section. Anyone may use every control; each sends one edit Godot sends. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { ArmySummary, AssetDto } from '../api/types';
  import type { ConnectionState } from '../connection/gameConnection';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import TeamCards from '../lobby/TeamCards.svelte';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import type { BuiltinTerrain, Catalog } from './catalog';
  import { ZONE_STYLES, addAsset as addAssetEdit, addBuiltin as addBuiltinEdit, clearTerrain, objective, swapZones, tableSize, texture, zones } from './edits';
  import LayoutsSection from './LayoutsSection.svelte';
  import {
    MARKERS, MAX_EDGE, MIN_EDGE, TERRAIN, cannotAddMarker, cannotAddTerrain, cannotClearTerrain,
  } from './limits';
  import { terrainGroups, terrainSummary, textureLabel, zoneStyleLabel } from './terrainGroups';
  import { SizeFields } from './sizeFields';
  import { untrack } from 'svelte';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { draft, catalog, api, snap, creator, send, report }: {
    draft: TableState; catalog: Catalog; api: Api; snap: ConnectionState; creator: boolean;
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
  const groups = $derived(terrainGroups(catalog, query, snap.terrainWords));
  let style = $state(ZONE_STYLES[0]!);
  const teams = $derived([...(snap.game?.teams ?? [])].sort((a, b) => a.ordinal - b.ordinal).map((t) => t.ordinal));
  let teamA = $state(1), teamB = $state(2);
  let asking: Question | null = $state(null);
  let assets: AssetDto[] | null = $state(null);
  let armies: ArmySummary[] = $state([]);

  const addTerrain = $derived(cannotAddTerrain(draft));
  const clear = $derived(cannotClearTerrain(draft));
  const addMarker = $derived(cannotAddMarker(draft));

  $effect(() => { api.listArmies().then((list) => { armies = list; }).catch(() => {}); });

  const addBuiltin = (entry: BuiltinTerrain) =>
    send(addBuiltinEdit(entry, draft.size));

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
  <details class="section" open>
    <summary><span>Table</span><span class="meta">{draft.size.width} × {draft.size.length}"</span></summary>
    <div class="body">
      <div class="size">
        <label>Width <input type="number" min={MIN_EDGE} max={MAX_EDGE} bind:value={width} /></label>
        <span class="x" aria-hidden="true">×</span>
        <label>Length <input type="number" min={MIN_EDGE} max={MAX_EDGE} bind:value={length} /></label>
        <button class="small" disabled={!sizeChanged} onclick={() => send(tableSize(width, length))}>Set</button>
      </div>
      <small class="dim">Inches, {MIN_EDGE}–{MAX_EDGE}" a side.</small>
      <label>Surface
        <select value={draft.texture.builtin ?? ''} onchange={(e) => send(texture(e.currentTarget.value))}>
          {#if draft.texture.builtin === undefined}<option value="">From a library</option>{/if}
          {#each catalog.textures as t (t)}<option value={t}>{textureLabel(t)}</option>{/each}
        </select>
      </label>
    </div>
  </details>

  <details class="section" open>
    <summary><span>Terrain</span><span class="meta">{draft.terrain.length} / {TERRAIN}</span></summary>
    <div class="body">
      <input type="search" class="search" placeholder="Search terrain" aria-label="Search terrain" bind:value={query} />
      {#if addTerrain !== null}<small class="limit">{addTerrain}</small>{/if}
      <div class="list" role="list">
        {#each groups as group (group.title)}
          <div class="group" role="listitem">
            <h4>{group.title}</h4>
            <ul>
              {#each group.entries as entry (entry.id)}
                <li>
                  <button class="row" disabled={addTerrain !== null} title={`Add ${entry.label} beside the table: ${terrainSummary(entry, snap.terrainWords)}`}
                    onclick={() => addBuiltin(entry)}>
                    <span class="label">{entry.label}</span><Icon name="plus" size="1em" />
                  </button>
                </li>
              {/each}
            </ul>
          </div>
        {:else}
          <p class="dim none">No terrain matches “{query}”.</p>
        {/each}
        {#if assets !== null}
          <div class="group" role="listitem">
            <h4>Your library</h4>
            <ul>
              {#each assets as a (a.id)}
                <li><button class="row" disabled={addTerrain !== null} title={`Add ${a.name} beside the table`} onclick={() => addAsset(a)}>
                  <span class="label">{a.name}</span><Icon name="plus" size="1em" />
                </button></li>
              {/each}
            </ul>
          </div>
        {/if}
      </div>
      <small class="dim">New pieces land beside the table; drag them on.</small>
      <div class="pair">
        {#if assets === null}<button class="small" disabled={addTerrain !== null} onclick={showAssets}><Icon name="library" />Your library</button>{/if}
        <button class="small danger" disabled={clear !== null} title={clear ?? ''} onclick={askClear}>Clear terrain</button>
      </div>
    </div>
  </details>

  <details class="section" open>
    <summary><span>Objectives</span><span class="meta">{draft.markers.length} / {MARKERS}</span></summary>
    <div class="body">
      <div class="pair">
        <button class="small" disabled={addMarker !== null} title={addMarker ?? ''} onclick={() => send(objective(draft.size, draft.markers))}><Icon name="objective" />Objective</button>
      </div>
    </div>
  </details>

  <details class="section" open>
    <summary><span>Deployment zones</span></summary>
    <div class="body">
      <div class="inline">
        <select aria-label="Zone style" bind:value={style}>{#each ZONE_STYLES as z (z)}<option value={z}>{zoneStyleLabel(z)}</option>{/each}</select>
        <button class="small" onclick={() => send(zones(style))}>Set</button>
      </div>
      {#if teams.length >= 2}
        <small class="dim">Swap two teams' zones</small>
        <div class="inline swap">
          <select aria-label="First team" bind:value={teamA}>{#each teams as t (t)}<option value={t}>Team {t}</option>{/each}</select>
          <span>and</span>
          <select aria-label="Second team" bind:value={teamB}>{#each teams as t (t)}<option value={t}>Team {t}</option>{/each}</select>
          <button class="small" disabled={teamA === teamB} onclick={() => send(swapZones(teamA, teamB))}>Swap</button>
        </div>
      {/if}
    </div>
  </details>

  <details class="section" open>
    <summary><span>Layouts</span></summary>
    <div class="body"><LayoutsSection {draft} {api} {send} {report} /></div>
  </details>

  {#if snap.game !== null}
    <details class="section">
      <summary><span>Players</span><span class="meta">{snap.game.participants.length}</span></summary>
      <div class="body"><TeamCards {snap} {armies} {creator} {send} compact /></div>
    </details>
  {/if}
</div>

<Confirm bind:ask={asking} />

<style>
  .tools { display: flex; flex-direction: column; }
  .section { border-bottom: 1px solid var(--rule); }
  summary { display: flex; align-items: center; gap: var(--s2); padding: var(--s3) var(--s4); cursor: pointer; list-style: none;
    font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case); letter-spacing: var(--display-tracking);
    font-size: .85rem; color: var(--accent); user-select: none; }
  :global([data-theme='fantasy']) summary { color: var(--wood); font-size: 1rem; }
  summary::-webkit-details-marker { display: none; }
  summary::before { content: ''; width: .45rem; height: .45rem; border-right: 2px solid currentColor; border-bottom: 2px solid currentColor;
    transform: rotate(-45deg); transition: transform .12s; flex: none; margin-right: var(--s1); }
  details[open] > summary::before { transform: rotate(45deg); }
  summary:hover { color: var(--text); }
  .meta { margin-left: auto; font-family: var(--font-num); font-weight: var(--num-weight); font-size: .8rem; color: var(--dim); text-transform: none; letter-spacing: 0; }
  .body { display: flex; flex-direction: column; gap: var(--s2); padding: 0 var(--s4) var(--s4); }
  label { display: flex; flex-direction: column; gap: var(--s1); color: var(--dim); font-size: .9rem; }
  label select, label input { width: 100%; color: var(--text); font-size: 1rem; }
  .size { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr) auto; align-items: end; gap: var(--s2); }
  .size input { width: 100%; }
  .x { padding-bottom: .6rem; color: var(--dim); }
  .size button { min-height: 2.5rem; }
  .search { width: 100%; }
  .limit { color: var(--warning); }
  .list { max-height: 22rem; overflow-y: auto; margin: 0 calc(-1 * var(--s2)); padding: 0 var(--s2); border-top: 1px solid var(--rule); border-bottom: 1px solid var(--rule); }
  .group h4 { margin: var(--s3) 0 var(--s1); font-size: .75rem; font-weight: 600; color: var(--dim); text-transform: uppercase; letter-spacing: .08em; }
  :global([data-theme='fantasy']) .group h4 { font-family: var(--font-display); text-transform: none; letter-spacing: .03em; font-size: .85rem; color: var(--wood); }
  .group ul { list-style: none; margin: 0 0 var(--s2); padding: 0; }
  .row { all: unset; box-sizing: border-box; display: flex; width: 100%; align-items: center; justify-content: space-between; gap: var(--s2);
    padding: var(--s1) var(--s2); cursor: pointer; color: var(--text); min-height: 2rem; }
  .row :global(.icon) { color: var(--accent); opacity: 0; transition: opacity .12s; }
  .row:hover:not(:disabled), .row:focus-visible { background: var(--rule); }
  .row:hover :global(.icon), .row:focus-visible :global(.icon) { opacity: 1; }
  .row:focus-visible { outline: 2px solid var(--focus); outline-offset: -2px; }
  .row:disabled { cursor: not-allowed; opacity: .5; }
  .label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .none { margin: var(--s3) 0; font-size: .9rem; }
  .pair { display: flex; flex-wrap: wrap; gap: var(--s2); }
  .pair > button { flex: 1; }
  .inline { display: flex; align-items: center; gap: var(--s2); }
  .inline select { flex: 1; min-width: 0; }
  .swap { color: var(--dim); font-size: .9rem; }
  .swap select { flex: 1 1 0; font-size: .95rem; padding-left: var(--s2); }
  small { font-size: .85rem; }
</style>
