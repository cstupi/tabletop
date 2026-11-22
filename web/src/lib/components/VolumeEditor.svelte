<script lang="ts">
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Orientation, Volume } from '$lib/api/types';
  import MeshPreview, { type Measure } from './MeshPreview.svelte';
  import { fromDraft, screenToTable, toDraft, validate, type VolumeDraft } from '$lib/library/volume-edit';
  import { DECLARATIONS, DECLARATION_LABELS, defaultVolume } from '$lib/units/volumes';
  import { describeGlb } from '$lib/mesh/convert';
  import { terrainVolume } from '$lib/mesh/bounds';

  let { assetId, glb, scale, orientation }: { assetId: string; glb: Uint8Array; scale: number; orientation: Orientation } = $props();
  let drafts = $state<VolumeDraft[]>([]); let selected = $state(0); let error = $state<string | null>(null); let saved = $state(false);
  let extent = $state<Measure>({ width: 1, length: 1, height: 1, frame: { minX: -1, maxX: 1, minY: -1, maxY: 1 } });
  const volumes = $derived(drafts.map(fromDraft));
  const errors = $derived(drafts.flatMap((d, i) => validate(d).map((e) => `Volume ${i + 1}: ${e}`)));

  $effect(() => { api.assets.getVolumes(assetId).then((rows) => (drafts = rows.map((r) => toDraft(r.volume)))).catch((e) => (error = errorMessage(e))); });

  const add = () => { drafts = [...drafts, toDraft(defaultVolume(extent.width, extent.length, extent.height))]; selected = drafts.length - 1; };
  const duplicate = () => { if (drafts[selected]) { drafts = [...drafts, structuredClone($state.snapshot(drafts[selected]!))]; selected = drafts.length - 1; } };
  const remove = () => { drafts = drafts.filter((_, i) => i !== selected); selected = Math.max(0, selected - 1); };
  const reset = async () => { try { const d = await describeGlb(glb); drafts = [toDraft(terrainVolume(d.bounds, { scale, orientation }))]; selected = 0; } catch (e) { error = e instanceof Error ? e.message : String(e); } };
  let busy = $state(false);
  const save = () => { busy = true; error = null; saved = false; api.assets.putVolumes(assetId, volumes).then(() => (saved = true)).catch((e) => (error = errorMessage(e))).finally(() => (busy = false)); };
  const toggle = (d: VolumeDraft, name: (typeof DECLARATIONS)[number], on: boolean) => { const s = new Set(d.declarations); on ? s.add(name) : s.delete(name); d.declarations = s; };

  // drag the selected footprint: MeshPreview reports the table rectangle its top-down camera frames
  let dragging = false;
  function tableAt(e: PointerEvent) {
    const box = (e.currentTarget as HTMLElement).getBoundingClientRect();
    return screenToTable(e.clientX - box.left, e.clientY - box.top, { width: box.width, height: box.height }, extent.frame);
  }
  function move(e: PointerEvent) { if (!dragging || !drafts[selected]) return; const p = tableAt(e); drafts[selected]!.offsetX = +p.x.toFixed(2); drafts[selected]!.offsetY = +p.y.toFixed(2); }
</script>

<section class="panel editor">
  <h2>Collision volumes</h2>
  <p>Gameplay runs on these prisms, not on the mesh. Orange is solid (stood on), green is area (stood inside). Drag the selected outline to move it.</p>
  <div class="top" role="application" aria-label="Top view: drag to move the selected volume" onpointerdown={() => (dragging = true)} onpointerup={() => (dragging = false)} onpointerleave={() => (dragging = false)} onpointermove={move}>
    <MeshPreview {glb} {scale} {orientation} base={null} mode="top" {volumes} highlight={selected} onmeasure={(e) => (extent = e)} />
  </div>
  <div class="bar"><button onclick={add}>Add volume</button><button onclick={duplicate} disabled={!drafts[selected]}>Duplicate</button><button onclick={remove} disabled={!drafts[selected]}>Remove</button><button onclick={reset}>Reset to bounding box</button></div>
  <div class="volumes">
    {#each drafts as d, i}
      <div class:selected={i === selected} role="button" tabindex="0" onkeydown={(e) => { if (e.target !== e.currentTarget) return; if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selected = i; } }} onclick={() => (selected = i)}>
        <select bind:value={d.kind} aria-label="Volume {i + 1} kind" onchange={() => (saved = false)}><option value="solid">solid</option><option value="area">area</option></select>
        <select bind:value={d.footprintKind} aria-label="Volume {i + 1} footprint"><option value="rect">rectangle</option><option value="circle">circle</option></select>
        {#if d.footprintKind === 'circle'}<label>radius <input type="number" step="0.25" bind:value={d.a} /></label>
        {:else}<label>width <input type="number" step="0.25" bind:value={d.a} /></label><label>length <input type="number" step="0.25" bind:value={d.b} /></label>{/if}
        <label>offset x <input type="number" step="0.25" bind:value={d.offsetX} /></label><label>y <input type="number" step="0.25" bind:value={d.offsetY} /></label>
        <label>yaw <input type="number" step="15" bind:value={d.yawDegrees} /></label>
        <label>bottom <input type="number" step="0.25" bind:value={d.bottom} /></label><label>top <input type="number" step="0.25" bind:value={d.top} /></label>
        <fieldset><legend>Declares</legend>{#each DECLARATIONS as name}<label><input type="checkbox" checked={d.declarations.has(name)} onchange={(e) => toggle(d, name, e.currentTarget.checked)} /> {DECLARATION_LABELS[name]}</label>{/each}</fieldset>
        <label>Notes <input bind:value={d.notes} maxlength="2000" placeholder="difficult when moving up, …" /></label>
      </div>
    {/each}
  </div>
  {#if drafts.length === 0}<p>No volumes yet. Add one, or reset to the bounding box.</p>{/if}
  {#each errors as e}<p role="alert">{e}</p>{/each}
  <div class="bar"><button class="primary" onclick={save} disabled={busy || errors.length > 0 || drafts.length === 0}>{busy ? 'Saving…' : 'Save volumes'}</button><span class="saved" role="status">{#if saved}Saved.{/if}</span></div>
  {#if error}<p role="alert">{error}</p>{/if}
</section>
<style>
  .top { height: 24rem; touch-action: none; background: var(--view-bg); margin: var(--space-3) 0 }
  .selected { outline: 2px solid var(--accent); outline-offset: -2px; background: var(--rule) }
  .bar { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: center; margin: var(--space-3) 0 }
  .volumes > div { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-3); align-items: center; padding: var(--space-3); border-bottom: 1px solid var(--rule) }
  .volumes label { display: inline-flex; align-items: center; gap: var(--space-1) }
</style>
