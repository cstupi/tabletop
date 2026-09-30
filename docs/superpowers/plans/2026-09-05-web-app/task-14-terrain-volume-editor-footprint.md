# Task 14: The terrain volume editor — a footprint and two heights over a top-down preview

**Files:**
- Create: `web/src/lib/components/VolumeEditor.svelte`, `web/src/lib/library/volume-edit.ts`
- Modify: `web/src/lib/components/MeshPreview.svelte` (the `highlight` prop)
- Test: `web/tests/volume-edit.test.ts`

**Interfaces:**
- Consumes: `api.assets.getVolumes/putVolumes`, `Volume`, `TerrainVolume`, `MeshPreview` in `'top'` mode, `DECLARATIONS`, `DECLARATION_LABELS`, `parseDeclarations`, `formatDeclarations`, `defaultVolume`, `describeGlb`, `terrainVolume`.
- Produces: `VolumeDraft { kind; footprintKind; a; b; offsetX; offsetY; yawDegrees; bottom; top; declarations: Set<Declaration>; notes: string }`, `toDraft(v: Volume): VolumeDraft`, `fromDraft(d: VolumeDraft): Volume`, `validate(d): string[]` (every number finite, `top > bottom`, positive dimensions, `see_in_and_out` only with `blocks_line_of_sight`; management validates nothing on `PUT /volumes` beyond deserialisation, so this is the only check), `screenToTable(px, py, viewport, extent)` for drag-to-move; `VolumeEditor` with props `assetId`, `glb`, `scale`, `orientation`; `MeshPreview` gains `highlight?: number`, the index of the volume drawn in blue.
- The editor is for when the default is wrong: a list of volumes with add / duplicate / remove / *reset to bounding box*, each row editing kind (solid | area), footprint (circle radius | rect width × length), offset, yaw, bottom, top, the six declaration checkboxes and free-text notes; the selected volume is highlighted in the top-down preview and dragged to move its offset. Save is one `PUT /v1/assets/{id}/volumes` with the whole list (the endpoint replaces). Each row is a `role="button"` element that selects on click, Enter or Space; its keydown handler returns unless the event's target is the row itself, so Space still types in Notes and toggles the checkboxes. *Reset to bounding box* reports a GLB it cannot read into `error`.

Spec: geometry values → Collision volumes (`kind solid | area`, `footprint circle(radius) | rect(width, length)`, `offset`, `yaw`, `bottom`, `top`, `declarations`, `notes`; "a piece carries a *list* of volumes"), Declarations ("`see in and out` is a modifier on blocking … means nothing on a volume that is not opaque"), Where volumes come from ("The editor is only for when the default is wrong … a top-down 2D task over a model preview"; the bar: "upload a terrain model and set where collision happens, easily, with the supported shapes"); `architecture.md` → Web frontend ("Collision volume authoring is the web's too").

- [ ] **Step 1: Write the failing tests and `volume-edit.ts`**

```ts
// web/tests/volume-edit.test.ts
import { describe, expect, it } from 'vitest';
import { fromDraft, screenToTable, toDraft, validate } from '../src/lib/library/volume-edit';
import type { Volume } from '../src/lib/api/types';

const v: Volume = { kind: 'area', footprint: { kind: 'circle', a: 3, b: 3 }, offset: { x: 1, y: -2 }, yaw: { degrees: 30 }, bottom: 2, top: 8, declarations: 'blocks_line_of_sight, see_in_and_out, cover', notes: 'forest' };

describe('volume drafts', () => {
  it('round-trips and keeps a circle square', () => {
    const d = toDraft(v);
    expect(d.declarations).toEqual(new Set(['blocks_line_of_sight', 'see_in_and_out', 'cover']));
    expect(fromDraft(d)).toEqual(v);
    expect(fromDraft({ ...d, footprintKind: 'circle', a: 4, b: 9 }).footprint).toEqual({ kind: 'circle', a: 4, b: 4 });
    expect(fromDraft({ ...d, notes: '  ' }).notes).toBeUndefined();
  });
  it('validates heights, dimensions and the see-in-and-out modifier', () => {
    expect(validate(toDraft(v))).toEqual([]);
    expect(validate({ ...toDraft(v), top: 1 })).toContain('top must be above bottom');
    expect(validate({ ...toDraft(v), a: 0 })).toContain('dimensions must be positive');
    expect(validate({ ...toDraft(v), declarations: new Set(['see_in_and_out']) })).toContain('see in and out needs blocks line of sight');
    expect(validate({ ...toDraft(v), bottom: null as unknown as number })).toContain('values must be numbers');
    expect(validate({ ...toDraft(v), a: NaN })).toContain('values must be numbers');
  });
  it('maps a click in the top-down view to table coordinates', () => {
    // a 400×200 viewport showing x ∈ [-4, 4], y ∈ [-2, 2]: the centre is the origin, +y is up on screen
    expect(screenToTable(200, 100, { width: 400, height: 200 }, { minX: -4, maxX: 4, minY: -2, maxY: 2 })).toEqual({ x: 0, y: 0 });
    expect(screenToTable(400, 0, { width: 400, height: 200 }, { minX: -4, maxX: 4, minY: -2, maxY: 2 })).toEqual({ x: 4, y: 2 });
  });
});
```

```ts
// web/src/lib/library/volume-edit.ts
import type { Declaration, FootprintKind, Volume, VolumeKind } from '$lib/api/types';
import { formatDeclarations, parseDeclarations } from '$lib/units/volumes';

export interface VolumeDraft { kind: VolumeKind; footprintKind: FootprintKind; a: number; b: number; offsetX: number; offsetY: number; yawDegrees: number; bottom: number; top: number; declarations: Set<Declaration>; notes: string }

export const toDraft = (v: Volume): VolumeDraft => ({ kind: v.kind, footprintKind: v.footprint.kind, a: v.footprint.a, b: v.footprint.b, offsetX: v.offset.x, offsetY: v.offset.y, yawDegrees: v.yaw.degrees, bottom: v.bottom, top: v.top, declarations: parseDeclarations(v.declarations), notes: v.notes ?? '' });

export function fromDraft(d: VolumeDraft): Volume {
  const notes = d.notes.trim();
  return { kind: d.kind, footprint: { kind: d.footprintKind, a: d.a, b: d.footprintKind === 'circle' ? d.a : d.b }, offset: { x: d.offsetX, y: d.offsetY }, yaw: { degrees: d.yawDegrees }, bottom: d.bottom, top: d.top, declarations: formatDeclarations(d.declarations), ...(notes ? { notes } : {}) };
}

export function validate(d: VolumeDraft): string[] {
  const errors: string[] = [];
  if (![d.a, d.b, d.offsetX, d.offsetY, d.yawDegrees, d.bottom, d.top].every(Number.isFinite)) errors.push('values must be numbers');
  if (!(d.top > d.bottom)) errors.push('top must be above bottom');
  if (!(d.a > 0) || (d.footprintKind === 'rect' && !(d.b > 0))) errors.push('dimensions must be positive');
  if (d.declarations.has('see_in_and_out') && !d.declarations.has('blocks_line_of_sight')) errors.push('see in and out needs blocks line of sight');
  return errors;
}

/** The top-down camera shows [minX, maxX] × [minY, maxY] across the viewport with +Y up the screen. */
export function screenToTable(px: number, py: number, viewport: { width: number; height: number }, e: { minX: number; maxX: number; minY: number; maxY: number }) {
  return { x: e.minX + (px / viewport.width) * (e.maxX - e.minX), y: e.maxY - (py / viewport.height) * (e.maxY - e.minY) };
}
```

- [ ] **Step 2: Run; expect failures then pass. Add `highlight` to `MeshPreview.svelte`:** take `highlight?: number` in `$props()`, draw volume `i` with `const color = i === highlight ? 0x3399ff : v.kind === 'solid' ? 0xff8800 : 0x33cc66;` in `drawOverlay`'s loop, and read `highlight` in the transform `$effect` beside `volumes` so a change of selection redraws.

- [ ] **Step 3: Write `VolumeEditor.svelte`** (replacing Task 13's stub):

```svelte
<script lang="ts">
  import { api } from '$lib/api/client';
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

  $effect(() => { api.assets.getVolumes(assetId).then((rows) => (drafts = rows.map((r) => toDraft(r.volume)))).catch((e) => (error = e instanceof Error ? e.message : String(e))); });

  const add = () => { drafts = [...drafts, toDraft(defaultVolume(extent.width, extent.length, extent.height))]; selected = drafts.length - 1; };
  const duplicate = () => { if (drafts[selected]) { drafts = [...drafts, structuredClone($state.snapshot(drafts[selected]!))]; selected = drafts.length - 1; } };
  const remove = () => { drafts = drafts.filter((_, i) => i !== selected); selected = Math.max(0, selected - 1); };
  const reset = async () => { try { const d = await describeGlb(glb); drafts = [toDraft(terrainVolume(d.bounds, { scale, orientation }))]; selected = 0; } catch (e) { error = e instanceof Error ? e.message : String(e); } };
  const save = () => api.assets.putVolumes(assetId, volumes).then(() => (saved = true)).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const toggle = (d: VolumeDraft, name: (typeof DECLARATIONS)[number], on: boolean) => { const s = new Set(d.declarations); on ? s.add(name) : s.delete(name); d.declarations = s; };

  // drag the selected footprint: MeshPreview reports the table rectangle its top-down camera frames
  let dragging = false;
  function tableAt(e: PointerEvent) {
    const box = (e.currentTarget as HTMLElement).getBoundingClientRect();
    return screenToTable(e.clientX - box.left, e.clientY - box.top, { width: box.width, height: box.height }, extent.frame);
  }
  function move(e: PointerEvent) { if (!dragging || !drafts[selected]) return; const p = tableAt(e); drafts[selected]!.offsetX = +p.x.toFixed(2); drafts[selected]!.offsetY = +p.y.toFixed(2); }
</script>

<section class="editor">
  <h2>Collision volumes</h2>
  <p>Gameplay runs on these prisms, not on the mesh. Orange is solid (stood on), green is area (stood inside). Drag the selected outline to move it.</p>
  <div class="top" role="application" onpointerdown={() => (dragging = true)} onpointerup={() => (dragging = false)} onpointerleave={() => (dragging = false)} onpointermove={move}>
    <MeshPreview {glb} {scale} {orientation} base={null} mode="top" {volumes} highlight={selected} onmeasure={(e) => (extent = e)} />
  </div>
  <div class="bar"><button onclick={add}>Add volume</button><button onclick={duplicate} disabled={!drafts[selected]}>Duplicate</button><button onclick={remove} disabled={!drafts[selected]}>Remove</button><button onclick={reset}>Reset to bounding box</button></div>
  <div class="volumes">
    {#each drafts as d, i}
      <div class:selected={i === selected} role="button" tabindex="0" onkeydown={(e) => { if (e.target !== e.currentTarget) return; if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selected = i; } }} onclick={() => (selected = i)}>
        <select bind:value={d.kind}><option value="solid">solid</option><option value="area">area</option></select>
        <select bind:value={d.footprintKind}><option value="rect">rectangle</option><option value="circle">circle</option></select>
        {#if d.footprintKind === 'circle'}<label>radius <input type="number" step="0.25" bind:value={d.a} /></label>
        {:else}<label>width <input type="number" step="0.25" bind:value={d.a} /></label><label>length <input type="number" step="0.25" bind:value={d.b} /></label>{/if}
        <label>offset x <input type="number" step="0.25" bind:value={d.offsetX} /></label><label>y <input type="number" step="0.25" bind:value={d.offsetY} /></label>
        <label>yaw <input type="number" step="15" bind:value={d.yawDegrees} /></label>
        <label>bottom <input type="number" step="0.25" bind:value={d.bottom} /></label><label>top <input type="number" step="0.25" bind:value={d.top} /></label>
        <fieldset><legend>Declares</legend>{#each DECLARATIONS as name}<label><input type="checkbox" checked={d.declarations.has(name)} onchange={(e) => toggle(d, name, e.currentTarget.checked)} /> {DECLARATION_LABELS[name]}</label>{/each}</fieldset>
        <label>Notes <input bind:value={d.notes} placeholder="difficult when moving up, …" /></label>
      </div>
    {/each}
  </div>
  {#each errors as e}<p role="alert">{e}</p>{/each}
  <button onclick={save} disabled={errors.length > 0 || drafts.length === 0}>Save volumes</button>{#if saved}<span>Saved.</span>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</section>
<style>.top { height: 24rem; touch-action: none } .selected { outline: 2px solid #3399ff } .bar { display: flex; gap: .5rem } .volumes > div { display: flex; flex-wrap: wrap; gap: .5rem; padding: .5rem; border-bottom: 1px solid #8884 }</style>
```

- [ ] **Step 4: `npm test && npm run check`; 57 passed. Run the app on a terrain asset: the default box is there from upload; add an `area` volume declaring cover + difficult, see it outlined in blue while selected, drag it, set `see in and out` without `blocks line of sight` and read the error, save, reload, and `GET /v1/assets/{id}/volumes` shows two rows. Commit: `Add the terrain volume editor over the top-down preview`.**
