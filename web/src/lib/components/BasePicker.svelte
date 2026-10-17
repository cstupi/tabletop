<script lang="ts">
  import type { Base, BaseShape } from '$lib/api/types';
  import { BASE_PRESETS, presetFor } from '$lib/units/bases';
  import { inchesToMm, mmToInches, round } from '$lib/units/units';

  let { value = $bindable(), allowNone = false, label, onchangevalue }: { value: Base | null; allowNone?: boolean; label?: string; onchangevalue?: (b: Base | null) => void } = $props();
  let choice = $state<string>(value === null ? 'none' : presetFor(value)?.label ?? 'custom');
  let shape = $state<BaseShape>(value?.shape ?? 'circle');
  let aMm = $state(round(inchesToMm(value?.a ?? 32 / 25.4), 1));
  let bMm = $state(round(inchesToMm(value?.b ?? 32 / 25.4), 1));

  function pick(label: string) {
    choice = label;
    if (label === 'none') value = null;
    else if (label === 'custom') {
      if (value !== null) {
        shape = value.shape;
        aMm = round(inchesToMm(value.a), 1);
        bMm = round(inchesToMm(value.b), 1);
      }
      applyCustom();
    } else value = BASE_PRESETS.find((p) => p.label === label)!.base;
    if (label !== 'custom') onchangevalue?.(value);
  }
  function applyCustom() {
    const a = mmToInches(aMm), b = shape === 'circle' ? a : mmToInches(bMm);
    if (!Number.isFinite(a) || a <= 0 || !Number.isFinite(b) || b <= 0) return;
    value = { shape, a, b };
    onchangevalue?.(value);
  }
</script>

<select value={choice} aria-label={label} onchange={(e) => pick(e.currentTarget.value)}>
  {#if allowNone}<option value="none">No base</option>{/if}
  {#each BASE_PRESETS as p}<option value={p.label}>{p.label}</option>{/each}
  <option value="custom">Custom…</option>
</select>
{#if choice === 'custom'}
  <select bind:value={shape} onchange={applyCustom} aria-label="Base shape"><option value="circle">round</option><option value="oval">oval</option><option value="rect">rectangle</option></select>
  <input type="number" min="1" step="0.5" bind:value={aMm} onchange={applyCustom} aria-label={shape === 'circle' ? 'Diameter (mm)' : 'Length (mm)'} /> mm
  {#if shape !== 'circle'}× <input type="number" min="1" step="0.5" bind:value={bMm} onchange={applyCustom} aria-label="Width (mm)" /> mm{/if}
{/if}
