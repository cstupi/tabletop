<script lang="ts">
  import type { AssetKind, Base, Orientation } from '$lib/api/types';
  import MeshPreview from './MeshPreview.svelte';
  import BasePicker from './BasePicker.svelte';
  import { BUDGETS } from '$lib/mesh/decimate';
  import { convertInWorker, describeGlb, type ConversionResult, type Progress } from '$lib/mesh/convert';
  import type { Bounds } from '$lib/mesh/types';
  import type { UpAxis } from '$lib/mesh/guess';
  import { terrainVolume } from '$lib/mesh/bounds';
  import { classifyFiles, type Intake } from '$lib/upload/intake';
  import { upload, type UploadOutcome, type UploadStage } from '$lib/upload/upload';
  import { circle } from '$lib/units/bases';

  let { open, onclose, onuploaded }: { open: boolean; onclose: () => void; onuploaded: (o: UploadOutcome) => void } = $props();

  type Stage = 'drop' | 'converting' | 'preview' | 'uploading' | 'done';
  let stage = $state<Stage>('drop');
  let intake = $state<Intake | null>(null);
  let progress = $state<string>(''); let error = $state<string | null>(null);
  let kind = $state<AssetKind>('miniature'); let name = $state('');
  let budget = $state<number>(BUDGETS.miniature);
  let result = $state<ConversionResult | null>(null);       // STL path
  let glb = $state<Uint8Array | null>(null); let glbInfo = $state<{ triangles: number } | null>(null);
  let scale = $state(1); let orientation = $state<Orientation>({ pitch: 0, yaw: 0, roll: 0 });
  let base = $state<Base | null>(circle(32));
  let meshBounds = $state<Bounds | null>(null);   // mesh-space bounds; the volume is derived from these at confirm, with the confirmed scale and orientation
  let previewed = $state(false); let outcome = $state<UploadOutcome | null>(null);
  let convToken = 0; let seeded = $state(false);
  let forcedAxis = $state<UpAxis | null>(null);   // set by Stand up / lay flat on an STL, so the re-conversion re-centres on the axis the user chose

  async function onfiles(list: FileList | null) {
    error = null; result = null; glbInfo = null; meshBounds = null; forcedAxis = null;
    intake = classifyFiles(Array.from(list ?? []));
    if (intake.kind === 'rejected') { error = intake.reason; return; }
    name = intake.kind === 'stl' ? intake.files[0]!.name.replace(/\.stl$/i, '') : intake.file.name.replace(/\.[^.]+$/, '');
    if (intake.kind === 'image') { kind = 'texture'; base = null; stage = 'preview'; previewed = true; return; }
    if (intake.kind === 'glb') {
      try {
        glb = new Uint8Array(await intake.file.arrayBuffer());
        const d = await describeGlb(glb); glbInfo = { triangles: d.triangles }; meshBounds = d.bounds;
        scale = 1; orientation = { pitch: 0, yaw: 0, roll: 0 };   // a dropped GLB gets no proposal; the user sets both
        seeded = true; stage = 'preview'; return;
      } catch (e) { error = e instanceof Error ? e.message : String(e); return; }
    }
    seeded = false; await runConversion();
  }
  async function runConversion() {
    if (intake?.kind !== 'stl') return;
    stage = 'converting'; previewed = false;
    const t = ++convToken;
    try {
      const files = await Promise.all(intake.files.map(async (f) => ({ name: f.name, bytes: await f.arrayBuffer() })));
      const res = await convertInWorker({ kind: kind === 'terrain' ? 'terrain' : 'miniature', budget, files, ...(forcedAxis ? { upAxis: forcedAxis } : {}) }, (p: Progress) => (progress = p.step));
      if (t !== convToken) return;
      result = res; glb = res.glb; meshBounds = res.bounds;
      if (!seeded) { scale = res.proposal.scale; orientation = res.proposal.orientation; seeded = true; }
      stage = 'preview';
    } catch (e) { if (t === convToken) { error = e instanceof Error ? e.message : String(e); stage = 'drop'; } }
  }
  const numbersOk = $derived(kind === 'texture' || [scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite));
  function standUp() {
    const pitch = orientation.pitch === 90 ? 0 : 90;
    orientation = { ...orientation, pitch };
    if (intake?.kind === 'stl') { forcedAxis = pitch === 90 ? 'y' : 'z'; void runConversion(); }   // a GLB has no conversion: only the pitch changes
  }
  function setKind(k: 'miniature' | 'terrain') {
    kind = k;
    if (k === 'terrain') base = null; else if (base === null) base = circle(32);
    const next = BUDGETS[k]; if (next !== budget) { budget = next; void runConversion(); }
  }
  async function confirm() {
    if (!intake || intake.kind === 'rejected') return;
    stage = 'uploading'; error = null;
    try {
      const bytes = intake.kind === 'image' ? new Uint8Array(await intake.file.arrayBuffer()) : glb!;
      const contentType = intake.kind === 'image' ? intake.contentType : 'model/gltf-binary';
      // The bounding-box volume is computed here, from the scale and orientation the user confirmed — not from the proposal.
      const volume = kind === 'terrain' && meshBounds ? terrainVolume(meshBounds, { scale, orientation }) : undefined;
      outcome = await upload({ name: name.trim() || 'Untitled', kind, contentType, bytes, scale, orientation, modelledBase: kind === 'miniature' ? base : null, volume }, (s: UploadStage) => (progress = s));
      stage = 'done'; onuploaded(outcome);
    } catch (e) { error = e instanceof Error ? e.message : String(e); stage = 'preview'; }
  }
  function reset() { ++convToken; stage = 'drop'; intake = null; result = null; glb = null; glbInfo = null; meshBounds = null; outcome = null; error = null; previewed = false; kind = 'miniature'; budget = BUDGETS.miniature; base = circle(32); scale = 1; orientation = { pitch: 0, yaw: 0, roll: 0 }; }
</script>

{#if open}
<dialog open>
  <header><h2>Add to library</h2><button onclick={() => { reset(); onclose(); }}>Close</button></header>

  {#if stage === 'drop'}
    <label class="drop" ondragover={(e) => e.preventDefault()} ondrop={(e) => { e.preventDefault(); void onfiles(e.dataTransfer?.files ?? null); }}>
      Drop one or more <code>.stl</code> (a kit's parts merge as placed), one <code>.glb</code>, or a <code>.png</code>/<code>.jpg</code> board texture — or
      <input type="file" multiple accept=".stl,.glb,.png,.jpg,.jpeg,.webp" onchange={(e) => void onfiles(e.currentTarget.files)} />
    </label>
  {:else if stage === 'converting'}
    <p>Converting… {progress}</p>
  {:else if stage === 'preview' || stage === 'uploading'}
    <div class="preview">
      {#if kind !== 'texture'}
        <MeshPreview {glb} {scale} {orientation} {base} onmeasure={() => (previewed = true)} onerror={(m) => { error = m; previewed = false; }} />
      {:else}<p>Texture: {name}</p>{/if}
      <aside>
        <label>Name <input bind:value={name} /></label>
        <!-- the kinds this file can be: a mesh is a miniature or terrain, an image is a board texture. Management answers 415 for any other pairing. -->
        <label>Kind <select value={kind} onchange={(e) => setKind(e.currentTarget.value as 'miniature' | 'terrain')} disabled={intake?.kind === 'image' || stage === 'uploading'}>
          {#if intake?.kind === 'image'}<option value="texture">Board texture</option>
          {:else}<option value="miniature">Miniature</option><option value="terrain">Terrain</option>{/if}
        </select></label>
        {#if kind === 'miniature'}<label>Modelled base <BasePicker bind:value={base} /></label>{/if}
        {#if kind !== 'texture'}
          <fieldset><legend>Scale</legend>
            <input type="number" step="any" bind:value={scale} /> ×
            <button onclick={() => (scale = 1 / 25.4)}>mm</button><button onclick={() => (scale = 1 / 2.54)}>cm</button><button onclick={() => (scale = 1)}>inches</button>
          </fieldset>
          <fieldset><legend>Orientation (degrees)</legend>
            <label>Pitch <input type="number" step="15" bind:value={orientation.pitch} /></label>
            <label>Roll <input type="number" step="15" bind:value={orientation.roll} /></label>
            <label>Yaw (facing) <input type="number" step="15" bind:value={orientation.yaw} /></label>
            <button onclick={standUp} disabled={stage === 'uploading'}>Stand up / lay flat</button>
          </fieldset>
        {/if}
        {#if result}
          <p>{result.trianglesBefore.toLocaleString()} → {result.trianglesAfter.toLocaleString()} triangles</p>
          <label>Budget <input type="number" step="1000" min="1000" bind:value={budget} onchange={() => void runConversion()} disabled={stage === 'uploading'} /></label>
          <p>Guessed millimetres, {result.proposal.upAxis.toUpperCase()} up. Check the model stands on the ring and faces the arrow.</p>
          {#if result.supports.suspected}
            <p role="alert">This file looks pre-supported: {result.supports.supportLike} thin, tall pieces stand under it. Supports are never removed here — please drop the unsupported file instead.</p>
          {/if}
        {:else if glbInfo}<p>{glbInfo.triangles.toLocaleString()} triangles. No conversion ran, so set the scale and orientation yourself.</p>{/if}
        {#if stage === 'uploading'}<p>Uploading… {progress}</p>{/if}
        {#if !numbersOk}<p role="alert">Scale and orientation must be numbers.</p>{/if}
        <button onclick={confirm} disabled={stage === 'uploading' || !numbersOk || (kind !== 'texture' && !previewed)}>Confirm and upload</button>
      </aside>
    </div>
  {:else if stage === 'done' && outcome}
    <p>Added <strong>{outcome.asset.name}</strong>{#if outcome.deduplicated} (these bytes were already in your library){/if}{#if outcome.triangles !== undefined}, {outcome.triangles.toLocaleString()} triangles{/if}.</p>
    {#if outcome.warning}<p role="alert">{outcome.warning}</p>{/if}
    <button onclick={reset}>Add another</button><button onclick={() => { reset(); onclose(); }}>Done</button>
  {/if}
  {#if error}<p role="alert">{error}</p>{/if}
</dialog>
{/if}

<style>
  dialog { width: min(64rem, 95vw); } .preview { display: grid; grid-template-columns: 2fr 1fr; gap: 1rem; min-height: 24rem; }
  .drop { display: block; border: 2px dashed currentColor; padding: 3rem; text-align: center; }
</style>
