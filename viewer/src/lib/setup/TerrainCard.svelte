<!-- viewer/src/lib/setup/TerrainCard.svelte -->
<!-- The selected terrain piece or setup marker (TerrainCard.cs in setup): name, source, height, and every edit of the piece. Every volume
     field is one OverrideTerrainVolumes carrying the placement's whole volume list with that volume changed. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { AssetDto } from '../api/types';
  import type { Geometry } from '../geometry';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import { type Catalog, volumesOfBuiltin } from './catalog';
  import {
    SCALE_MAX, SCALE_MIN, SCALE_STEP, TERRAIN_TURN, overrideVolume, turnInDraft, withBottom, withDeclaration, withFootprint, withKind,
    withNotes, withTop,
  } from './edits';
  import { NOTES } from './limits';
  import { DECLARATION_WORDS, markerCardOf, terrainCardOf } from './terrainCard';
  import { declarationsOf } from '../live/project';

  let { draft, catalog, geometry, api, selected, send, report }: {
    draft: TableState; catalog: Catalog; geometry: Geometry; api: Api; selected: string | null;
    send: (edit: Edit) => Promise<void>; report: (text: string | null) => void;
  } = $props();

  const card = $derived(terrainCardOf(draft, 'setup', selected, catalog));
  const marker = $derived(markerCardOf(draft, 'setup', selected));
  const piece = $derived(card === null ? undefined : draft.terrain.find((t) => t.id === card.pieceId));
  const names = $derived([...catalog.terrain.keys()].sort());

  let swap = $state('');
  $effect(() => { swap = piece?.mesh.builtin !== undefined && names.includes(piece.mesh.builtin) ? piece.mesh.builtin : names[0] ?? ''; });
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
      if (list.length === 0) { report('No terrain in your library; upload some in the web app.'); return; }
      assets = list;
      asset = list[0]!.id;
    } catch (e) { report((e as Error).message); }
  }

  async function resetVolumes() {
    if (piece === undefined) return;
    try {
      const volumes = piece.mesh.builtin !== undefined
        ? volumesOfBuiltin(catalog.terrain.get(piece.mesh.builtin) ?? (() => { throw new Error('That built-in is not in the catalog.'); })())
        : (await api.assetVolumes(piece.mesh.assetId!)).map((v) => ({ id: crypto.randomUUID(), volume: v.volume }));
      await send({ kind: 'override_terrain_volumes', placementId: piece.id, volumes });
    } catch (e) { report((e as Error).message); }
  }

  const edit = (volumeId: string, change: Parameters<typeof overrideVolume>[2]) => {
    if (piece !== undefined) void send(overrideVolume(piece, volumeId, change));
  };
  const num = (e: Event) => Number((e.currentTarget as HTMLInputElement).value);
</script>

{#if card !== null && piece !== undefined}
  <section class="card" aria-label="Terrain card">
    <p><strong>{card.name}</strong></p>
    <p class="muted">{card.source}</p>
    {#if card.height !== ''}<p>Height {card.height}</p>{/if}

    <div class="flow">
      <span>Facing</span>
      <input type="number" min="0" max="359" step="1" value={card.facing} title="Degrees counter-clockwise from above" onchange={(e) => turnTo(num(e))} />°
      <button onclick={() => turnTo(piece.yaw.degrees + TERRAIN_TURN)}>⟲ {TERRAIN_TURN}°</button>
      <button onclick={() => turnTo(piece.yaw.degrees - TERRAIN_TURN)}>⟳ {TERRAIN_TURN}°</button>
    </div>
    <div class="flow">
      <span>Scale</span>
      ×<input type="number" min={SCALE_MIN} max={SCALE_MAX} step={SCALE_STEP} value={piece.scale}
        onchange={(e) => send({ kind: 'scale_terrain', placementId: piece.id, scale: num(e) })} />
    </div>
    <div class="flow">
      <select aria-label="Mesh" bind:value={swap}>{#each names as n (n)}<option value={n}>{n}</option>{/each}</select>
      <button onclick={() => send({ kind: 'swap_terrain_mesh', placementId: piece.id, mesh: { builtin: swap } })}>Swap mesh (keeps volumes)</button>
    </div>
    <div class="flow">
      <button onclick={swapForAsset}>Swap for my asset…</button>
      <button onclick={resetVolumes}>Reset volumes</button>
      <button onclick={() => send({ kind: 'remove_terrain', placementId: piece.id })}>Remove</button>
    </div>
    {#if assets !== null}
      <div class="flow">
        <select aria-label="My terrain" bind:value={asset}>{#each assets as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</select>
        <button onclick={() => { assets = null; void send({ kind: 'swap_terrain_mesh', placementId: piece.id, mesh: { assetId: asset } }); }}>Swap</button>
        <button onclick={() => (assets = null)}>Cancel</button>
      </div>
    {/if}

    {#each piece.volumes as tv, i (tv.id)}
      {@const v = tv.volume}
      {@const shown = card.volumes[i]}
      {@const on = new Set(declarationsOf(v.declarations))}
      <hr />
      <p>Volume {i + 1} · {shown?.kind} · {shown?.footprint} · {shown?.heights}</p>
      <div class="flow">
        <select aria-label="Kind" value={v.kind} onchange={(e) => edit(tv.id, withKind(e.currentTarget.value as 'solid' | 'area'))}>
          <option value="solid">Solid</option><option value="area">Area</option>
        </select>
        <span>{v.footprint.kind === 'circle' ? 'radius' : 'w×l'}</span>
        <input type="number" min="0" max="144" step="0.25" value={v.footprint.a} onchange={(e) => edit(tv.id, withFootprint('a', num(e)))} />
        {#if v.footprint.kind === 'rect'}
          <input type="number" min="0" max="144" step="0.25" value={v.footprint.b} onchange={(e) => edit(tv.id, withFootprint('b', num(e)))} />
        {/if}
      </div>
      <div class="flow">
        <span>from</span><input type="number" min="0" max="60" step="0.25" value={v.bottom} onchange={(e) => edit(tv.id, withBottom(num(e)))} />
        <span>to</span><input type="number" min="0" max="60" step="0.25" value={v.top} onchange={(e) => edit(tv.id, withTop(num(e)))} />
      </div>
      <div class="flow">
        {#each DECLARATION_WORDS as [name, word] (name)}
          <label><input type="checkbox" checked={on.has(name)} onchange={(e) => edit(tv.id, withDeclaration(name, e.currentTarget.checked))} /> {word}</label>
        {/each}
      </div>
      <input class="notes" maxlength={NOTES} value={v.notes ?? ''} placeholder={`notes, Enter to save (at most ${NOTES} characters)`}
        onkeydown={(e) => { if (e.key === 'Enter') edit(tv.id, withNotes(e.currentTarget.value)); }} />
    {/each}
  </section>
{:else if marker !== null}
  <section class="card" aria-label="Marker card">
    <p><strong>{marker.label === null ? marker.name : `${marker.name} · ${marker.label}`}</strong></p>
    <p>{marker.diameter}</p>
    <button onclick={() => send({ kind: 'remove_setup_marker', markerId: marker.pieceId })}>Remove marker</button>
  </section>
{/if}

<style>
  .card { border: 1px solid var(--line); padding: 0.4rem; margin-bottom: 0.6rem; background: var(--panel); }
  p { margin: 0.15rem 0; }
  .flow { display: flex; flex-wrap: wrap; gap: 0.3rem; align-items: center; margin: 0.3rem 0; }
  input[type='number'] { width: 4.5rem; }
  .notes { width: 100%; }
  hr { border: 0; border-top: 1px solid var(--line); }
  label { display: inline-flex; gap: 0.2rem; align-items: center; }
</style>
