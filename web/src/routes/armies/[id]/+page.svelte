<script lang="ts">
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import type { Army, AssetDto, PatchArmyRequest } from '$lib/api/types';
  import AssetPicker from '$lib/components/AssetPicker.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '$lib/armies/patches';
  import { baseLabel } from '$lib/units/bases';

  const id = $derived(page.params.id!);
  let army = $state<Army | null>(null); let name = $state(''); let error = $state<string | null>(null); let open = $state<Record<string, boolean>>({});
  let assets = $state<AssetDto[]>([]);
  const fail = (e: unknown) => { error = e instanceof Error ? e.message : String(e); };

  $effect(() => { api.armies.get(id).then((a) => { army = a; name = a.name; }).catch(fail); });
  // the library is loaded once and handed to every picker
  $effect(() => {
    (async () => {
      const out: AssetDto[] = [];
      for (let after: string | undefined; ;) {
        const p = await api.assets.list({ limit: 200, after, kind: 'miniature' });
        out.push(...p.items);
        if (!p.next) break;
        after = p.next;
      }
      assets = out.filter((a) => a.uploaded);
    })().catch(fail);
  });
  const apply = (p: PatchArmyRequest) => api.armies.patch(id, p).then((a) => (army = a)).catch(fail);
  const props = (ps: { name: string; parameter?: number }[]) => ps.map((p) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`)).join(', ');
</script>

{#if army}
  <header>
    <input bind:value={name} onchange={() => name.trim() && name !== army!.name && apply(rename(name.trim()))} aria-label="Army name" class="title" />
    <p><small>{army.gameSystem} · imported from <a href={army.source.startsWith('http') ? army.source : `https://army-forge.onepagerules.com/share?id=${army.source}`} target="_blank" rel="noopener">Army Forge</a>. Stats, weapons and rules are the list's; meshes, bases and heights are yours.</small></p>
  </header>
  {#each army.units as unit (unit.id)}
    <section class="unit">
      <h2><button onclick={() => (open[unit.id] = !open[unit.id])}>{open[unit.id] ? '▾' : '▸'}</button> {unit.name} <small>[{unit.models.length}] · {Object.entries(unit.stats).map(([k, v]) => `${k} ${v}`).join(' · ')}{#if unit.properties.length} · {props(unit.properties)}{/if}</small></h2>
      <label>Mesh for every model <AssetPicker {assets} value={unit.models.every((m) => m.assetId === unit.models[0]?.assetId) ? (unit.models[0]?.assetId ?? null) : null} base={unit.models[0]?.base ?? null} onchange={(a) => apply(unitDefault(unit.id, a))} /></label>
      {#if open[unit.id]}
        <table>
          <thead><tr><th>Model</th><th>Loadout and rules</th><th>Mesh</th><th>Base (hitbox)</th><th>Height (in)</th></tr></thead>
          <tbody>
            {#each unit.models as m (m.id)}
              <tr>
                <td>{m.name}</td>
                <td><small>{slotSummary(m)}</small></td>
                <td><AssetPicker {assets} value={m.assetId ?? null} base={m.base} onchange={(a) => apply(slotMesh(m.id, a))} /></td>
                <td><BasePicker value={m.base} allowNone={false} onchangevalue={(b) => b && apply(slotBase(m.id, b))} /> <small>{baseLabel(m.base)}</small></td>
                <td><input type="number" step="0.25" min="0.25" value={m.height} onchange={(e) => { const h = Number(e.currentTarget.value); if (Number.isFinite(h) && h > 0) apply(slotHeight(m.id, h)); else e.currentTarget.value = String(m.height); }} /></td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}
    </section>
  {/each}
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>.title { font-size: 1.5rem; font-weight: bold; border: none; background: none; width: 100% } .unit { border-top: 1px solid #8884; padding: .5rem 0 } td, th { text-align: left; vertical-align: top; padding: .25rem .5rem }</style>
