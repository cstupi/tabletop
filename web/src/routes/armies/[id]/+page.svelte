<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { tick } from 'svelte';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Army, AssetDto, FolderDto, Guid, ModelSlot, PatchArmyRequest, Unit } from '$lib/api/types';
  import AssetPicker from '$lib/components/AssetPicker.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import Icon from '$lib/components/Icon.svelte';
  import Thumb from '$lib/components/Thumb.svelte';
  import { meshLine, slotRows, statBadges, systemName } from '$lib/armies/cards';
  import { loadFolder, saveFolder } from '$lib/armies/folder';
  import { importErrorMessage, reimport } from '$lib/armies/import';
  import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '$lib/armies/patches';
  import { folderOptions } from '$lib/library/bulk';
  import { MAX_NAME, nameProblem } from '$lib/validation';

  const id = $derived(page.params.id!);
  let army = $state<Army | null>(null); let name = $state(''); let error = $state<string | null>(null); let perModel = $state<Record<string, boolean>>({});
  let loadFailed = $state(false); let editing = $state(false); let nameInput = $state<HTMLInputElement>(); let busy = $state(false);
  let assets = $state<AssetDto[]>([]); let assetsLoaded = $state(false);
  let status = $state<string>('');
  // every mesh picker lists one library folder, chosen once for the page and remembered for this army
  let folders = $state<FolderDto[]>([]); let folder = $state<Guid | null>(null);
  const folderChoices = $derived(folderOptions(folders));
  $effect(() => { const armyId = id; api.folders.list().then((f) => { folders = f; folder = loadFolder(armyId, f); }).catch(fail); });
  function pickFolder(f: Guid | null) { folder = f; saveFolder(id, f); }
  const fail = (e: unknown) => { error = errorMessage(e); };
  const nameError = $derived(army ? nameProblem(name, 'Army name') : null);
  const slots = $derived(army ? army.units.flatMap((u) => u.models) : []);
  const withMesh = $derived(slots.filter((m) => m.assetId).length);
  const byId = $derived(new Map(assets.map((a) => [a.id, a])));
  const forgeUrl = $derived(army ? (army.source.startsWith('http') ? army.source : `https://army-forge.onepagerules.com/share?id=${army.source}`) : '');

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
  async function startRename() { editing = true; await tick(); nameInput?.focus(); nameInput?.select(); }
  function saveName(e?: SubmitEvent) {
    e?.preventDefault();
    if (!army || nameError) return;
    editing = false;
    if (name.trim() !== army.name) void apply(rename(name.trim()));
  }
  async function doReimport() {
    if (!army || !confirm(`Re-import "${army.name}" from Army Forge?\n\nThe army is replaced with the list as it is now. Every mesh, base and height set per slot is lost.`)) return;
    busy = true; error = null;
    try { const fresh = await reimport(army); await goto(`/armies/${fresh.id}`); } catch (e) { error = importErrorMessage(e); } finally { busy = false; }
  }
  const props = (ps: { name: string; parameter?: number }[]) => ps.map((p) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`)).join(', ');
  const plural = (n: number, w: string) => `${n} ${w}${n === 1 ? '' : 's'}`;
  function height(e: Event & { currentTarget: HTMLInputElement }, ids: Guid[], current: number) {
    const h = Number(e.currentTarget.value);
    if (Number.isFinite(h) && h > 0) void apply(slotHeight(ids, h)); else e.currentTarget.value = current.toFixed(2);
  }
</script>

<svelte:head><title>{army ? `${army.name} · Armies` : 'Army'} · Tabletop</title></svelte:head>

{#snippet row(unit: Unit, m: ModelSlot, all: boolean, label: string)}
  {@const ids = all ? unit.models.map((x) => x.id) : [m.id]}
  {@const mesh = m.assetId ? byId.get(m.assetId) : undefined}
  <div class="slot" class:sub={!all && unit.models.length > 1 && slotRows(unit).shared}>
    <Thumb url={mesh?.thumbnail} alt={mesh?.name ?? ''} size="2.75rem" />
    <span class="slot-name" title={slotSummary(m)}>{label}</span>
    <span class="cell mesh"><AssetPicker {assets} {folders} {folder} value={m.assetId ?? null} base={m.base} label="Mesh for {label}"
      onchange={(a) => apply(all ? unitDefault(unit.id, a) : slotMesh(m.id, a))} /></span>
    <span class="cell base"><Icon name="base" /><BasePicker value={m.base} allowNone={false} label="Base for {label}" onchangevalue={(b) => b && apply(slotBase(ids, b))} /></span>
    <span class="cell tall"><Icon name="height" /><input class="height" type="number" step="0.25" min="0.25" value={m.height.toFixed(2)} aria-label="Height in inches for {label}"
      onchange={(e) => height(e, ids, m.height)} /><span class="dim">in</span></span>
  </div>
{/snippet}

<p><a class="back" href="/armies"><Icon name="back" />Armies</a></p>
{#if army}
  <header class="head" title="Stats, weapons and rules are the list's; meshes, bases and heights are yours.">
    <div class="name-row">
      <span title={systemName(army.gameSystem)}><Icon name={army.gameSystem === 'age-of-fantasy' ? 'fantasy' : 'grimdark'} size="1.75rem" label={systemName(army.gameSystem)} /></span>
      {#if editing}
        <form class="rename" onsubmit={saveName}>
          <input bind:this={nameInput} bind:value={name} maxlength={MAX_NAME} aria-label="Army name" aria-invalid={!!nameError} onblur={() => saveName()}
            onkeydown={(e) => { if (e.key === 'Escape') { name = army!.name; editing = false; } }} />
        </form>
      {:else}
        <h1>{army.name}</h1>
        <button class="icon-btn" title="Rename" aria-label="Rename {army.name}" onclick={startRename}><Icon name="edit" size="1.1em" /></button>
      {/if}
      <span class="chip" title="Edition">{army.edition}</span>
      <a class="forge" href={forgeUrl} target="_blank" rel="noopener">Army Forge<Icon name="external" size="1em" /></a>
      <span class="push"></span>
      <button onclick={doReimport} disabled={busy}><Icon name="replay" />Re-import</button>
    </div>
    {#if nameError && editing}<p><small>{nameError}</small></p>{/if}
    <div class="facts-row">
      <p class="facts">{plural(army.units.length, 'unit')} · {plural(slots.length, 'model')} · {meshLine(withMesh, slots.length)}</p>
      <span class="status saved" role="status">{status}</span>
      {#if folderChoices.length}
        <label class="folder">Mesh folder
          <select value={folder ?? ''} onchange={(e) => pickFolder(e.currentTarget.value || null)}>
            <option value="">All folders</option>
            {#each folderChoices as o (o.id)}<option value={o.id}>{o.label}</option>{/each}
          </select></label>
      {/if}
    </div>
  </header>
  {#if assetsLoaded && assets.length === 0}
    <p class="quiet-band"><Icon name="mesh" />No miniatures in the <a href="/library">Library</a></p>
  {/if}
  {#each army.units as unit (unit.id)}
    {@const view = slotRows(unit)}
    <section class="panel unit" aria-labelledby="unit-{unit.id}">
      <div class="unit-head">
        <h2 id="unit-{unit.id}" title={props(unit.properties) || undefined}>{unit.name}{#if unit.models.length > 1}<span class="count num">×{unit.models.length}</span>{/if}</h2>
        <span class="stats">{#each statBadges(unit) as s (s.key)}<span class="stat" title={s.key}>{#if s.icon}<Icon name={s.icon} label={s.key} />{:else}<small>{s.key}</small>{/if}<strong class="num">{s.value}</strong></span>{/each}</span>
      </div>
      {#if view.shared}
        {@render row(unit, unit.models[0]!, true, 'every model')}
        <button class="quiet per" aria-expanded={!!perModel[unit.id]} onclick={() => (perModel[unit.id] = !perModel[unit.id])}><span class="chev"><Icon name="chevron" size="1em" /></span>per model</button>
        {#if perModel[unit.id]}{#each unit.models as m, i (m.id)}{@render row(unit, m, false, `${m.name} ${i + 1}`)}{/each}{/if}
      {:else}
        {#each unit.models as m, i (m.id)}{@render row(unit, m, false, unit.models.length > 1 ? `${m.name} ${i + 1}` : m.name)}{/each}
      {/if}
    </section>
  {/each}
{:else if !loadFailed}
  <p class="loading" role="status">Loading…</p>
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>
  .head { margin: 0 0 var(--s4) }
  .name-row { display: flex; flex-wrap: wrap; align-items: center; gap: var(--s2) var(--s3) }
  .name-row h1 { margin: 0; min-width: 0; overflow-wrap: anywhere }
  .name-row h1::after { display: none }
  .rename input { font-family: var(--font-display); font-weight: var(--display-weight); font-size: 1.4rem; width: min(28rem, 70vw) }
  .forge { display: inline-flex; align-items: center; gap: var(--s1) }
  .push { flex: 1 }
  .facts-row { display: flex; flex-wrap: wrap; align-items: center; gap: var(--s2) var(--s4); margin-top: var(--s3); padding-top: var(--s3); border-top: 2px solid var(--accent) }
  :global([data-theme='fantasy']) .facts-row { border-top-color: var(--wood) }
  .facts { margin: 0; color: var(--dim) }
  .status { min-width: 4rem; margin: 0 }
  .folder { margin-left: auto; display: inline-flex; align-items: center; gap: var(--s2) }
  .unit { padding: var(--s3) var(--s4) var(--s3) calc(var(--s4) + 6px); margin-bottom: var(--s3); border-left: 6px solid var(--dim) }
  :global([data-theme='fantasy']) .unit { padding-left: calc(var(--s4) + 10px) }
  .unit-head { display: flex; flex-wrap: wrap; align-items: center; gap: var(--s2) var(--s4); margin: 0 0 var(--s2); padding-bottom: var(--s2); border-bottom: 1px solid var(--rule) }
  .unit-head h2 { margin: 0; font-size: 1.05rem; flex: 1; min-width: 0 }
  .count { margin-left: var(--s2); color: var(--dim); font-size: .85em }
  .stats { display: inline-flex; gap: var(--s3) }
  .stat { display: inline-flex; align-items: center; gap: var(--s1) }
  .stat :global(.icon) { color: var(--accent) }
  :global([data-theme='fantasy']) .stat :global(.icon) { color: var(--wood) }
  .slot { display: grid; grid-template-columns: auto minmax(7rem, 11rem) minmax(10rem, 1fr) auto auto; align-items: center; gap: var(--s2) var(--s3); padding: var(--s1) 0 }
  .slot.sub { margin-left: var(--s5) }
  .slot-name { overflow-wrap: anywhere }
  .cell { display: inline-flex; align-items: center; gap: var(--s1); min-width: 0; flex-wrap: wrap }
  .cell :global(.icon) { color: var(--dim) }
  .mesh :global(.picker) { min-width: 0; max-width: 100% }
  .mesh :global(select) { max-width: 100%; min-width: 0 }
  .base { flex-wrap: nowrap; min-width: max-content }
  .base :global(input[type='number']) { width: 4.5rem }
  .tall { flex-wrap: nowrap }
  input.height { width: 5.5rem }
  .unit { overflow: visible }
  .per { margin: var(--s1) 0 0; gap: var(--s1) }
  .chev { display: inline-flex; transition: transform .15s }
  .per[aria-expanded='true'] .chev { transform: rotate(90deg) }
  @media (max-width: 60rem) {
    .slot { grid-template-columns: auto minmax(0, 1fr); }
    .mesh, .mesh :global(.picker), .mesh :global(select) { width: 100% }
    .slot > .cell { grid-column: 2 }
    .base { flex-wrap: wrap; min-width: 0 }
    .folder { margin-left: 0 }
  }
</style>
