<!-- viewer/src/lib/setup/TerrainCard.svelte -->
<!-- The selected terrain piece or setup marker (TerrainCard.cs in setup): name, source, height, and every edit of the piece. Every volume
     field is one OverrideTerrainVolumes carrying the placement's whole volume list with that volume changed. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { AssetDto } from '../api/types';
  import type { Geometry } from '../geometry';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import type { Catalog } from './catalog';
  import {
    SCALE_MAX, SCALE_MIN, SCALE_STEP, TERRAIN_TURN, overrideVolume, removeMarker, removeTerrain, resetVolumes as resetVolumesEdit, scale, swapAsset, swapBuiltin, turnInDraft, withBottom, withDeclaration, withFootprint, withKind,
    withNotes, withTop,
  } from './edits';
  import { NOTES } from './limits';
  import { declarationWords, markerCardOf, terrainCardOf, type TerrainWords } from './terrainCard';
  import { declarationsOf } from '../live/project';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { draft, catalog, geometry, api, selected, terrainWords = null, send, report, onClose }: {
    draft: TableState; catalog: Catalog; geometry: Geometry; api: Api; selected: string | null; terrainWords?: TerrainWords | null;
    send: (edit: Edit) => Promise<void>; report: (text: string | null) => void; onClose: () => void;
  } = $props();

  const card = $derived(terrainCardOf(draft, 'setup', selected, catalog, terrainWords));
  const marker = $derived(markerCardOf(draft, 'setup', selected));
  const piece = $derived(card === null ? undefined : draft.terrain.find((t) => t.id === card.pieceId));
  const meshes = $derived([...catalog.terrain.values()].sort((a, b) => a.label.localeCompare(b.label)));

  let swap = $state('');
  $effect(() => { swap = piece?.mesh.builtin !== undefined && catalog.terrain.has(piece.mesh.builtin) ? piece.mesh.builtin : meshes[0]?.id ?? ''; });
  let assets: AssetDto[] | null = $state(null);
  let asset = $state('');

  function turnTo(degrees: number) {
    const turn = piece === undefined ? null : turnInDraft(geometry, draft, piece.id, degrees);
    if (turn !== null) void send(turn);
  }

  async function swapForAsset() {
    report(null);
    try {
      const list = await api.terrainAssets();
      if (list.length === 0) { report('Your library has no terrain yet: upload some on the website\'s Library page.'); return; }
      assets = list;
      asset = list[0]!.id;
    } catch (e) { report((e as Error).message); }
  }

  async function resetVolumes() {
    if (piece === undefined) return;
    try {
      const volumes = piece.mesh.builtin !== undefined
        ? [(catalog.terrain.get(piece.mesh.builtin) ?? (() => { throw new Error('That built-in is not in the catalog.'); })()).volume]
        : (await api.assetVolumes(piece.mesh.assetId!)).map((v) => v.volume);
      await send(resetVolumesEdit(piece.id, volumes));
    } catch (e) { report((e as Error).message); }
  }

  const edit = (volumeId: string, change: Parameters<typeof overrideVolume>[2]) => {
    if (piece !== undefined) void send(overrideVolume(piece, volumeId, change));
  };
  const num = (e: Event) => Number((e.currentTarget as HTMLInputElement).value);
</script>

{#if card !== null && piece !== undefined}
  <section class="card" aria-label="Terrain card">
    <header>
      <div class="title">
        <h2>{card.name}</h2>
        <span class="dim">{card.source}{card.height === '' ? '' : ` · ${card.height} high`}</span>
      </div>
      <button class="icon-btn" aria-label="Close the card" title="Close" onclick={onClose}><Icon name="close" /></button>
    </header>

    <div class="group">
      <h3>Placement</h3>
      <div class="grid2">
        <label>Facing (degrees)
          <input type="number" min="0" max="359" step="1" value={card.facing} title="Degrees counter-clockwise from above" onchange={(e) => turnTo(num(e))} />
        </label>
        <label>Scale
          <input type="number" min={SCALE_MIN} max={SCALE_MAX} step={SCALE_STEP} value={piece.scale}
            onchange={(e) => send(scale(piece.id, num(e)))} />
        </label>
      </div>
      <div class="pair">
        <button class="small" title="Q" onclick={() => turnTo(piece.yaw.degrees + TERRAIN_TURN)}>⟲ {TERRAIN_TURN}°</button>
        <button class="small" title="E" onclick={() => turnTo(piece.yaw.degrees - TERRAIN_TURN)}>⟳ {TERRAIN_TURN}°</button>
      </div>
    </div>

    <div class="group">
      <h3>Look</h3>
      <div class="inline">
        <select aria-label="Mesh" bind:value={swap}>{#each meshes as m (m.id)}<option value={m.id}>{m.label}</option>{/each}</select>
        <button class="small" title="Swap the mesh; the volumes stay" onclick={() => send(swapBuiltin(piece.id, swap))}>Swap</button>
      </div>
      {#if assets === null}
        <button class="quiet left" onclick={swapForAsset}>Use a mesh from your library…</button>
      {:else}
        <div class="inline">
          <select aria-label="Your terrain" bind:value={asset}>{#each assets as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</select>
          <button class="small" onclick={() => { assets = null; void send(swapAsset(piece.id, asset)); }}>Swap</button>
          <button class="quiet" onclick={() => (assets = null)}>Cancel</button>
        </div>
      {/if}
    </div>

    {#each piece.volumes as tv, i (tv.id)}
      {@const v = tv.volume}
      {@const shown = card.volumes[i]}
      {@const on = new Set(declarationsOf(v.declarations))}
      <div class="group">
        <h3>{piece.volumes.length === 1 ? 'Volume' : `Volume ${i + 1}`} <span class="dim sub">{shown?.footprint} · {shown?.heights}</span></h3>
        <div class="grid2">
          <label class="full">Kind
            <select value={v.kind} onchange={(e) => edit(tv.id, withKind(e.currentTarget.value as 'solid' | 'area'))}>
              <option value="solid">Solid</option><option value="area">Area</option>
            </select>
          </label>
          {#if v.footprint.kind === 'circle'}
            <label>Radius <input type="number" min="0" max="144" step="0.25" value={v.footprint.a} onchange={(e) => edit(tv.id, withFootprint('a', num(e)))} /></label>
          {:else}
            <label>Width <input type="number" min="0" max="144" step="0.25" value={v.footprint.a} onchange={(e) => edit(tv.id, withFootprint('a', num(e)))} /></label>
          {/if}
          {#if v.footprint.kind === 'rect'}
            <label>Length <input type="number" min="0" max="144" step="0.25" value={v.footprint.b} onchange={(e) => edit(tv.id, withFootprint('b', num(e)))} /></label>
          {/if}
          <label>Bottom (inches) <input type="number" min="0" max="60" step="0.25" value={v.bottom} onchange={(e) => edit(tv.id, withBottom(num(e)))} /></label>
          <label>Top (inches) <input type="number" min="0" max="60" step="0.25" value={v.top} onchange={(e) => edit(tv.id, withTop(num(e)))} /></label>
        </div>
        <fieldset>
          <legend>Rules</legend>
          {#each declarationWords(terrainWords) as [name, word] (name)}
            <label class="check"><input type="checkbox" checked={on.has(name)} onchange={(e) => edit(tv.id, withDeclaration(name, e.currentTarget.checked))} />{word}</label>
          {/each}
        </fieldset>
        <label>Notes
          <input maxlength={NOTES} value={v.notes ?? ''} placeholder="Enter saves"
            onkeydown={(e) => { if (e.key === 'Enter') edit(tv.id, withNotes(e.currentTarget.value)); }} />
        </label>
      </div>
    {/each}

    <div class="actions-row">
      <button class="small" onclick={resetVolumes} title="Back to the catalog's or the asset's volumes">Reset volumes</button>
      <button class="small danger" onclick={() => send(removeTerrain(piece.id))}>Remove</button>
    </div>
  </section>
{:else if marker !== null}
  <section class="card" aria-label="Marker card">
    <header>
      <div class="title">
        <h2>{marker.label === null ? marker.name : `${marker.name}: ${marker.label}`}</h2>
        <span class="dim">{marker.diameter}</span>
      </div>
      <button class="icon-btn" aria-label="Close the card" title="Close" onclick={onClose}><Icon name="close" /></button>
    </header>
    <div class="actions-row">
      <button class="small danger" onclick={() => send(removeMarker(marker.pieceId))}>Remove marker</button>
    </div>
  </section>
{/if}

<style>
  .card { display: flex; flex-direction: column; }
  header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); padding: var(--space-3); border-bottom: 1px solid var(--rule); }
  .title { display: flex; flex-direction: column; gap: var(--space-1); min-width: 0; }
  h2 { margin: 0; font-size: 1.05rem; color: var(--text); }
  .title .dim { font-size: .9rem; }
  .group { display: flex; flex-direction: column; gap: var(--space-2); padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--rule); }
  h3 { margin: 0; font-size: .75rem; font-weight: 600; color: var(--dim); text-transform: uppercase; letter-spacing: .08em; font-family: var(--font-body); }
  :global([data-theme='fantasy']) h3 { font-family: var(--font-display); text-transform: none; letter-spacing: .03em; font-size: .85rem; color: var(--wood); }
  .sub { text-transform: none; letter-spacing: 0; font-weight: normal; margin-left: var(--space-1); }
  .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); }
  label { display: flex; flex-direction: column; gap: var(--space-1); color: var(--dim); font-size: .85rem; min-width: 0; }
  label input:not([type='checkbox']), label select { width: 100%; color: var(--text); font-size: 1rem; }
  .full { grid-column: 1 / -1; }
  input[type='number'] { width: 100%; }
  .pair { display: flex; gap: var(--space-2); }
  .pair > button { flex: 1; }
  .inline { display: flex; gap: var(--space-2); align-items: center; }
  .inline select { flex: 1; min-width: 0; }
  .left { align-self: flex-start; }
  fieldset { display: grid; grid-template-columns: 1fr; gap: var(--space-1); padding: var(--space-2) var(--space-2); }
  .check { flex-direction: row; align-items: center; gap: var(--space-2); color: var(--text); font-size: .95rem; margin: 0; }
  .actions-row { display: flex; gap: var(--space-2); justify-content: space-between; padding: var(--space-2) var(--space-3) var(--space-3); }
</style>
