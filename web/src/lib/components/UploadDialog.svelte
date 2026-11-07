<script lang="ts">
  import type { AssetKind, Base, FolderDto, Guid, Orientation } from '$lib/api/types';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import { folderOptions } from '$lib/library/bulk';
  import MeshPreview from './MeshPreview.svelte';
  import BasePicker from './BasePicker.svelte';
  import Icon from './Icon.svelte';
  import ScaleOrientation from './ScaleOrientation.svelte';
  import { BUDGETS } from '$lib/mesh/decimate';
  import { convertInWorker, describeGlb, STEPS, type ConversionResult, type Progress } from '$lib/mesh/convert';
  import type { Bounds } from '$lib/mesh/types';
  import type { UpAxis } from '$lib/mesh/guess';
  import { terrainVolume } from '$lib/mesh/bounds';
  import { classifyFiles, type Intake } from '$lib/upload/intake';
  import { STAGE_LABELS, stepStates, upload, type UploadOutcome, type UploadStage } from '$lib/upload/upload';
  import { circle } from '$lib/units/bases';
  import { GLB_DEFAULTS } from '$lib/mesh/size';
  import { formatBytes, MAX_NAME, nameProblem, sizeProblem } from '$lib/validation';
  import { sendThumbnail } from '$lib/thumbnail/render';

  /** `folderId`: the folder selected in the Library when the dialog opens; each upload is filed there unless the user picks another or none. */
  let { open, onclose, onuploaded, folders = [], folderId = null }: { open: boolean; onclose: () => void; onuploaded: (o: UploadOutcome) => void; folders?: FolderDto[]; folderId?: Guid | null } = $props();

  type Stage = 'drop' | 'converting' | 'preview' | 'uploading' | 'done';
  let stage = $state<Stage>('drop');
  let intake = $state<Intake | null>(null);
  let step = $state<Progress['step'] | null>(null);
  let uploadStage = $state<UploadStage | null>(null); let sent = $state<{ sent: number; total: number } | null>(null);
  let error = $state<string | null>(null);
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
  let target = $state<Guid | ''>(''); let filed = $state<string | null>(null); let fileError = $state<string | null>(null);
  const targets = $derived(folderOptions(folders));
  let dialog = $state<HTMLDialogElement>(); let heading = $state<HTMLHeadingElement>(); let dragOver = $state(false);

  // A real modal: focus stays inside, Escape closes, the page behind is inert.
  $effect(() => { if (open && dialog && !dialog.open) { target = folderId ?? ''; dialog.showModal(); heading?.focus(); } });

  const stepIndex = $derived(step ? STEPS.findIndex((s) => s.step === step) + 1 : 0);
  const stepLabel = $derived(step ? STEPS.find((s) => s.step === step)!.label : 'Starting');
  const nameError = $derived(nameProblem(name));
  /** The finished file against management's per-kind cap: an image as dropped, a mesh after conversion. */
  const bytes = $derived(intake?.kind === 'image' ? intake.file.size : glb?.byteLength ?? 0);
  const tooBig = $derived(intake && intake.kind !== 'rejected' ? sizeProblem(kind, bytes) : null);

  async function onfiles(list: FileList | null) {
    error = null; result = null; glbInfo = null; meshBounds = null; forcedAxis = null;
    intake = classifyFiles(Array.from(list ?? []));
    if (intake.kind === 'rejected') { error = intake.reason; return; }
    name = (intake.kind === 'stl' ? intake.files[0]!.name.replace(/\.stl$/i, '') : intake.file.name.replace(/\.[^.]+$/, '')).slice(0, MAX_NAME);
    if (intake.kind === 'image') {
      const big = sizeProblem('texture', intake.file.size);
      if (big) { error = big; intake = null; return; }
      kind = 'texture'; base = null; stage = 'preview'; previewed = true; return;
    }
    if (intake.kind === 'glb') {
      const big = sizeProblem('miniature', intake.file.size);
      if (big) { error = big; intake = null; return; }
      try {
        glb = new Uint8Array(await intake.file.arrayBuffer());
        const d = await describeGlb(glb); glbInfo = { triangles: d.triangles }; meshBounds = d.bounds;
        scale = GLB_DEFAULTS.scale; orientation = { ...GLB_DEFAULTS.orientation };   // glTF: metres, Y-up, front +Z
        seeded = true; stage = 'preview'; return;
      } catch (e) { error = `That GLB could not be read: ${e instanceof Error ? e.message : String(e)}. Export it again as a binary glTF (.glb).`; intake = null; return; }
    }
    seeded = false; await runConversion();
  }
  async function runConversion() {
    if (intake?.kind !== 'stl') return;
    stage = 'converting'; previewed = false; step = null;
    const t = ++convToken;
    try {
      const files = await Promise.all(intake.files.map(async (f) => ({ name: f.name, bytes: await f.arrayBuffer() })));
      const res = await convertInWorker({ kind: kind === 'terrain' ? 'terrain' : 'miniature', budget, files, ...(forcedAxis ? { upAxis: forcedAxis } : {}) }, (p: Progress) => (step = p.step));
      if (t !== convToken) return;
      result = res; glb = res.glb; meshBounds = res.bounds;
      if (!seeded) { scale = res.proposal.scale; orientation = res.proposal.orientation; seeded = true; }
      stage = 'preview';
    } catch (e) { if (t === convToken) { error = `The STL could not be converted: ${e instanceof Error ? e.message : String(e)}. Check it opens in your slicer, then try again.`; stage = 'drop'; } }
  }
  function numbersOkFor(s: number, o: Orientation) { return [s, o.pitch, o.roll, o.yaw].every(Number.isFinite) && s > 0; }
  const numbersOk = $derived(kind === 'texture' || numbersOkFor(scale, orientation));
  function standUp() {
    const pitch = orientation.pitch === 90 ? 0 : 90;   // only an STL gets here; the shared component toggles a GLB's pitch itself
    orientation = { ...orientation, pitch };
    forcedAxis = pitch === 90 ? 'y' : 'z'; void runConversion();
  }
  function setKind(k: 'miniature' | 'terrain') {
    kind = k;
    if (k === 'terrain') base = null; else if (base === null) base = circle(32);
    const next = BUDGETS[k]; if (next !== budget) { budget = next; void runConversion(); }
  }
  async function confirm(e: SubmitEvent) {
    e.preventDefault();
    if (!intake || intake.kind === 'rejected' || nameError || tooBig) return;
    stage = 'uploading'; error = null; sent = null;
    try {
      const bytes = intake.kind === 'image' ? new Uint8Array(await intake.file.arrayBuffer()) : glb!;
      const contentType = intake.kind === 'image' ? intake.contentType : 'model/gltf-binary';
      // The bounding-box volume is computed here, from the scale and orientation the user confirmed — not from the proposal.
      const volume = kind === 'terrain' && meshBounds ? terrainVolume(meshBounds, { scale, orientation }) : undefined;
      outcome = await upload({ name: name.trim(), kind, contentType, bytes, scale, orientation, modelledBase: kind === 'miniature' ? base : null, volume },
        (s: UploadStage) => (uploadStage = s), (a, total) => (sent = { sent: a, total }));
      // the library tile's picture, from the bytes just sent, before the dialog says done
      if (contentType === 'model/gltf-binary') await sendThumbnail(outcome.asset.id, bytes, scale, orientation);
      filed = null; fileError = null;
      const into = target ? targets.find((t) => t.id === target) : undefined;
      if (into) {
        // The asset is kept whether or not filing works; a failure says so and leaves it unfiled.
        try { await api.folders.addAsset(into.id, outcome.asset.id); filed = into.label; }
        catch (err) { fileError = `It was uploaded but could not be added to ${into.label}: ${errorMessage(err)} Add it from the asset's page.`; }
      }
      stage = 'done'; onuploaded(outcome);
    } catch (err) { error = err instanceof Error ? err.message : String(err); stage = 'preview'; }
  }
  function reset() { ++convToken; stage = 'drop'; intake = null; result = null; glb = null; glbInfo = null; meshBounds = null; outcome = null; filed = null; fileError = null; error = null; previewed = false; kind = 'miniature'; budget = BUDGETS.miniature; base = circle(32); scale = 1; orientation = { pitch: 0, yaw: 0, roll: 0 }; step = null; uploadStage = null; sent = null; }
  /** Every way out (Close, Done, Escape, Open it) closes the element; its close event resets and tells the page. */
  const close = () => dialog?.close();
  function onDialogClose() { reset(); onclose(); }
  // Escape during an upload would hide a request still running; the dialog stays until it finishes.
  function oncancel(e: Event) { if (stage === 'uploading') e.preventDefault(); }
</script>

{#if open}
<dialog bind:this={dialog} aria-labelledby="upload-heading" {oncancel} onclose={onDialogClose}>
  <header class="head"><h2 id="upload-heading" tabindex="-1" bind:this={heading}><Icon name="upload" />Add to library</h2><button onclick={close} disabled={stage === 'uploading'}><Icon name="close" size="1em" />Close</button></header>

  {#if stage === 'drop'}
    <label class="drop" class:over={dragOver} ondragenter={() => (dragOver = true)} ondragleave={() => (dragOver = false)} ondragover={(e) => e.preventDefault()} ondrop={(e) => { e.preventDefault(); dragOver = false; void onfiles(e.dataTransfer?.files ?? null); }}>
      <Icon name="upload" size="2.5rem" />
      <span>Drop one or more <code>.stl</code> (a kit's parts merge as placed), one <code>.glb</code>, or a <code>.png</code>/<code>.jpg</code>/<code>.webp</code> board texture here, or choose files:</span>
      <input type="file" multiple accept=".stl,.glb,.png,.jpg,.jpeg,.webp" onchange={(e) => void onfiles(e.currentTarget.files)} />
      <small>A mesh is at most 64 MB after conversion, a texture at most 16 MB.</small>
    </label>
  {:else if stage === 'converting'}
    <div role="status">
      <p class="loading">Converting in your browser: step {Math.max(stepIndex, 1)} of {STEPS.length}, {stepLabel.toLowerCase()}…</p>
      <progress max={STEPS.length} value={stepIndex}></progress>
      <p><small>Nothing is uploaded until you confirm.</small></p>
    </div>
  {:else if stage === 'preview' || stage === 'uploading'}
    <div class="preview">
      {#if kind !== 'texture'}
        <div class="view panel"><MeshPreview {glb} {scale} {orientation} {base} onmeasure={() => (previewed = true)} onerror={(m) => { error = `The preview could not show this mesh: ${m}`; previewed = false; }} /></div>
      {:else}<p>Texture: {name} ({formatBytes(bytes)})</p>{/if}
      <form class="stack" onsubmit={confirm}>
        <label>Name <input bind:value={name} maxlength={MAX_NAME} disabled={stage === 'uploading'} aria-invalid={!!nameError} aria-describedby={nameError ? 'upload-name-error' : undefined} /></label>
        {#if nameError}<p id="upload-name-error"><small>{nameError}</small></p>{/if}
        <label>Adds to <select bind:value={target} disabled={stage === 'uploading'}>
          <option value="">No folder</option>
          {#each targets as t (t.id)}<option value={t.id}>{t.label}</option>{/each}
        </select></label>
        <!-- the kinds this file can be: a mesh is a miniature or terrain, an image is a board texture. Management answers 415 for any other pairing. -->
        <label>Kind <select value={kind} onchange={(e) => setKind(e.currentTarget.value as 'miniature' | 'terrain')} disabled={intake?.kind === 'image' || stage === 'uploading'}>
          {#if intake?.kind === 'image'}<option value="texture">Board texture</option>
          {:else}<option value="miniature">Miniature</option><option value="terrain">Terrain</option>{/if}
        </select></label>
        {#if kind === 'miniature'}<label>Modelled base <BasePicker bind:value={base} label="Modelled base" /></label>{/if}
        {#if kind !== 'texture'}
          <ScaleOrientation bind:scale bind:orientation bounds={meshBounds} checkUnit={intake?.kind === 'glb'} gltf={intake?.kind === 'glb'} disabled={stage === 'uploading'} onstandup={intake?.kind === 'stl' ? standUp : undefined} />
        {/if}
        {#if result}
          <p>{result.trianglesBefore.toLocaleString()} → {result.trianglesAfter.toLocaleString()} triangles, {formatBytes(bytes)}</p>
          <label>Triangle budget <input type="number" step="1000" min="1000" bind:value={budget} onchange={() => void runConversion()} disabled={stage === 'uploading'} /></label>
          <p>Guessed millimetres, {result.proposal.upAxis.toUpperCase()} up. Check the model stands on the ring and faces the arrow.</p>
          {#if result.supports.suspected}
            <p role="alert">This file looks pre-supported: {result.supports.supportLike} thin, tall pieces stand under it. Supports are not removed: drop the unsupported file instead.</p>
          {/if}
        {:else if glbInfo}<p>{glbInfo.triangles.toLocaleString()} triangles, {formatBytes(bytes)}. Read as glTF: metres, Y up, front +Z. Check the size, and that the model stands on the ring and faces the arrow.</p>{/if}
        {#if tooBig}<p role="alert">{tooBig}</p>{/if}
        {#if !numbersOk}<p role="alert">Scale must be a number above zero, and the orientation numbers.</p>{/if}
        {#if stage === 'uploading' && uploadStage}
          {@const stages = (kind === 'terrain' ? ['hash', 'create', 'put', 'confirm', 'volumes'] : ['hash', 'create', 'put', 'confirm']) as UploadStage[]}
          <ol class="stepper" role="status">
            {#each stages as s, i (s)}
              {@const state = stepStates(stages, uploadStage)[i]}
              <li class={state}><span class="mark">{#if state === 'done'}<Icon name="ready" size="1.2em" />{:else}<span class="num">{i + 1}</span>{/if}</span>
                <span>{STAGE_LABELS[s]}{#if s === 'put' && state === 'current' && sent}{' '}<span class="num">{Math.floor((100 * sent.sent) / Math.max(sent.total, 1))}%</span>{/if}
                  {#if s === 'put' && state === 'current'}<progress max={sent?.total ?? 1} value={sent?.sent ?? 0}></progress>{/if}</span></li>
            {/each}
          </ol>
        {/if}
        <div class="actions">
          <button type="submit" class="primary" disabled={stage === 'uploading' || !numbersOk || !!nameError || !!tooBig || (kind !== 'texture' && !previewed)}>{stage === 'uploading' ? 'Uploading…' : 'Confirm and upload'}</button>
          <button type="button" onclick={reset} disabled={stage === 'uploading'}>Choose another file</button>
        </div>
        {#if kind !== 'texture' && !previewed && stage === 'preview'}<p><small>Waiting for the preview to draw before upload.</small></p>{/if}
      </form>
    </div>
  {:else if stage === 'done' && outcome}
    <p class="notice" role="status">Added <strong>{outcome.asset.name}</strong> to your library{#if filed}{' '}in <strong>{filed}</strong>{/if}{#if outcome.deduplicated}{' '}(already stored; nothing was sent){/if}{#if outcome.triangles !== undefined}, {outcome.triangles.toLocaleString()} triangles{/if}.</p>
    {#if fileError}<p role="alert">{fileError}</p>{/if}
    {#if outcome.warning}<p role="alert">{outcome.warning}</p>{/if}
    <div class="actions"><button onclick={reset}>Add another</button><a class="button" href="/library/{outcome.asset.id}" onclick={close}>Open it</a><button class="primary" onclick={close}>Done</button></div>
  {/if}
  {#if error}<p role="alert">{error}</p>{/if}
</dialog>
{/if}

<style>
  dialog { width: min(64rem, calc(100vw - 2rem)); max-height: calc(100dvh - 2rem); overflow: auto; }
  .head { display: flex; justify-content: space-between; align-items: center; gap: var(--s4); margin-bottom: var(--s4); }
  .head h2 { display: inline-flex; align-items: center; gap: var(--s2); margin: 0; font-size: 1.15rem; }
  .preview { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: var(--s5); min-height: 24rem; }
  .view { padding: 0; margin: 0; background: var(--view-bg); height: min(34rem, calc(100dvh - 10rem)); min-height: 20rem; overflow: hidden; align-self: start; position: sticky; top: 0; }
  @media (max-width: 48rem) { .preview { grid-template-columns: minmax(0, 1fr); } .view { height: 20rem; position: static; } }
  .drop { display: flex; flex-direction: column; align-items: center; gap: var(--s3); border: 2px dashed var(--accent); padding: var(--s7) var(--s4); text-align: center; color: var(--text); background: var(--rule); }
  .drop :global(.icon) { color: var(--accent); }
  .drop input { display: block; margin: var(--s2) auto; max-width: 100%; }
  .drop.over { border-style: solid; background: var(--panel-solid); }
  :global([data-theme='fantasy']) .drop { border-color: var(--wood); border-radius: 4px; }
  progress { width: 100%; }
  .stepper { list-style: none; padding: 0; margin: var(--s3) 0; display: grid; gap: var(--s2); }
  .stepper li { display: grid; grid-template-columns: 1.6rem 1fr; align-items: center; gap: var(--s2); color: var(--dim); }
  .stepper li.current, .stepper li.done { color: var(--text); }
  .stepper .mark { display: inline-grid; place-items: center; width: 1.6rem; height: 1.6rem; border: 1px solid currentColor; border-radius: 50%; font-size: .8rem; }
  .stepper li.done .mark { border-color: transparent; color: var(--success); }
  .stepper li.current .mark { color: var(--accent); }
</style>
