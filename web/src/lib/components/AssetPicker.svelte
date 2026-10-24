<script lang="ts">
  import type { AssetDto, Base, FolderDto, Guid } from '$lib/api/types';
  import { pickerAssets } from '$lib/armies/folder';
  import { folderOptions } from '$lib/library/bulk';
  import { baseLabel, sameBase } from '$lib/units/bases';
  let { value, base, assets, label, onchange, folders = [], folder = null, onfolder }: {
    value: Guid | null; base: Base | null; assets: AssetDto[]; label?: string; onchange: (id: Guid | null) => void;
    folders?: FolderDto[]; folder?: Guid | null; onfolder?: (id: Guid | null) => void;
  } = $props();

  const listed = $derived(pickerAssets(assets, folders, folder, value));
  const fits = (a: AssetDto) => !!base && !!a.modelledBase && sameBase(a.modelledBase, base);
  const fitting = $derived(listed.filter(fits).sort((a, b) => a.name.localeCompare(b.name)));
  const others = $derived(listed.filter((a) => !fits(a)).sort((a, b) => a.name.localeCompare(b.name)));
  const options = $derived(folderOptions(folders));
</script>

<span class="picker">
  {#if options.length && onfolder}
    <select class="folder" value={folder ?? ''} aria-label={label ? `Folder for ${label}` : 'Folder'} onchange={(e) => onfolder(e.currentTarget.value || null)}>
      <option value="">All folders</option>
      {#each options as o (o.id)}<option value={o.id}>{o.label}</option>{/each}
    </select>
  {/if}
  <select value={value ?? ''} aria-label={label} onchange={(e) => onchange(e.currentTarget.value || null)}>
    <option value="">— none (draws the hitbox) —</option>
    {#if fitting.length}<optgroup label="Made for this base">{#each fitting as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</optgroup>{/if}
    {#if others.length}<optgroup label={fitting.length ? 'Other miniatures' : 'Miniatures'}>{#each others as a (a.id)}<option value={a.id}>{a.name}{#if a.modelledBase}{' · '}{baseLabel(a.modelledBase)}{/if}</option>{/each}</optgroup>{/if}
  </select>
</span>
<style>
  .picker { display: inline-flex; flex-wrap: wrap; gap: var(--s1) var(--s2); align-items: center }
  .folder { max-width: 14rem }
</style>
