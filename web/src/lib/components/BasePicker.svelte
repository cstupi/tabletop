<script lang="ts">
  import type { Base, BaseShape } from '$lib/api/types';
  import { BASE_PRESETS, presetFor } from '$lib/units/bases';
  import { inchesToMm, mmToInches, round } from '$lib/units/units';

  let { value = $bindable(), allowNone = false, onchangevalue }: { value: Base | null; allowNone?: boolean; onchangevalue?: (b: Base | null) => void } = $props();
  let choice = $state<string>(value === null ? 'none' : presetFor(value)?.label ?? 'custom');
  let shape = $state<BaseShape>(value?.shape ?? 'circle');
  let aMm = $state(round(inchesToMm(value?.a ?? 32 / 25.4), 1));
  let bMm = $state(round(inchesToMm(value?.b ?? 32 / 25.4), 1));

  function pick(label: string) {
    choice = label;
    if (label === 'none') value = null;
    else if (label === 'custom') applyCustom();
    else value = BASE_PRESETS.find((p) => p.label === label)!.base;
    if (label !== 'custom') onchangevalue?.(value);
  }
  function applyCustom() {
    const a = mmToInches(aMm), b = shape === 'circle' ? a : mmToInches(bMm);
    value = { shape, a, b };
    onchangevalue?.(value);
  }
</script>

<select value={choice} onchange={(e) => pick(e.currentTarget.value)}>
  {#if allowNone}<option value="none">No base</option>{/if}
  {#each BASE_PRESETS as p}<option value={p.label}>{p.label}</option>{/each}
  <option value="custom">Custom…</option>
</select>
{#if choice === 'custom'}
  <select bind:value={shape} onchange={applyCustom}><option value="circle">round</option><option value="oval">oval</option><option value="rect">rectangle</option></select>
  <input type="number" min="1" step="0.5" bind:value={aMm} onchange={applyCustom} aria-label={shape === 'circle' ? 'size (mm)' : 'length (mm)'} /> mm
  {#if shape !== 'circle'}× <input type="number" min="1" step="0.5" bind:value={bMm} onchange={applyCustom} aria-label="width (mm)" /> mm{/if}
{/if}
