<!-- viewer/src/lib/setup/SetupToolbar.svelte -->
<!-- SetupToolbar.cs: the closed set of setup edits, one control each, under headings that count. Anyone may use every one of them. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { AssetDto, Planet } from '../api/types';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import { type Catalog, placeAsset, placeBuiltin } from './catalog';
  import { ZONE_STYLES, clearTerrain, objective, token, zoneShapes } from './edits';
  import LayoutsSection from './LayoutsSection.svelte';
  import { PLANETS, randomEdit, randomRequest, randomSummary } from './layouts';
  import {
    MAX_EDGE, MIN_EDGE, SIZE_HINT, beside, cannotAddMarker, cannotAddTerrain, cannotClearTerrain, markersHeading, terrainHeading,
  } from './limits';

  let { draft, catalog, api, send, report }: {
    draft: TableState; catalog: Catalog; api: Api; send: (edit: Edit) => Promise<void>; report: (text: string | null) => void;
  } = $props();

  let width = $state(72), length = $state(48);
  let typing = $state(false);
  // The size fields follow the draft (another player's change shows) unless one is being typed in.
  $effect(() => { const s = draft.size; if (!typing) { width = s.width; length = s.length; } });

  const terrainList = $derived([...catalog.terrain.values()].sort((a, b) => a.label.localeCompare(b.label)));
  let pick = $state('');
  $effect(() => { if (pick === '' || !catalog.terrain.has(pick)) pick = terrainList[0]?.id ?? ''; });
  let planet: Planet = $state('generic');
  let style = $state(ZONE_STYLES[0]!);
  let teamA = $state(1), teamB = $state(2);
  let asking: Question | null = $state(null);
  let assets: AssetDto[] | null = $state(null);
  let asset = $state('');
  let busy = $state(false);

  const addTerrain = $derived(cannotAddTerrain(draft));
  const clear = $derived(cannotClearTerrain(draft));
  const addMarker = $derived(cannotAddMarker(draft));

  function addBuiltin() {
    const entry = catalog.terrain.get(pick);
    if (entry !== undefined) void send({ kind: 'add_terrain', placement: placeBuiltin(entry, beside(draft.size), { degrees: 0 }) });
  }

  async function showAssets() {
    report(null);
    try {
      const list = await api.terrainAssets();
      if (list.length === 0) { report('No terrain in your library; upload some in the web app.'); return; }
      assets = list;
      asset = list[0]!.id;
    } catch (e) { report((e as Error).message); }
  }

  async function addAsset() {
    try {
      // Scale 1: the asset's own scale is already in its mesh and volumes.
      const volumes = await api.assetVolumes(asset);
      assets = null;
      await send({ kind: 'add_terrain', placement: placeAsset(asset, volumes, beside(draft.size)) });
    } catch (e) { report((e as Error).message); }
  }

  async function randomTable() {
    busy = true;
    report(null);
    try {
      // The server rolls and lays the table, balanced across the teams; the draft keeps its size, texture, markers and zones.
      const answer = await api.randomTable(randomRequest(draft, planet));
      await send(randomEdit(draft, answer));
      if (answer.unplaced.length > 0 || answer.unlisted.length > 0) report(randomSummary(answer));
    } catch (e) { report((e as Error).message); }
    busy = false;
  }

  const askClear = () => {
    asking = {
      title: 'Clear terrain?', ok: 'Clear terrain',
      text: `All ${draft.terrain.length} terrain pieces come off the table. Markers and zones stay.`,
      run: () => send(clearTerrain(draft)),
    };
  };
</script>

<div class="toolbar">
  <h2>Setup</h2>

  <details open>
    <summary>Table</summary>
    <p class="muted">{SIZE_HINT}</p>
    <div class="flow">
      <input type="number" aria-label="Width" min={MIN_EDGE} max={MAX_EDGE} bind:value={width} onfocus={() => (typing = true)} onblur={() => (typing = false)} />
      <input type="number" aria-label="Length" min={MIN_EDGE} max={MAX_EDGE} bind:value={length} onfocus={() => (typing = true)} onblur={() => (typing = false)} />
      <button onclick={() => send({ kind: 'set_table_size', size: { width, length } })}>Set size</button>
    </div>
    <select aria-label="Texture" value={draft.texture.builtin ?? ''} onchange={(e) => send({ kind: 'set_texture', texture: { builtin: e.currentTarget.value } })}>
      {#if draft.texture.builtin === undefined}<option value="">(library texture)</option>{/if}
      {#each catalog.textures as t (t)}<option value={t}>{t}</option>{/each}
    </select>
  </details>

  <details open>
    <summary>{terrainHeading(draft)}</summary>
    <select aria-label="Built-in terrain" bind:value={pick}>
      {#each terrainList as t (t.id)}<option value={t.id}>{t.label}</option>{/each}
    </select>
    <button class="wide" disabled={addTerrain !== null} title={addTerrain ?? ''} onclick={addBuiltin}>Add built-in terrain (beside the table)</button>
    <button class="wide" disabled={addTerrain !== null} title={addTerrain ?? ''} onclick={showAssets}>Add my terrain…</button>
    {#if assets !== null}
      <div class="flow">
        <select aria-label="My terrain" bind:value={asset}>{#each assets as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</select>
        <button onclick={addAsset}>Add</button>
        <button onclick={() => (assets = null)}>Cancel</button>
      </div>
    {/if}
    <div class="flow">
      <select aria-label="Planet" bind:value={planet}>{#each PLANETS as p (p.planet)}<option value={p.planet}>{p.label}</option>{/each}</select>
      <button disabled={busy} onclick={randomTable}>Random table</button>
    </div>
    <button class="wide" disabled={clear !== null} title={clear ?? ''} onclick={askClear}>Clear terrain</button>
  </details>

  <details open>
    <summary>{markersHeading(draft)}</summary>
    <div class="flow">
      <button disabled={addMarker !== null} title={addMarker ?? ''} onclick={() => send(objective(draft.size, draft.markers))}>Add objective</button>
      <button disabled={addMarker !== null} title={addMarker ?? ''} onclick={() => send(token(draft.size, draft.markers))}>Add token</button>
    </div>
  </details>

  <details open>
    <summary>Zones</summary>
    <div class="flow">
      <select aria-label="Zone style" bind:value={style}>{#each ZONE_STYLES as z (z)}<option value={z}>{z}</option>{/each}</select>
      <button onclick={() => send({ kind: 'set_zone_style', style, shapes: zoneShapes(style) })}>Set zones</button>
    </div>
    <div class="flow">
      <label>Team <input type="number" min="1" max="8" bind:value={teamA} /></label>
      <label>Team <input type="number" min="1" max="8" bind:value={teamB} /></label>
      <button onclick={() => send({ kind: 'swap_zones', teamOrdinalA: teamA, teamOrdinalB: teamB })}>Swap</button>
    </div>
  </details>

  <details open>
    <summary>Layouts</summary>
    <LayoutsSection {draft} {api} {send} {report} />
  </details>
</div>

<Confirm bind:ask={asking} />

<style>
  h2 { font-size: 1rem; margin: 0.4rem 0; }
  summary { cursor: pointer; font-weight: bold; margin: 0.4rem 0 0.2rem; }
  .flow { display: flex; flex-wrap: wrap; gap: 0.3rem; margin: 0.3rem 0; align-items: center; }
  .wide { display: block; width: 100%; margin: 0.3rem 0; text-align: left; }
  input[type='number'] { width: 5rem; }
  select { max-width: 100%; }
</style>
