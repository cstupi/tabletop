<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import type { ArmySummary, BaseSet } from '$lib/api/types';
  import { importErrorMessage, normaliseSource, reimport } from '$lib/armies/import';
  import Icon from '$lib/components/Icon.svelte';

  let armies = $state<ArmySummary[]>([]); let next = $state<string | undefined>(); let source = $state(''); let bases = $state<BaseSet>('round');
  let busy = $state<string | null>(null); let loading = $state(true); let loadingMore = $state(false);
  let error = $state<string | null>(null); let notice = $state<string | null>(null);
  const normalised = $derived(normaliseSource(source));

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
    try { const army = await api.armies.import(normalised, bases); await goto(`/armies/${army.id}`); }
    catch (err) { error = importErrorMessage(err); } finally { busy = null; }
  }
  async function doReimport(a: ArmySummary) {
    if (!confirm(`Re-import "${a.name}" from Army Forge on ${bases} bases?\n\nThis replaces the army with the list as it is now. Every per-slot mesh, base and height you set is lost; unit defaults are quick to set again.`)) return;
    busy = `Re-importing ${a.name}…`; error = null; notice = null;
    try { const fresh = await reimport(a, bases); await goto(`/armies/${fresh.id}`); } catch (e) { error = importErrorMessage(e); } finally { busy = null; }
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
</script>

<svelte:head><title>Armies · Tabletop</title></svelte:head>

<h1>Armies</h1>
<section class="panel" aria-labelledby="import-heading">
  <h2 id="import-heading">Import from Army Forge</h2>
  <form class="stack" onsubmit={doImport}>
    <label>Army Forge share link or list id
      <input bind:value={source} placeholder="https://army-forge.onepagerules.com/share?id=…" inputmode="url" autocomplete="off" spellcheck="false"
        aria-invalid={!!source.trim() && !normalised} aria-describedby="source-help" />
    </label>
    <p id="source-help"><small>{source.trim() && !normalised ? 'That is not a share link or list id. In Army Forge, use Share and copy the link.' : 'In Army Forge, open the list, choose Share and paste the link here.'}</small></p>
    <fieldset><legend>Bases</legend>
      <label><input type="radio" bind:group={bases} value="round" /> Round</label>
      <label><input type="radio" bind:group={bases} value="square" /> Square</label>
    </fieldset>
    <div class="actions"><button type="submit" class="primary" disabled={!!busy || !normalised}><Icon name="import" />Import</button></div>
    <p><small>Grimdark Future and Age of Fantasy lists import; the skirmish and regiment variants do not. The army keeps its own copy of every value. Army Forge publishes a round and a square base for every unit; the choice applies to the whole army, and to Re-import.</small></p>
  </form>
</section>

<div aria-live="polite">
  {#if busy}<p class="loading">{busy}</p>{/if}
  {#if notice}<p class="notice">{notice}</p>{/if}
</div>
{#if error}<p role="alert">{error}</p>{/if}

<section class="panel" aria-labelledby="list-heading">
  <h2 id="list-heading">Your armies</h2>
  {#if loading}
    <p class="loading" role="status">Loading…</p>
  {:else if armies.length === 0}
    <div class="empty"><Icon name="armies" size="2.5rem" /><p>No armies yet. Import one from Army Forge above.</p></div>
  {:else}
    <div class="scroll-x">
      <table>
        <thead><tr><th scope="col">Name</th><th scope="col">System</th><th scope="col" class="num">Units</th><th scope="col" class="num">Models</th><th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
        <tbody>
          {#each armies as a (a.id)}
            <tr><th scope="row"><a href="/armies/{a.id}">{a.name}</a></th><td>{a.gameSystem}</td><td class="num">{a.units}</td><td class="num">{a.models}</td>
              <td class="row-actions"><button onclick={() => doReimport(a)} disabled={!!busy} aria-label="Re-import {a.name}">Re-import</button> <button class="danger" onclick={() => remove(a)} disabled={!!busy || loadingMore} aria-label="Delete {a.name}">Delete</button></td></tr>
          {/each}
        </tbody>
      </table>
    </div>
    {#if next}<div class="actions"><button onclick={() => load(true)} disabled={loadingMore || !!busy}>{loadingMore ? 'Loading…' : 'Load more'}</button></div>{/if}
  {/if}
</section>
<style>
  tbody th a { font-weight: 700 } td { vertical-align: middle } tbody th { vertical-align: middle }
  .row-actions { white-space: nowrap; text-align: right } .row-actions button { min-height: 2rem; padding-inline: var(--s3) }
</style>
