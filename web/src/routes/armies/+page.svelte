<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import type { ArmySummary } from '$lib/api/types';
  import { meshLine, meshPips, systemName } from '$lib/armies/cards';
  import { importErrorMessage, normaliseSource, reimport } from '$lib/armies/import';
  import Icon from '$lib/components/Icon.svelte';
  import Menu from '$lib/components/Menu.svelte';

  let armies = $state<ArmySummary[]>([]); let next = $state<string | undefined>(); let source = $state('');
  let busy = $state<string | null>(null); let loading = $state(true); let loadingMore = $state(false);
  let error = $state<string | null>(null); let notice = $state<string | null>(null);
  const normalised = $derived(normaliseSource(source));
  const ABOUT = 'Grimdark Future and Age of Fantasy lists import; the skirmish and regiment variants do not. The army keeps its own copy of every value. Bases come from Army Forge\'s round base, or its square base where a unit has no round one; each slot\'s base is editable on the army page.';

  async function load(more = false) {
    if (more) { if (loadingMore || busy) return; loadingMore = true; }
    try {
      const p = await api.armies.list({ limit: 50, after: more ? next : undefined });
      armies = more ? [...armies, ...p.items] : p.items; next = p.next;
    } catch (e) {
      error = importErrorMessage(e);
    } finally { loading = false; if (more) loadingMore = false; }
  }
  $effect(() => { void load(); });

  async function doImport(e: SubmitEvent) {
    e.preventDefault();
    if (!normalised || busy) return;
    busy = 'Importing from Army Forge…'; error = null; notice = null;
    try { const army = await api.armies.import(normalised); await goto(`/armies/${army.id}`); }
    catch (err) { error = importErrorMessage(err); } finally { busy = null; }
  }
  async function doReimport(a: ArmySummary) {
    if (!confirm(`Re-import "${a.name}" from Army Forge?\n\nThe army is replaced with the list as it is now. Every mesh, base and height set per slot is lost.`)) return;
    busy = `Re-importing ${a.name}…`; error = null; notice = null;
    try { const fresh = await reimport(a); await goto(`/armies/${fresh.id}`); } catch (e) { error = importErrorMessage(e); } finally { busy = null; }
  }
  async function remove(a: ArmySummary) {
    if (busy || loadingMore) return;
    if (!confirm(`Delete "${a.name}"? Games already loaded with it keep their own copy.`)) return;
    busy = `Deleting ${a.name}…`; error = null; notice = null;
    try {
      await api.armies.delete(a.id); await load(); notice = `Deleted ${a.name}.`;
    } catch (e) {
      error = importErrorMessage(e);
    } finally { busy = null; }
  }
  const plural = (n: number, w: string) => `${n} ${w}${n === 1 ? '' : 's'}`;
</script>

<svelte:head><title>Armies · Tabletop</title></svelte:head>

<h1>Armies</h1>
<form class="import" onsubmit={doImport} title={ABOUT}>
  <label class="field"><Icon name="link" /><span class="visually-hidden">Army Forge share link or list id</span>
    <input bind:value={source} placeholder="Army Forge share link or list id" inputmode="url" autocomplete="off" spellcheck="false"
      aria-invalid={!!source.trim() && !normalised} aria-describedby="source-help" />
  </label>
  <button type="submit" class="primary" disabled={!!busy || !normalised}><Icon name="import" />Import</button>
  <p id="source-help" class="help">{#if source.trim() && !normalised}<small>Not a share link or list id.</small>{:else}<span class="visually-hidden">{ABOUT}</span>{/if}</p>
</form>

<div aria-live="polite">
  {#if busy}<p class="loading">{busy}</p>{/if}
  {#if notice}<p class="notice">{notice}</p>{/if}
</div>
{#if error}<p role="alert">{error}</p>{/if}

{#if loading}
  <p class="loading" role="status">Loading…</p>
{:else if armies.length === 0}
  <div class="empty"><Icon name="armies" size="2.5rem" /><p>No armies</p></div>
{:else}
  <ul class="cards" aria-label="Your armies">
    {#each armies as a (a.id)}
      <li class="panel card">
        <div class="title">
          <span class="system" title={systemName(a.gameSystem)}><Icon name={a.gameSystem === 'age-of-fantasy' ? 'fantasy' : 'grimdark'} size="1.5rem" label={systemName(a.gameSystem)} /></span>
          <h2><a href="/armies/{a.id}">{a.name}</a></h2>
          <span class="chip" title="Edition">{a.edition}</span>
        </div>
        <p class="facts"><span>{plural(a.units, 'unit')} · {plural(a.models, 'model')}</span></p>
        <p class="facts meshes" title="Slots with a mesh">
          <span class="pips" aria-hidden="true">{#each meshPips(a.meshes, a.models) as on, i (i)}<span class:on></span>{/each}</span>
          <span>{meshLine(a.meshes, a.models)}</span>
        </p>
        <div class="foot">
          <a class="button primary" href="/armies/{a.id}">Open</a>
          <span class="end"><Menu label="More for {a.name}">
            <button onclick={() => doReimport(a)} disabled={!!busy}><Icon name="replay" />Re-import</button>
            <button class="danger" onclick={() => remove(a)} disabled={!!busy || loadingMore}><Icon name="remove" />Delete</button>
          </Menu></span>
        </div>
      </li>
    {/each}
  </ul>
  {#if next}<div class="actions"><button onclick={() => load(true)} disabled={loadingMore || !!busy}>{loadingMore ? 'Loading…' : 'Load more'}</button></div>{/if}
{/if}
<style>
  .import { display: flex; flex-wrap: wrap; gap: var(--s2) var(--s3); align-items: center; margin: 0 0 var(--s5) }
  .field { flex: 1 1 22rem; display: flex; align-items: center; gap: var(--s2) }
  .field input { flex: 1; min-width: 0 }
  .help { flex-basis: 100%; margin: 0; min-height: 0 }
  .help:empty { display: none }
  h2 a { color: inherit; text-decoration: none } h2 a:hover { text-decoration: underline }
  .title h2 { flex: 1 }
  .system { display: inline-flex }
</style>
