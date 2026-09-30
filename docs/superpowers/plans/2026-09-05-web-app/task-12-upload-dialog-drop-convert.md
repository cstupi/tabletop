# Task 12: The upload dialog — drop, convert, preview, confirm

**Files:**
- Create: `web/src/lib/components/UploadDialog.svelte`, `web/src/lib/upload/intake.ts`
- Test: `web/tests/intake.test.ts`

**Interfaces:**
- Consumes: `convertInWorker`, `describeGlb`, `ConversionInput.upAxis`, `UpAxis`, `BUDGETS`, `terrainVolume`, `upload`, `MeshPreview` (with `onerror`), `BasePicker`, `circle`.
- Produces: `classifyFiles(files: { name: string; type: string }[]): Intake` where `Intake = { kind: 'stl'; files } | { kind: 'glb'; file } | { kind: 'image'; file; contentType: 'image/png' | 'image/jpeg' | 'image/webp' } | { kind: 'rejected'; reason: string }` (several `.stl` merge; exactly one `.glb`; one `.png`/`.jpg`; `.gltf` is rejected with the reason management gives); `UploadDialog` with props `open: boolean`, `onclose()`, `onuploaded(outcome: UploadOutcome)`.
- The dialog's states, in the spec's order: **drop** → **converting** (progress from the worker) → **preview** (kind, name, modelled base, scale, orientation, budget, before/after counts, the support warning) → **uploading** (stage) → **done** (management's `warning`, if any). Changing the budget re-runs conversion; changing kind switches the default budget. Kind, budget and *Stand up / lay flat* are disabled while uploading. Confirm is disabled until the preview has rendered, so nothing uploads before the user has seen it: `previewed` is set by `MeshPreview`'s `onmeasure`, which fires only once a scene has been parsed, and cleared by its `onerror`. Confirm is also disabled while scale or any orientation angle is not finite, or scale is not positive.
- The kind offered follows the file: a mesh is *Miniature* or *Terrain*, an image is *Board texture* and the select is fixed, because management answers `415` for any other pairing of kind and content type (management plan Task 7 → `AssetRules.ContentTypes`).
- **The terrain volume is computed at confirm**, from the mesh bounds the dialog holds and the scale and orientation the user confirmed — not from the converter's proposal, which the user may have corrected in the preview (mesh conversion → What it writes; What it does not do). `ConversionResult.volume` is the proposal-time default and the dialog does not use it.
- The proposal seeds scale and orientation only on a file's first conversion; a re-run for budget, kind or axis keeps the user's corrections. Each conversion carries a token, and a result that arrives after a newer run, or after `reset()`, is dropped. `onfiles` clears the previous file's result, GLB description, bounds and forced axis.
- *Stand up / lay flat* toggles pitch between 0 and 90. On an STL it also re-runs conversion with `upAxis` forced to the implied axis (`'y'` at pitch 90, `'z'` at 0), so the floor and the footprint centre follow the correction. A pitch typed into the field does not re-centre.
- `reset()` restores kind `miniature`, the miniature budget, a 32 mm round base, scale 1 and identity orientation, and bumps the conversion token, so an STL dropped after a texture is not created as `kind: 'texture'`.
- A GLB dropped straight in gets no proposal: scale defaults to 1, orientation to identity, and the user sets both (mesh conversion → What it writes). A texture skips the mesh preview and is uploaded as `kind: 'texture'` with no base and no volume. A GLB that cannot be read reports into `error` and stays on the drop stage.

Spec: mesh conversion → The flow (all four steps; "Nothing uploads before the user has seen it"; "The budget is adjustable here"), What conversion does (the support warning "says so in words and asks for the unsupported file"); management API → GLB only (`.gltf` with external buffers rejected); management plan Task 7 (`415` for other content types; miniature carries a `modelledBase`, terrain and textures do not).

- [ ] **Step 1: Write the failing test and `intake.ts`**

```ts
// web/tests/intake.test.ts
import { describe, expect, it } from 'vitest';
import { classifyFiles } from '../src/lib/upload/intake';
describe('classifyFiles', () => {
  it('merges STLs, takes one GLB or one image, and rejects the rest', () => {
    expect(classifyFiles([{ name: 'a.STL', type: '' }, { name: 'b.stl', type: '' }])).toMatchObject({ kind: 'stl' });
    expect(classifyFiles([{ name: 'm.glb', type: 'model/gltf-binary' }])).toMatchObject({ kind: 'glb' });
    expect(classifyFiles([{ name: 'board.jpeg', type: 'image/jpeg' }])).toMatchObject({ kind: 'image', contentType: 'image/jpeg' });
    expect(classifyFiles([{ name: 'board.webp', type: 'image/webp' }])).toMatchObject({ kind: 'image', contentType: 'image/webp' });
    expect(classifyFiles([{ name: 'm.gltf', type: '' }])).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('GLB') });
    expect(classifyFiles([{ name: 'a.stl', type: '' }, { name: 'm.glb', type: '' }])).toMatchObject({ kind: 'rejected' });
    expect(classifyFiles([{ name: 'a.glb', type: '' }, { name: 'b.glb', type: '' }])).toMatchObject({ kind: 'rejected' });
  });
});
```

```ts
// web/src/lib/upload/intake.ts
export type Intake<F extends { name: string } = File> =
  | { kind: 'stl'; files: F[] } | { kind: 'glb'; file: F } | { kind: 'image'; file: F; contentType: 'image/png' | 'image/jpeg' | 'image/webp' } | { kind: 'rejected'; reason: string };

export function classifyFiles<F extends { name: string }>(files: F[]): Intake<F> {
  const ext = (f: F) => f.name.toLowerCase().split('.').pop() ?? '';
  if (files.length === 0) return { kind: 'rejected', reason: 'Drop one or more .stl files, one .glb, or one .png/.jpg.' };
  if (files.every((f) => ext(f) === 'stl')) return { kind: 'stl', files };
  if (files.length === 1 && ext(files[0]!) === 'glb') return { kind: 'glb', file: files[0]! };
  const images: Record<string, 'image/png' | 'image/jpeg' | 'image/webp'> = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp' };
  if (files.length === 1 && images[ext(files[0]!)]) return { kind: 'image', file: files[0]!, contentType: images[ext(files[0]!)]! };
  if (files.some((f) => ext(f) === 'gltf')) return { kind: 'rejected', reason: 'A .gltf with external buffers is several files; export a single .glb (GLB) instead.' };
  return { kind: 'rejected', reason: 'Mix not understood: drop STLs together, or one GLB, or one image.' };
}
```

- [ ] **Step 2: Run; expect 1 failure then pass. Write `UploadDialog.svelte`:**

```svelte
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
  const numbersOk = $derived(kind === 'texture' || ([scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite) && scale > 0));
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
```

- [ ] **Step 3: `npm test && npm run check`; expect 52 passed, 0 errors. The dialog has no route until Task 13 creates `/library`, so its first browser run is Task 13's. Commit: `Add the upload dialog: drop, convert in a worker, preview on the base ring, confirm`.**
