<!-- viewer/src/lib/setup/LayoutsSection.svelte -->
<!-- Layouts: the predefined tables and the account's own, loaded as one ReplaceDraft each (asked first); Save as; Delete (the spec's
     addition: Godot has none). -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { LayoutSummary } from '../api/types';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import { builtinLabel, layoutLabel, loadEdit, saveRequest } from './layouts';

  let { draft, api, send, report }: {
    draft: TableState; api: Api; send: (edit: Edit) => Promise<void>; report: (text: string | null) => void;
  } = $props();

  let predefined: TableState[] = $state([]);
  let saved: LayoutSummary[] = $state([]);
  let name = $state('');
  let asking: Question | null = $state(null);

  async function reload() {
    try {
      [predefined, saved] = await Promise.all([api.builtinLayouts(), api.listLayouts()]);
    } catch (e) { report((e as Error).message); }
  }
  $effect(() => { void reload(); });

  const LOAD_TEXT = 'Loading replaces the whole table, size included.';

  function ask(title: string, text: string, ok: string, run: () => Promise<void>) { asking = { title, text, ok, run }; }

  const loadPredefined = (t: TableState) => ask('Load this table?', LOAD_TEXT, 'Load', () => send(loadEdit(t)));
  const loadSaved = (l: LayoutSummary) => ask('Load this layout?', LOAD_TEXT, 'Load', async () => {
    try { await send(loadEdit(await api.getLayout(l.id))); } catch (e) { report((e as Error).message); }
  });
  const remove = (l: LayoutSummary) => ask('Delete this layout?', `Delete the layout ${l.name}?`, 'Delete', async () => {
    try { await api.deleteLayout(l.id); } catch (e) { report((e as Error).message); }
    await reload();
  });

  async function save() {
    const request = saveRequest(draft, name);
    if ('problem' in request) { report(request.problem); return; }
    report(null);
    try {
      await api.saveLayout(request.table);
      report(`Layout ${request.table.name} saved.`);
    } catch (e) { report((e as Error).message); }
    await reload();
  }
</script>

<h3>Predefined tables</h3>
<ul>
  {#each predefined as t (t.id)}
    <li><span class="grow">{builtinLabel(t)}</span><button onclick={() => loadPredefined(t)}>Load</button></li>
  {:else}
    <li class="muted">None.</li>
  {/each}
</ul>

<h3>My layouts</h3>
<ul>
  {#each saved as l (l.id)}
    <li>
      <span class="grow">{layoutLabel(l)}</span>
      <button onclick={() => loadSaved(l)}>Load</button>
      <button onclick={() => remove(l)}>Delete</button>
    </li>
  {:else}
    <li class="muted">No saved layouts.</li>
  {/each}
</ul>

<div class="flow">
  <input placeholder="layout name" maxlength="64" bind:value={name} onkeydown={(e) => { if (e.key === 'Enter') void save(); }} />
  <button onclick={save}>Save as</button>
</div>

<Confirm bind:ask={asking} />

<style>
  h3 { font-size: 0.85rem; margin: 0.5rem 0 0.2rem; }
  ul { list-style: none; margin: 0; padding: 0; }
  li { display: flex; gap: 0.3rem; align-items: center; margin: 0.15rem 0; }
  .grow { flex: 1; min-width: 0; overflow-wrap: anywhere; }
  .flow { display: flex; gap: 0.3rem; margin: 0.3rem 0; }
  input { min-width: 0; flex: 1; }
</style>
