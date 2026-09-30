<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import type { ArmySummary } from '$lib/api/types';
  import { importErrorMessage, normaliseSource, reimport } from '$lib/armies/import';

  let armies = $state<ArmySummary[]>([]); let next = $state<string | undefined>(); let source = $state(''); let busy = $state(false); let loadingMore = $state(false); let error = $state<string | null>(null);
  const normalised = $derived(normaliseSource(source));

  async function load(more = false) {
    if (more) { if (loadingMore || busy) return; loadingMore = true; }
    try {
      const p = await api.armies.list({ limit: 50, after: more ? next : undefined });
      armies = more ? [...armies, ...p.items] : p.items; next = p.next;
    } catch (e) {
      error = importErrorMessage(e);
    } finally { if (more) loadingMore = false; }
  }
  $effect(() => { void load(); });

  async function doImport() {
    if (!normalised) return;
    busy = true; error = null;
    try { const army = await api.armies.import(normalised); await goto(`/armies/${army.id}`); }
    catch (e) { error = importErrorMessage(e); } finally { busy = false; }
  }
  async function doReimport(a: ArmySummary) {
    if (!confirm(`Re-import "${a.name}" from Army Forge?\n\nThis replaces the army with the list as it is now. Every per-slot mesh, base and height you set is lost; unit defaults are quick to set again.`)) return;
    busy = true; error = null;
    try { const fresh = await reimport(a); await goto(`/armies/${fresh.id}`); } catch (e) { error = importErrorMessage(e); } finally { busy = false; }
  }
  async function remove(a: ArmySummary) {
    if (!confirm(`Delete "${a.name}"? Games already loaded with it keep their own copy.`)) return;
    if (busy || loadingMore) return;
    busy = true;
    try {
      await api.armies.delete(a.id); await load();
    } catch (e) {
      error = importErrorMessage(e);
    } finally { busy = false; }
  }
</script>

<h1>Armies</h1>
<section>
  <label>Army Forge share link or list id <input bind:value={source} placeholder="https://army-forge.onepagerules.com/share?id=…" size="60" /></label>
  <button onclick={doImport} disabled={busy || !normalised}>Import</button>
  {#if source && !normalised}<small>That is not a share link or list id.</small>{/if}
  <p><small>Grimdark Future and Age of Fantasy lists import; the skirmish and regiment variants do not. Management fetches the list; the army then holds its own copy of every value.</small></p>
</section>
{#if error}<p role="alert">{error}</p>{/if}
<table>
  <thead><tr><th>Name</th><th>System</th><th>Units</th><th>Models</th><th></th></tr></thead>
  <tbody>
    {#each armies as a (a.id)}
      <tr><td><a href="/armies/{a.id}">{a.name}</a></td><td>{a.gameSystem}</td><td>{a.units}</td><td>{a.models}</td>
        <td><button onclick={() => doReimport(a)} disabled={busy}>Re-import</button> <button onclick={() => remove(a)} disabled={busy || loadingMore}>Delete</button></td></tr>
    {/each}
  </tbody>
</table>
{#if next}<button onclick={() => load(true)} disabled={loadingMore || busy}>Load more</button>{/if}
