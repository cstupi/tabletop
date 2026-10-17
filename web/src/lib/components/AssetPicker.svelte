<script lang="ts">
  import type { AssetDto, Base, Guid } from '$lib/api/types';
  import { baseLabel, sameBase } from '$lib/units/bases';
  let { value, base, assets, label, onchange }: { value: Guid | null; base: Base | null; assets: AssetDto[]; label?: string; onchange: (id: Guid | null) => void } = $props();

  const fits = (a: AssetDto) => !!base && !!a.modelledBase && sameBase(a.modelledBase, base);
  const fitting = $derived(assets.filter(fits).sort((a, b) => a.name.localeCompare(b.name)));
  const others = $derived(assets.filter((a) => !fits(a)).sort((a, b) => a.name.localeCompare(b.name)));
</script>

<select value={value ?? ''} aria-label={label} onchange={(e) => onchange(e.currentTarget.value || null)}>
  <option value="">— none (draws the hitbox) —</option>
  {#if fitting.length}<optgroup label="Made for this base">{#each fitting as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</optgroup>{/if}
  {#if others.length}<optgroup label={fitting.length ? 'Other miniatures' : 'Miniatures'}>{#each others as a (a.id)}<option value={a.id}>{a.name}{#if a.modelledBase}{' · '}{baseLabel(a.modelledBase)}{/if}</option>{/each}</optgroup>{/if}
</select>
