<script lang="ts">
  import type { Orientation } from '$lib/api/types';
  import type { Bounds } from '$lib/mesh/types';
  import { UNIT_SCALES, type MeshUnit } from '$lib/units/units';
  import { describeSize, suggestUnit, toggleUpright } from '$lib/mesh/size';

  /** The Scale and Orientation fieldsets shared by the upload dialog and the asset page. `checkUnit` turns on the Use mm / Use cm
   *  suggestion (a glTF's unit is known, so an implausible size means the exporter ignored it); `onstandup` replaces the plain
   *  pitch toggle (an STL upload re-converts on the chosen axis). */
  let { scale = $bindable(), orientation = $bindable(), bounds, checkUnit = false, gltf = true, disabled = false, onstandup }: {
    scale: number; orientation: Orientation; bounds: Bounds | null; checkUnit?: boolean; gltf?: boolean; disabled?: boolean; onstandup?: () => void;
  } = $props();

  const UNIT_LABELS: Record<MeshUnit, string> = { mm: 'mm', cm: 'cm', in: 'inches', m: 'm' };
  const UNIT_NAMES: Record<MeshUnit, string> = { mm: 'millimetres', cm: 'centimetres', in: 'inches', m: 'metres' };
  const numbersOk = $derived([scale, orientation.pitch, orientation.roll, orientation.yaw].every(Number.isFinite) && scale > 0);
  const size = $derived(bounds && numbersOk ? describeSize(bounds, { scale, orientation }) : null);
  const suggested = $derived(checkUnit && bounds && scale > 0 ? suggestUnit(bounds, scale) : null);
  const standUp = () => { if (onstandup) onstandup(); else orientation = toggleUpright(orientation, gltf); };
</script>

<fieldset><legend>Scale</legend>
  <input type="number" step="any" min="0" bind:value={scale} aria-label="Scale, inches per model unit" {disabled} /> ×
  <span class="seg units" role="group" aria-label="The model was made in">
    {#each Object.keys(UNIT_SCALES) as MeshUnit[] as u (u)}<button type="button" aria-pressed={scale === UNIT_SCALES[u]} onclick={() => (scale = UNIT_SCALES[u])} {disabled}>{UNIT_LABELS[u]}</button>{/each}
  </span>
  {#if size}<p class="size" aria-live="polite">{size}</p>{/if}
  {#if suggested}<p class="suggest" role="status">That is not a miniature's size in these units; it looks like {UNIT_NAMES[suggested]}. <button type="button" onclick={() => (scale = UNIT_SCALES[suggested!])}>Use {UNIT_LABELS[suggested]}</button></p>{/if}
</fieldset>
<fieldset><legend>Orientation (degrees)</legend>
  <label>Pitch <input type="number" step="15" bind:value={orientation.pitch} {disabled} /></label>
  <label>Roll <input type="number" step="15" bind:value={orientation.roll} {disabled} /></label>
  <label>Yaw (facing) <input type="number" step="15" bind:value={orientation.yaw} {disabled} /></label>
  <button type="button" onclick={standUp} {disabled}>Stand up / lay flat</button>
</fieldset>

<style>
  fieldset { margin: var(--s3) 0; }
  fieldset label { display: inline-flex; flex-direction: column; align-items: flex-start; gap: var(--s1); margin: 0 var(--s3) var(--s2) 0; }
  .units { vertical-align: middle; margin-left: var(--s2); }
  .units > button { min-height: 2.25rem; }
  .size, .suggest { margin: var(--s2) 0 0; }
  .suggest button { margin-left: var(--s2); }
</style>
