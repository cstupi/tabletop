<script lang="ts">
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Army, AssetDto, PatchArmyRequest } from '$lib/api/types';
  import AssetPicker from '$lib/components/AssetPicker.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '$lib/armies/patches';
  import { baseLabel } from '$lib/units/bases';
  import { MAX_NAME, nameProblem } from '$lib/validation';

  const id = $derived(page.params.id!);
  let army = $state<Army | null>(null); let name = $state(''); let error = $state<string | null>(null); let open = $state<Record<string, boolean>>({});
  let loadFailed = $state(false);
  let assets = $state<AssetDto[]>([]); let assetsLoaded = $state(false);
  let status = $state<string>('');
  const fail = (e: unknown) => { error = errorMessage(e); };
  const nameError = $derived(army ? nameProblem(name, 'Army name') : null);

  $effect(() => { api.armies.get(id).then((a) => { army = a; name = a.name; }).catch((e) => { loadFailed = true; fail(e); }); });
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
      assets = out.filter((a) => a.uploaded); assetsLoaded = true;
    })().catch(fail);
  });
  async function apply(p: PatchArmyRequest) {
    status = 'Saving…'; error = null;
    try { army = await api.armies.patch(id, p); status = 'Saved.'; }
    catch (e) { status = ''; fail(e); }
  }
  function saveName() {
    if (!army || nameError || name.trim() === army.name) return;
    void apply(rename(name.trim()));
  }
  const props = (ps: { name: string; parameter?: number }[]) => ps.map((p) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`)).join(', ');
</script>

<svelte:head><title>{army ? `${army.name} · Armies` : 'Army'} · Tabletop</title></svelte:head>

<p><a href="/armies">← All armies</a></p>
{#if army}
  <header>
    <h1 class="visually-hidden">{army.name}</h1>
    <label for="army-name"><small>Army name</small></label>
    <input id="army-name" bind:value={name} maxlength={MAX_NAME} onchange={saveName} aria-invalid={!!nameError} aria-describedby="army-name-help" class="title" />
    {#if nameError}<p id="army-name-help"><small>{nameError}</small></p>{/if}
    <p><small>{army.gameSystem} · imported from <a href={army.source.startsWith('http') ? army.source : `https://army-forge.onepagerules.com/share?id=${army.source}`} target="_blank" rel="noopener">Army Forge</a>. Stats, weapons and rules are the list's; meshes, bases and heights are yours. Changes save as you make them.</small></p>
  </header>
  <p class="status" role="status">{status}</p>
  {#if assetsLoaded && assets.length === 0}
    <p>Your library has no miniatures yet. <a href="/library">Upload meshes in the Library</a>, then pick them here. A model without a mesh is drawn as its base.</p>
  {/if}
  {#each army.units as unit (unit.id)}
    <section class="unit" aria-labelledby="unit-{unit.id}">
      <h2 id="unit-{unit.id}">
        <button onclick={() => (open[unit.id] = !open[unit.id])} aria-expanded={!!open[unit.id]} aria-controls="models-{unit.id}">
          <span aria-hidden="true">{open[unit.id] ? '▾' : '▸'}</span> {unit.name}
        </button>
        <small>[{unit.models.length}] · {Object.entries(unit.stats).map(([k, v]) => `${k} ${v}`).join(' · ')}{#if unit.properties.length} · {props(unit.properties)}{/if}</small>
      </h2>
      <label>Mesh for every model in {unit.name} <AssetPicker {assets} value={unit.models.every((m) => m.assetId === unit.models[0]?.assetId) ? (unit.models[0]?.assetId ?? null) : null} base={unit.models[0]?.base ?? null} onchange={(a) => apply(unitDefault(unit.id, a))} /></label>
      {#if open[unit.id]}
        <div class="scroll-x" id="models-{unit.id}">
          <table>
            <thead><tr><th scope="col">Model</th><th scope="col">Loadout and rules</th><th scope="col">Mesh</th><th scope="col">Base (hitbox)</th><th scope="col">Height (in)</th></tr></thead>
            <tbody>
              {#each unit.models as m, i (m.id)}
                <tr>
                  <th scope="row">{m.name}</th>
                  <td><small>{slotSummary(m)}</small></td>
                  <td><AssetPicker {assets} value={m.assetId ?? null} base={m.base} label="Mesh for {m.name} {i + 1}" onchange={(a) => apply(slotMesh(m.id, a))} /></td>
                  <td><BasePicker value={m.base} allowNone={false} label="Base for {m.name} {i + 1}" onchangevalue={(b) => b && apply(slotBase(m.id, b))} /> <small>{baseLabel(m.base)}</small></td>
                  <td><input type="number" step="0.25" min="0.25" value={m.height} aria-label="Height in inches for {m.name} {i + 1}" onchange={(e) => { const h = Number(e.currentTarget.value); if (Number.isFinite(h) && h > 0) apply(slotHeight(m.id, h)); else e.currentTarget.value = String(m.height); }} /></td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/each}
{:else if !loadFailed}
  <p role="status">Loading…</p>
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>
  .title { font-size: 1.5rem; font-weight: bold; border: none; background: none; width: 100%; box-sizing: border-box }
  .unit { border-top: 1px solid #8884; padding: .5rem 0 } .unit h2 { font-size: 1.1rem } .unit h2 button { font: inherit; font-weight: bold }
  .status { min-height: 1.4em; margin: 0 }
  td, th { text-align: left; vertical-align: top; padding: .25rem .5rem } tbody th { font-weight: normal }
</style>
