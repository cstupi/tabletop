<!-- viewer/src/lib/setup/LayoutsSection.svelte -->
<!-- Layouts: the predefined tables and the account's own layouts, each loaded as one ReplaceDraft (asked
     first); Save as; Delete. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { LayoutSummary } from '../api/types';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import type { Edit, TableState } from '../protocol/wire.generated';
  import { loadEdit, saveRequest } from './layouts';

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

  function ask(title: string, text: string, ok: string, run: () => Promise<void>, danger = false) { asking = { title, text, ok, run, danger }; }

  const loadPredefined = (t: TableState) => ask(`Load ${t.name ?? 'this table'}?`, LOAD_TEXT, 'Load', () => send(loadEdit(t)));
  const loadSaved = (l: LayoutSummary) => ask(`Load ${l.name}?`, LOAD_TEXT, 'Load', async () => {
    try { await send(loadEdit(await api.getLayout(l.id))); } catch (e) { report((e as Error).message); }
  });
  const remove = (l: LayoutSummary) => ask('Delete this layout?', `Delete the layout ${l.name}? This cannot be undone.`, 'Delete', async () => {
    try { await api.deleteLayout(l.id); } catch (e) { report((e as Error).message); }
    await reload();
  }, true);

  async function save(event: SubmitEvent) {
    event.preventDefault();
    const request = saveRequest(draft, name);
    if ('problem' in request) { report(request.problem); return; }
    report(null);
    try {
      await api.saveLayout(request.table);
      name = '';
    } catch (e) { report((e as Error).message); }
    await reload();
  }

  const sizeOf = (w: number, l: number, terrain: number) => `${w} × ${l}", ${terrain} terrain`;
</script>

<h4>Predefined tables</h4>
<ul>
  {#each predefined as t (t.id)}
    <li>
      <span class="what"><span class="name">{t.name ?? 'Table'}</span><span class="dim">{sizeOf(t.size.width, t.size.length, t.terrain.length)}</span></span>
      <button class="small" onclick={() => loadPredefined(t)}>Load</button>
    </li>
  {:else}
    <li class="dim">None published.</li>
  {/each}
</ul>

<h4>Your layouts</h4>
<ul>
  {#each saved as l (l.id)}
    <li>
      <span class="what"><span class="name">{l.name}</span><span class="dim">{sizeOf(l.size.width, l.size.length, l.terrain)}</span></span>
      <button class="small" onclick={() => loadSaved(l)}>Load</button>
      <button class="quiet" aria-label={`Delete ${l.name}`} onclick={() => remove(l)}>Delete</button>
    </li>
  {:else}
    <li class="dim">No saved layouts yet.</li>
  {/each}
</ul>
<form class="inline" onsubmit={save}>
  <input placeholder="Layout name" aria-label="Layout name" maxlength="64" bind:value={name} />
  <button type="submit" class="small" disabled={name.trim() === ''}>Save</button>
</form>

<Confirm bind:ask={asking} />

<style>
  h4 { margin: var(--space-2) 0 0; font-size: .75rem; font-weight: 600; color: var(--dim); text-transform: uppercase; letter-spacing: .08em; }
  :global([data-theme='fantasy']) h4 { font-family: var(--font-display); text-transform: none; letter-spacing: .03em; font-size: .85rem; color: var(--wood); }
  ul { list-style: none; margin: 0; padding: 0; }
  li { display: flex; gap: var(--space-2); align-items: center; padding: var(--space-1) 0; border-top: 1px solid var(--rule); }
  li:first-child { border-top: 0; }
  li.dim { font-size: .9rem; }
  .what { flex: 1; min-width: 0; display: flex; flex-direction: column; line-height: 1.2; }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .what .dim { font-size: .8rem; }
  .inline { display: flex; gap: var(--space-2); align-items: center; }
  .inline input { flex: 1; min-width: 0; }
</style>
